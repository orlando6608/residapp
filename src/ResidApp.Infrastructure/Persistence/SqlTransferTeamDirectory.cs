using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>Equipos activos de la unidad del evento, solo si el evento es visible para el ámbito (mismo predicado que la bandeja).</summary>
public sealed class SqlTransferTeamDirectory(SqlConnectionFactory connections) : ITransferTeamDirectory
{
    public async Task<IReadOnlyList<TransferTeam>> ListAsync(Guid profileScopeId, CenterId centerId, Guid eventId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<TransferTeam>(new CommandDefinition($"""
            SELECT team.id AS TeamId, team.nombre_visible AS Name
              FROM dbo.equipos team
             WHERE team.centro_id = @CenterId AND team.estado = 'ACTIVE'
               AND team.unidad_id = (SELECT TOP 1 ea.unidad_id
                {SqlChangeInboxDirectory.ScopedEventsFrom}
                   AND ea.id = @EventId)
             ORDER BY team.nombre_visible
            """, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value, EventId = eventId }, cancellationToken: ct));
        return rows.ToList();
    }
}
