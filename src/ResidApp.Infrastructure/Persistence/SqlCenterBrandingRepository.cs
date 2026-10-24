using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Nombre y logo del propio centro (script 0068). Cada escritura va en una transacción que vuelve a comprobar que quien gestiona tiene su
/// ámbito de Administración vigente (la misma comprobación que SqlProfessionalAccountRepository), bloquea la fila del centro (UPDLOCK), como
/// el resto de cambios de estructura, y deja su evento en dbo.eventos_auditoria, sin la imagen.
/// </summary>
public sealed class SqlCenterBrandingRepository(SqlConnectionFactory connections) : ICenterBrandingRepository
{
    public async Task RenameAsync(AccountAdministrationAccess access, string name, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        await SqlCenterStructureRepository.LockCenterAsync(connection, transaction, access, ct);
        var current = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT nombre_visible FROM dbo.centros WHERE id = @CenterId",
            new { CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        if (string.Equals(current, name, StringComparison.Ordinal))
        {
            throw new DomainValidationException(NewCenter.InvalidCode);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.centros SET nombre_visible = @Name WHERE id = @CenterId",
            new { Name = name, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(
            connection, transaction, access, "CENTER", access.CenterId.Value, "CENTER_RENAME", DateTimeOffset.UtcNow, ct: ct);
        transaction.Commit();
    }

    public async Task SetLogoAsync(AccountAdministrationAccess access, CenterLogoImage logo, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        await SqlCenterStructureRepository.LockCenterAsync(connection, transaction, access, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.logos_centro
               SET tipo = @Type, contenido = @Content, actualizado_por_cuenta_id = @AccountId, actualizado_por_perfil = 'ADMINISTRACION',
                   actualizado_en = @OccurredAt
             WHERE centro_id = @CenterId;

            IF @@ROWCOUNT = 0
                INSERT INTO dbo.logos_centro (centro_id, tipo, contenido, actualizado_por_cuenta_id, actualizado_por_perfil, actualizado_en)
                VALUES (@CenterId, @Type, @Content, @AccountId, 'ADMINISTRACION', @OccurredAt);
            """, new
        {
            CenterId = access.CenterId.Value, logo.Type, logo.Content, AccountId = access.AccountId.Value, OccurredAt = occurredAt,
        }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(
            connection, transaction, access, "CENTER", access.CenterId.Value, "CENTER_LOGO_SET", occurredAt, ct: ct);
        transaction.Commit();
    }

    public async Task<bool> RemoveLogoAsync(AccountAdministrationAccess access, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        await SqlCenterStructureRepository.LockCenterAsync(connection, transaction, access, ct);
        var removed = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.logos_centro WHERE centro_id = @CenterId",
            new { CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        if (removed > 0)
        {
            await SqlProfessionalAccountRepository.AuditAsync(
                connection, transaction, access, "CENTER", access.CenterId.Value, "CENTER_LOGO_REMOVE", DateTimeOffset.UtcNow, ct: ct);
        }
        transaction.Commit();
        return removed > 0;
    }
}
