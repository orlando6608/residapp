using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Foto de identificación del residente (script 0059). Poner y quitar van en una transacción que bloquea el residente (UPDLOCK), como la
/// corrección de identidad, para que dos cambios a la vez no se crucen, y deja su evento en dbo.eventos_auditoria, sin la imagen.
/// </summary>
public sealed class SqlResidentPhotoRepository(SqlConnectionFactory connections) : IResidentPhotoRepository
{
    public async Task<byte[]?> ReadAsync(CenterId centerId, ResidentId residentId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<byte[]>(new CommandDefinition("""
            SELECT contenido FROM dbo.fotos_residente WHERE residente_id = @ResidentId AND centro_id = @CenterId
            """, new { ResidentId = residentId.Value, CenterId = centerId.Value }, cancellationToken: ct));
    }

    public async Task SetAsync(ResidentPhotoTarget target, byte[] content, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await LockResidentAsync(connection, transaction, target, ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.fotos_residente
               SET contenido = @Content, actualizada_por_cuenta_id = @AccountId, actualizada_por_perfil = @Profile, actualizada_en = @OccurredAt
             WHERE residente_id = @ResidentId AND centro_id = @CenterId;

            IF @@ROWCOUNT = 0
                INSERT INTO dbo.fotos_residente
                    (residente_id, centro_id, contenido, actualizada_por_cuenta_id, actualizada_por_perfil, actualizada_en)
                VALUES (@ResidentId, @CenterId, @Content, @AccountId, @Profile, @OccurredAt);
            """ + AuditInsert, Parameters(target, "RESIDENT_PHOTO_SET", content), transaction, cancellationToken: ct));
        transaction.Commit();
    }

    public async Task<bool> RemoveAsync(ResidentPhotoTarget target, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await LockResidentAsync(connection, transaction, target, ct);
        var removed = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            DELETE FROM dbo.fotos_residente WHERE residente_id = @ResidentId AND centro_id = @CenterId;
            DECLARE @Removed INT = @@ROWCOUNT;
            IF @Removed > 0
            BEGIN
            """ + AuditInsert + """

            END;
            SELECT @Removed;
            """, Parameters(target, "RESIDENT_PHOTO_REMOVE", null), transaction, cancellationToken: ct));
        transaction.Commit();
        return removed > 0;
    }

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
    }
}
