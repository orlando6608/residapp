using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Application.Sessions;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>dbo.sesiones (0061). Sin seguridad por filas, como dbo.cuentas: se lee en cada petición antes de que haya ámbito activo.
/// Tocar y cerrar solo actúan sobre sesiones abiertas, así que una sesión cerrada no cambia (el trigger de 0061 lo garantiza).</summary>
public sealed class SqlSessionStore(SqlConnectionFactory connections) : ISessionStore
{
    public async Task OpenAsync(Guid sessionId, AccountId accountId, DateTime now, DateTime expiresAt, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.sesiones (id, cuenta_id, creada_en, caduca_en, ultima_actividad_en)
            VALUES (@Id, @AccountId, @Now, @ExpiresAt, @Now)
            """, new { Id = sessionId, AccountId = accountId.Value, Now = now, ExpiresAt = expiresAt }, cancellationToken: ct));
    }

    public async Task<StoredSession?> FindAsync(Guid sessionId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
            SELECT session.id AS Id, account.sujeto_externo AS ExternalSubject, account.estado AS AccountStatus,
                   session.caduca_en AS ExpiresAt, session.ultima_actividad_en AS LastActivityAt, session.cerrada_en AS ClosedAt
              FROM dbo.sesiones session
              JOIN dbo.cuentas account ON account.id = session.cuenta_id
             WHERE session.id = @Id
            """, new { Id = sessionId }, cancellationToken: ct));
        return row is null
            ? null
            : new StoredSession(row.Id, row.ExternalSubject, row.AccountStatus == "ACTIVE", row.ExpiresAt, row.LastActivityAt, row.ClosedAt);
    }

    public async Task TouchAsync(Guid sessionId, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.sesiones SET ultima_actividad_en = @Now
             WHERE id = @Id AND cerrada_en IS NULL AND ultima_actividad_en < @Now
            """, new { Id = sessionId, Now = now }, cancellationToken: ct));
    }

    public async Task CloseAsync(Guid sessionId, SessionCloseReason reason, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.sesiones SET cerrada_en = @Now, motivo_cierre = @Reason
             WHERE id = @Id AND cerrada_en IS NULL
            """, new { Id = sessionId, Now = now, Reason = reason.ToCode() }, cancellationToken: ct));
    }

    public async Task CloseAllAsync(AccountId accountId, SessionCloseReason reason, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.sesiones SET cerrada_en = @Now, motivo_cierre = @Reason
             WHERE cuenta_id = @AccountId AND cerrada_en IS NULL
            """, new { AccountId = accountId.Value, Now = now, Reason = reason.ToCode() }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<OpenSession>> ListOpenAsync(AccountId accountId, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return (await connection.QueryAsync<OpenSession>(new CommandDefinition("""
            SELECT session.id AS Id, session.creada_en AS CreatedAt, session.ultima_actividad_en AS LastActivityAt,
                   session.caduca_en AS ExpiresAt, entry.ip AS Ip
              FROM dbo.sesiones session
              OUTER APPLY (SELECT TOP 1 ip FROM dbo.eventos_acceso WITH (NOLOCK)
                            WHERE sesion_id = session.id AND tipo IN ('ENTRADA', 'ENTRADA_DESARROLLO', 'ENTRADA_PASSKEY')
                            ORDER BY ocurrido_en) entry
             WHERE session.cuenta_id = @AccountId AND session.cerrada_en IS NULL AND session.caduca_en > @Now
             ORDER BY session.ultima_actividad_en DESC
            """, new { AccountId = accountId.Value, Now = now }, cancellationToken: ct))).ToList();
    }

    public async Task<bool> CloseOwnedAsync(
        AccountId accountId, Guid sessionId, SessionCloseReason reason, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.sesiones SET cerrada_en = @Now, motivo_cierre = @Reason
             WHERE id = @Id AND cuenta_id = @AccountId AND cerrada_en IS NULL
            """, new { Id = sessionId, AccountId = accountId.Value, Now = now, Reason = reason.ToCode() }, cancellationToken: ct)) > 0;
    }

    public async Task CloseOthersAsync(
        AccountId accountId, Guid keepSessionId, SessionCloseReason reason, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.sesiones SET cerrada_en = @Now, motivo_cierre = @Reason
             WHERE cuenta_id = @AccountId AND id <> @KeepId AND cerrada_en IS NULL
            """, new { AccountId = accountId.Value, KeepId = keepSessionId, Now = now, Reason = reason.ToCode() }, cancellationToken: ct));
    }

    private sealed record Row(Guid Id, string ExternalSubject, string AccountStatus, DateTime ExpiresAt, DateTime LastActivityAt, DateTime? ClosedAt);
}
