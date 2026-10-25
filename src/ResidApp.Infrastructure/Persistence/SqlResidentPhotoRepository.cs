using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Foto de identificación del residente (scripts 0059 y 0071). Cada cambio va en una transacción que bloquea el residente (UPDLOCK), como la
/// corrección de identidad, para que dos cambios a la vez no se crucen, vuelve a comprobar el permiso de Enfermería y deja su evento en
/// dbo.eventos_auditoria, sin la imagen.
/// </summary>
public sealed class SqlResidentPhotoRepository(SqlConnectionFactory connections) : IResidentPhotoRepository
{
    public const string RefusedCode = "RESIDENT_PHOTO_REFUSED";

    public async Task<byte[]?> ReadAsync(CenterId centerId, ResidentId residentId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<byte[]>(new CommandDefinition("""
            SELECT contenido FROM dbo.fotos_residente WHERE residente_id = @ResidentId AND centro_id = @CenterId
            """, new { ResidentId = residentId.Value, CenterId = centerId.Value }, cancellationToken: ct));
    }

    public async Task<ResidentPhotoStatus> ReadStatusAsync(CenterId centerId, ResidentId residentId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var row = await connection.QuerySingleAsync<StatusRow>(new CommandDefinition("""
            SELECT photo.actualizada_en AS UpdatedAt, photo.consentimiento_firmante_codigo AS SignerCode, photo.consentimiento_fecha AS SignedOn,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.fotos_residente_negativas refusal
                                           WHERE refusal.residente_id = @ResidentId AND refusal.centro_id = @CenterId) THEN 1 ELSE 0 END AS BIT) AS Refused
              FROM (SELECT 1 AS one) dummy
              LEFT JOIN dbo.fotos_residente photo ON photo.residente_id = @ResidentId AND photo.centro_id = @CenterId
            """, new { ResidentId = residentId.Value, CenterId = centerId.Value }, cancellationToken: ct));
        return new ResidentPhotoStatus(
            row.UpdatedAt is { } updated ? new DateTimeOffset(DateTime.SpecifyKind(updated, DateTimeKind.Utc)) : null,
            row.SignerCode is { } signer && row.SignedOn is { } signedOn
                ? new ResidentPhotoConsent(EnumCode.ParseCode<ResidentPhotoConsentSigner>(signer), DateOnly.FromDateTime(signedOn))
                : null,
            row.Refused);
    }

    public async Task SetAsync(ResidentPhotoTarget target, byte[] content, ResidentPhotoConsent consent, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await LockResidentAsync(connection, transaction, target, ct);
        var refused = await connection.ExecuteScalarAsync<int?>(new CommandDefinition("""
            SELECT 1 FROM dbo.fotos_residente_negativas WHERE residente_id = @ResidentId AND centro_id = @CenterId
            """, new { ResidentId = target.ResidentId.Value, CenterId = target.CenterId.Value }, transaction, cancellationToken: ct));
        if (refused is not null)
        {
            throw new DomainValidationException(RefusedCode);
        }

        var parameters = new DynamicParameters(Parameters(target, "RESIDENT_PHOTO_SET", content));
        parameters.Add("Signer", consent.Signer.ToCode());
        parameters.Add("SignedOn", consent.SignedOn.ToDateTime(TimeOnly.MinValue));
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.fotos_residente
               SET contenido = @Content, actualizada_por_cuenta_id = @AccountId, actualizada_por_perfil = @Profile, actualizada_en = @OccurredAt,
                   consentimiento_firmante_codigo = @Signer, consentimiento_fecha = @SignedOn
             WHERE residente_id = @ResidentId AND centro_id = @CenterId;

            IF @@ROWCOUNT = 0
                INSERT INTO dbo.fotos_residente
                    (residente_id, centro_id, contenido, actualizada_por_cuenta_id, actualizada_por_perfil, actualizada_en,
                     consentimiento_firmante_codigo, consentimiento_fecha)
                VALUES (@ResidentId, @CenterId, @Content, @AccountId, @Profile, @OccurredAt, @Signer, @SignedOn);
            """ + AuditInsert, parameters, transaction, cancellationToken: ct));
        transaction.Commit();
    }

    public async Task<bool> RemoveAsync(ResidentPhotoTarget target, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await LockResidentAsync(connection, transaction, target, ct);
        var removed = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            DeletePhoto, Parameters(target, "RESIDENT_PHOTO_REMOVE", null), transaction, cancellationToken: ct));
        transaction.Commit();
        return removed > 0;
    }

    public async Task<bool> MarkRefusalAsync(ResidentPhotoTarget target, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await LockResidentAsync(connection, transaction, target, ct);
        var marked = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            IF EXISTS (SELECT 1 FROM dbo.fotos_residente_negativas WHERE residente_id = @ResidentId AND centro_id = @CenterId)
                SELECT 0;
            ELSE
            BEGIN
                INSERT INTO dbo.fotos_residente_negativas (residente_id, centro_id, anotada_por_cuenta_id, anotada_por_perfil, anotada_en)
                VALUES (@ResidentId, @CenterId, @AccountId, @Profile, @OccurredAt);
            """ + AuditInsert + """

                SELECT 1;
            END;
            """, Parameters(target, "RESIDENT_PHOTO_REFUSAL_SET", null), transaction, cancellationToken: ct));
        if (marked > 0)
        {
            // La marca sustituye a la foto: si había una, se borra con su propio evento.
            await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                DeletePhoto, Parameters(target, "RESIDENT_PHOTO_REMOVE", null), transaction, cancellationToken: ct));
        }

        transaction.Commit();
        return marked > 0;
    }

    public async Task<bool> ClearRefusalAsync(ResidentPhotoTarget target, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await LockResidentAsync(connection, transaction, target, ct);
        var cleared = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            DELETE FROM dbo.fotos_residente_negativas WHERE residente_id = @ResidentId AND centro_id = @CenterId;
            DECLARE @Cleared INT = @@ROWCOUNT;
            IF @Cleared > 0
            BEGIN
            """ + AuditInsert + """

            END;
            SELECT @Cleared;
            """, Parameters(target, "RESIDENT_PHOTO_REFUSAL_CLEAR", null), transaction, cancellationToken: ct));
        transaction.Commit();
        return cleared > 0;
    }

    /// <summary>Borra la foto y, si había una, deja el evento indicado (@Action). Devuelve las filas borradas. También la usa la baja.</summary>
    internal const string DeletePhoto = """
        DELETE FROM dbo.fotos_residente WHERE residente_id = @ResidentId AND centro_id = @CenterId;
        DECLARE @Removed INT = @@ROWCOUNT;
        IF @Removed > 0
        BEGIN
            INSERT INTO dbo.eventos_auditoria
                (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (NEWID(), @AccountId, @Profile, @CenterId, @UnitId, @ResidentId, 'RESIDENT', @ResidentId, @Action, @OccurredAt);
        END;
        SELECT @Removed;
        """;

    private const string AuditInsert = """

        INSERT INTO dbo.eventos_auditoria
            (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
        VALUES (@AuditId, @AccountId, @Profile, @CenterId, @UnitId, @ResidentId, 'RESIDENT', @ResidentId, @Action, @OccurredAt);
        """;

    private static object Parameters(ResidentPhotoTarget target, string action, byte[]? content) => new
    {
        AuditId = Guid.NewGuid(), AccountId = target.AccountId.Value, Profile = target.Profile.ToCode(), CenterId = target.CenterId.Value,
        UnitId = target.UnitId.Value, ResidentId = target.ResidentId.Value, Action = action, Content = content,
        OccurredAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Bloquea al residente activo y, para Enfermería, vuelve a comprobar el permiso RESIDENT_PHOTO_MANAGE de su ámbito: si Administración
    /// se lo retiró después de la comprobación del caso de uso, no pasa.</summary>
    private static async Task LockResidentAsync(SqlConnection connection, SqlTransaction transaction, ResidentPhotoTarget target, CancellationToken ct)
    {
        var active = await connection.ExecuteScalarAsync<int?>(new CommandDefinition("""
            SELECT 1 FROM dbo.residentes WITH (UPDLOCK, ROWLOCK)
             WHERE id = @ResidentId AND centro_id = @CenterId AND estado = 'ACTIVE'
            """, new { ResidentId = target.ResidentId.Value, CenterId = target.CenterId.Value }, transaction, cancellationToken: ct));
        if (active is null)
        {
            throw new AccessDeniedException();
        }

        if (target.Profile != SystemProfile.Enfermeria)
        {
            return;
        }

        var allowed = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*)
              FROM dbo.permisos_perfil permission
             WHERE permission.ambito_perfil_id = @ProfileScopeId AND permission.centro_id = @CenterId
               AND permission.permiso_codigo = @Permission AND permission.revocado_en IS NULL
            """, new { target.ProfileScopeId, CenterId = target.CenterId.Value, Permission = ProfilePermissions.ResidentPhotoManage },
            transaction, cancellationToken: ct));
        if (allowed == 0)
        {
            throw new AccessDeniedException();
        }
    }

    private sealed record StatusRow(DateTime? UpdatedAt, string? SignerCode, DateTime? SignedOn, bool Refused);
}
