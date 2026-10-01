using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// ADM-03 (script 0021): corrección de la identidad administrativa. En una transacción bloquea el residente
/// (UPDLOCK), comprueba que el número de correcciones es el que tenía el formulario (si no, otra corrección se ha
/// adelantado: conflicto) y que algo cambia, y después:
/// <list type="bullet">
/// <item>registra la corrección, con los valores anteriores y los nuevos;</item>
/// <item>actualiza residentes, que TR_res_identity_guard solo acepta si coincide con esa última corrección;</item>
/// <item>deja RESIDENT_IDENTITY_CORRECT en dbo.eventos_auditoria, sin los valores.</item>
/// </list>
/// </summary>
public sealed class SqlResidentIdentityRepository(SqlConnectionFactory connections) : IResidentIdentityRepository
{
    public async Task<int> CorrectAsync(CorrectResidentIdentityInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var occurredAt = DateTimeOffset.UtcNow;
        var parameters = new { ResidentId = input.ResidentId.Value, CenterId = input.CenterId.Value };
        var current = await connection.QuerySingleOrDefaultAsync<CurrentRow>(new CommandDefinition("""
            SELECT resident.nombre_visible AS DisplayName, resident.fecha_nacimiento AS BirthDate, resident.sexo_documentado_codigo AS SexCode,
                   (SELECT COUNT(*) FROM dbo.residentes_identidad_correcciones c
                     WHERE c.residente_id = resident.id AND c.centro_id = resident.centro_id) AS Corrections
              FROM dbo.residentes resident WITH (UPDLOCK, ROWLOCK)
             WHERE resident.id = @ResidentId AND resident.centro_id = @CenterId
            """, parameters, transaction, cancellationToken: ct))
            ?? throw new AccessDeniedException();
        if (current.Corrections != input.ExpectedCorrections)
        {
            throw new DomainValidationException("RESIDENT_IDENTITY_CORRECTION_CONFLICT");
        }

        var before = new ResidentIdentity(
            current.DisplayName, DateOnly.FromDateTime(current.BirthDate), EnumCode.ParseCode<DocumentedSexCode>(current.SexCode));
        if (before.SameAs(input.Identity))
        {
            throw new DomainValidationException(ResidentIdentityCorrection.InvalidCode);
        }

        var after = input.Identity;
        var number = current.Corrections + 1;
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.residentes_identidad_correcciones
                    (id, residente_id, centro_id, numero, nombre_anterior, fecha_nacimiento_anterior, sexo_anterior_codigo,
                     nombre_nuevo, fecha_nacimiento_nueva, sexo_nuevo_codigo, motivo, corregido_por_cuenta_id, corregido_por_perfil, corregido_en)
                VALUES (@Id, @ResidentId, @CenterId, @Number, @NameBefore, @BirthDateBefore, @SexBefore,
                        @NameAfter, @BirthDateAfter, @SexAfter, @Reason, @AccountId, 'ADMINISTRACION', @OccurredAt);

                UPDATE dbo.residentes
                   SET nombre_visible = @NameAfter, fecha_nacimiento = @BirthDateAfter, sexo_documentado_codigo = @SexAfter
                 WHERE id = @ResidentId AND centro_id = @CenterId;

                INSERT INTO dbo.eventos_auditoria
                    (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
                VALUES (@AuditId, @AccountId, 'ADMINISTRACION', @CenterId, @UnitId, @ResidentId, 'RESIDENT', @ResidentId,
                        'RESIDENT_IDENTITY_CORRECT', @OccurredAt);
                """, new
            {
                Id = Guid.NewGuid(), AuditId = Guid.NewGuid(), ResidentId = input.ResidentId.Value, CenterId = input.CenterId.Value,
                UnitId = input.UnitId.Value, Number = number,
                NameBefore = before.DisplayName, BirthDateBefore = before.BirthDate, SexBefore = before.DocumentedSex.ToCode(),
                NameAfter = after.DisplayName, BirthDateAfter = after.BirthDate, SexAfter = after.DocumentedSex.ToCode(),
                input.Reason, AccountId = input.AccountId.Value, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            // UX_ric_numero: otra corrección registró el mismo número entre la lectura y la inserción.
            throw new DomainValidationException("RESIDENT_IDENTITY_CORRECTION_CONFLICT");
        }

        transaction.Commit();
        return number;
    }

    private sealed record CurrentRow(string DisplayName, DateTime BirthDate, string SexCode, int Corrections);
}
