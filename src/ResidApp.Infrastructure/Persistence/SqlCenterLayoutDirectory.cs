using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>Historia 2 (0029): edificios y plantas del centro, activos e inactivos, para quien tiene su ámbito de Administración vigente. No
/// dependen de las unidades del ámbito (son del centro, como el catálogo de turnos); los recuentos de unidades activas son solo números.</summary>
public sealed class SqlCenterLayoutDirectory(SqlConnectionFactory connections) : ICenterLayoutDirectory
{
    public async Task<IReadOnlyList<LayoutBuilding>> ListBuildingsAsync(AccountAdministrationAccess access, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var buildings = (await connection.QueryAsync<BuildingRow>(new CommandDefinition("""
            SELECT building.id AS BuildingId, building.nombre_visible AS Name,
                   CAST(CASE WHEN building.estado = 'ACTIVE' THEN 1 ELSE 0 END AS BIT) AS Active,
                   (SELECT COUNT(*) FROM dbo.unidades unit
                     WHERE unit.centro_id = building.centro_id AND unit.edificio_id = building.id AND unit.estado = 'ACTIVE') AS ActiveUnits
              FROM dbo.edificios building
             WHERE building.centro_id = @CenterId
             ORDER BY building.estado, building.nombre_visible
            """, new { CenterId = access.CenterId.Value }, cancellationToken: ct))).ToList();
        var floors = (await connection.QueryAsync<FloorRow>(new CommandDefinition("""
            SELECT fl.edificio_id AS BuildingId, fl.id AS FloorId, fl.nombre_visible AS Name,
                   CAST(CASE WHEN fl.estado = 'ACTIVE' THEN 1 ELSE 0 END AS BIT) AS Active,
                   (SELECT COUNT(*) FROM dbo.unidades unit
                     WHERE unit.centro_id = fl.centro_id AND unit.planta_id = fl.id AND unit.estado = 'ACTIVE') AS ActiveUnits
              FROM dbo.plantas fl
             WHERE fl.centro_id = @CenterId
             ORDER BY fl.estado, fl.nombre_visible
            """, new { CenterId = access.CenterId.Value }, cancellationToken: ct))).ToList();
        return buildings.Select(b => new LayoutBuilding(
            b.BuildingId, b.Name, b.Active, b.ActiveUnits,
            floors.Where(f => f.BuildingId == b.BuildingId).Select(f => new LayoutFloor(f.FloorId, f.Name, f.Active, f.ActiveUnits)).ToList())).ToList();
    }

    private sealed record BuildingRow(Guid BuildingId, string Name, bool Active, int ActiveUnits);

    private sealed record FloorRow(Guid BuildingId, Guid FloorId, string Name, bool Active, int ActiveUnits);
}
