using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Rangos de referencia de constantes por centro. El permiso REFERENCE_RANGES_MANAGE se comprueba en SQL
/// sobre el ámbito concreto (mismo patrón que CLINICAL_DETAIL_READ en SqlBaselineRepository). La versión es
/// el número de filas de historial del centro; al guardar se lee con UPDLOCK/HOLDLOCK para que dos
/// guardados simultáneos no pasen ambos la comprobación.
/// </summary>
public sealed class SqlReferenceRangeRepository(SqlConnectionFactory connections) : IReferenceRangeRepository
{
    public async Task<ReferenceRangesView> ReadAsync(ReferenceRangesAccess access, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await EnsurePermissionAsync(connection, null, access, ct);

        var ranges = await ReadRangesAsync(connection, null, access.CenterId, ct);
        var history = (await connection.QueryAsync<HistoryRow>(new CommandDefinition("""
            SELECT constante_codigo AS Code, minimo_anterior AS PreviousMin, maximo_anterior AS PreviousMax,
                   minimo_nuevo AS NewMin, maximo_nuevo AS NewMax, cambiado_por_perfil AS ProfileCode,
                   CAST(CASE WHEN cambiado_por_cuenta_id = @AccountId THEN 1 ELSE 0 END AS BIT) AS ByCurrentAccount,
                   cambiado_en AS ChangedAt
              FROM dbo.rangos_referencia_constantes_historial
             WHERE centro_id = @CenterId
             ORDER BY cambiado_en DESC, constante_codigo
            """, new { AccountId = access.AccountId.Value, CenterId = access.CenterId.Value }, cancellationToken: ct)))
            .Select(h => new ReferenceRangeChange(
                EnumCode.ParseCode<VitalSignCode>(h.Code), h.PreviousMin, h.PreviousMax, h.NewMin, h.NewMax,
                EnumCode.ParseCode<SystemProfile>(h.ProfileCode), h.ByCurrentAccount, new DateTimeOffset(h.ChangedAt, TimeSpan.Zero)))
            .ToList();

        return new ReferenceRangesView(ranges, history, history.Count);
    }

    public async Task<int> SaveAsync(SaveReferenceRangesInput input, CancellationToken ct = default)
    {
        var access = input.Access;
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await EnsurePermissionAsync(connection, transaction, access, ct);

        var version = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.rangos_referencia_constantes_historial WITH (UPDLOCK, HOLDLOCK) WHERE centro_id = @CenterId
            """, new { CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        if (version != input.ExpectedVersion)
        {
            throw new DomainValidationException("REFERENCE_RANGES_REVISION_CONFLICT");
        }

        var current = (await ReadRangesAsync(connection, transaction, access.CenterId, ct)).ToDictionary(r => r.Code);
        var desired = input.Ranges.ToDictionary(r => r.Code);
        var occurredAt = DateTimeOffset.UtcNow;
        var changes = 0;

        foreach (var code in Enum.GetValues<VitalSignCode>())
        {
            var before = current.GetValueOrDefault(code);
            var after = desired.GetValueOrDefault(code);
            if (before?.Min == after?.Min && before?.Max == after?.Max)
            {
                continue;
            }

            var parameters = new
            {
                CenterId = access.CenterId.Value, Code = code.ToCode(), NewMin = after?.Min, NewMax = after?.Max,
                PreviousMin = before?.Min, PreviousMax = before?.Max, AccountId = access.AccountId.Value,
                Profile = access.ActiveProfile.ToCode(), OccurredAt = occurredAt, Id = Guid.NewGuid(),
            };
            var write = (before, after) switch
            {
                (null, _) => "INSERT INTO dbo.rangos_referencia_constantes (centro_id, constante_codigo, minimo, maximo) VALUES (@CenterId, @Code, @NewMin, @NewMax);",
                (_, null) => "DELETE FROM dbo.rangos_referencia_constantes WHERE centro_id = @CenterId AND constante_codigo = @Code;",
                _ => "UPDATE dbo.rangos_referencia_constantes SET minimo = @NewMin, maximo = @NewMax WHERE centro_id = @CenterId AND constante_codigo = @Code;",
            };
            await connection.ExecuteAsync(new CommandDefinition(write + """

                INSERT INTO dbo.rangos_referencia_constantes_historial
                    (id, centro_id, constante_codigo, minimo_anterior, maximo_anterior, minimo_nuevo, maximo_nuevo,
                     cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
                VALUES (@Id, @CenterId, @Code, @PreviousMin, @PreviousMax, @NewMin, @NewMax, @AccountId, @Profile, @OccurredAt);
                """, parameters, transaction, cancellationToken: ct));
            changes++;
        }

        if (changes > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.eventos_auditoria (id, cuenta_id, perfil_activo, centro_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
                VALUES (@Id, @AccountId, @Profile, @CenterId, 'REFERENCE_RANGES', @CenterId, 'REFERENCE_RANGES_UPDATE', @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = access.AccountId.Value, Profile = access.ActiveProfile.ToCode(),
                CenterId = access.CenterId.Value, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));
        }

        transaction.Commit();
        return version + changes;
    }

    private static async Task EnsurePermissionAsync(
        SqlConnection connection, SqlTransaction? transaction, ReferenceRangesAccess access, CancellationToken ct)
    {
        var allowed = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*)
              FROM dbo.ambitos_perfil profile
              JOIN dbo.permisos_perfil permission ON permission.ambito_perfil_id = profile.id AND permission.centro_id = profile.centro_id
                   AND permission.permiso_codigo = 'REFERENCE_RANGES_MANAGE' AND permission.revocado_en IS NULL
             WHERE profile.id = @ProfileScopeId AND profile.cuenta_id = @AccountId AND profile.centro_id = @CenterId
               AND profile.perfil_codigo = @Profile AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
            """, new
        {
            access.ProfileScopeId, AccountId = access.AccountId.Value, CenterId = access.CenterId.Value, Profile = access.ActiveProfile.ToCode(),
        }, transaction, cancellationToken: ct));
        if (allowed == 0)
        {
            throw new DomainValidationException("REFERENCE_RANGES_NOT_AUTHORIZED");
        }
    }

    private static async Task<IReadOnlyList<VitalSignRange>> ReadRangesAsync(
        SqlConnection connection, SqlTransaction? transaction, CenterId centerId, CancellationToken ct) =>
        (await connection.QueryAsync<RangeRow>(new CommandDefinition("""
            SELECT constante_codigo AS Code, minimo AS Min, maximo AS Max FROM dbo.rangos_referencia_constantes WHERE centro_id = @CenterId
            """, new { CenterId = centerId.Value }, transaction, cancellationToken: ct)))
        .Select(r => new VitalSignRange(EnumCode.ParseCode<VitalSignCode>(r.Code), r.Min, r.Max))
        .ToList();

    private sealed record RangeRow(string Code, decimal? Min, decimal? Max);

    private sealed record HistoryRow(
        string Code, decimal? PreviousMin, decimal? PreviousMax, decimal? NewMin, decimal? NewMax, string ProfileCode,
        bool ByCurrentAccount, DateTime ChangedAt);
}
