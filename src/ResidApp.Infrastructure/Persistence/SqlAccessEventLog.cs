using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>dbo.eventos_acceso (0062): inmutable y sin seguridad por filas, como dbo.sesiones.</summary>
public sealed class SqlAccessEventLog(SqlConnectionFactory connections) : IAccessEventLog
{
    public async Task RecordAsync(
        AccessEventType type, AccountId? accountId, Guid? sessionId, string? ip, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.eventos_acceso (id, ocurrido_en, cuenta_id, tipo, sesion_id, ip)
            VALUES (NEWID(), @Now, @AccountId, @Type, @SessionId, @Ip)
            """, new { Now = now, AccountId = accountId?.Value, Type = type.ToCode(), SessionId = sessionId, Ip = ip }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AccessEventEntry>> ListRecentAsync(AccountId accountId, int take, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<(DateTime OccurredAt, string Type, string? Ip)>(new CommandDefinition("""
            SELECT TOP (@Take) ocurrido_en AS OccurredAt, tipo AS Type, ip AS Ip
              FROM dbo.eventos_acceso WITH (NOLOCK)
             WHERE cuenta_id = @AccountId
             ORDER BY ocurrido_en DESC
            """, new { Take = take, AccountId = accountId.Value }, cancellationToken: ct));
        return rows.Select(row => new AccessEventEntry(row.OccurredAt, EnumCode.ParseCode<AccessEventType>(row.Type), row.Ip)).ToList();
    }
}
