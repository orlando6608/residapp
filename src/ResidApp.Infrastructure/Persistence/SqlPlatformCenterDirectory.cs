using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>Los centros del sistema distintos del reservado, para el operador de plataforma. Solo datos del centro: código,
/// nombre, estado y número de unidades; nada de residentes.</summary>
public sealed class SqlPlatformCenterDirectory(SqlConnectionFactory connections) : IPlatformCenterDirectory
{
    public async Task<IReadOnlyList<PlatformCenterSummary>> ListAsync(PlatformAccess access, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlPlatformCenterRepository.EnsureOperatorAsync(connection, null, access, ct);
        var rows = await connection.QueryAsync<CenterRow>(new CommandDefinition("""
            SELECT center.id AS CenterId, center.codigo AS Code, center.nombre_visible AS Name,
                   CAST(CASE WHEN center.estado = 'ACTIVE' THEN 1 ELSE 0 END AS BIT) AS Active,
                   (SELECT COUNT(*) FROM dbo.unidades unit WHERE unit.centro_id = center.id) AS UnitCount,
                   center.creado_en AS CreatedAt
              FROM dbo.centros center
             WHERE center.id <> @PlatformCenterId
             ORDER BY center.nombre_visible, center.codigo
            """, new { PlatformCenterId = PlatformCenter.Id }, cancellationToken: ct));
        return rows.Select(row => new PlatformCenterSummary(
            CenterId.From(row.CenterId), row.Code, row.Name, row.Active, row.UnitCount,
            new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)))).ToList();
    }

    private sealed record CenterRow(Guid CenterId, string Code, string Name, bool Active, int UnitCount, DateTime CreatedAt);
}
