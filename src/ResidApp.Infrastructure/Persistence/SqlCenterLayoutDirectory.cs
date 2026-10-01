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

    public async Task<IReadOnlyList<LayoutRoom>> ListRoomsAsync(AccountAdministrationAccess access, UnitId unitId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var parameters = new { UnitId = unitId.Value, CenterId = access.CenterId.Value, access.ProfileScopeId };
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition($"""
                SELECT COUNT(*) FROM dbo.unidades unit
                 WHERE unit.id = @UnitId AND unit.centro_id = @CenterId AND {SqlSchedulingDirectory.AdministratorUnitGrant("unit.id")}
                """, parameters, cancellationToken: ct)) == 0)
        {
            throw new Application.Errors.AccessDeniedException();
        }

        var rooms = (await connection.QueryAsync<RoomRow>(new CommandDefinition("""
            SELECT room.id AS RoomId, room.nombre_visible AS Name, CAST(CASE WHEN room.estado = 'ACTIVE' THEN 1 ELSE 0 END AS BIT) AS Active,
                   (SELECT COUNT(*) FROM dbo.intervalos_ubicacion_residente location
                     WHERE location.centro_id = room.centro_id AND location.habitacion_id = room.id AND location.vigente_hasta IS NULL) AS CurrentResidents
              FROM dbo.habitaciones room
             WHERE room.centro_id = @CenterId AND room.unidad_id = @UnitId
             ORDER BY room.estado, room.nombre_visible
            """, parameters, cancellationToken: ct))).ToList();
        var places = (await connection.QueryAsync<PlaceRow>(new CommandDefinition("""
            SELECT place.habitacion_id AS RoomId, place.id AS PlaceId, place.nombre_visible AS Name,
                   CAST(CASE WHEN place.estado = 'ACTIVE' THEN 1 ELSE 0 END AS BIT) AS Active,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.intervalos_ubicacion_residente location
                                           WHERE location.centro_id = place.centro_id AND location.plaza_id = place.id
                                             AND location.vigente_hasta IS NULL) THEN 1 ELSE 0 END AS BIT) AS Occupied
              FROM dbo.plazas place
             WHERE place.centro_id = @CenterId AND place.unidad_id = @UnitId
             ORDER BY place.estado, place.nombre_visible
            """, parameters, cancellationToken: ct))).ToList();
        return rooms.Select(r => new LayoutRoom(
            r.RoomId, r.Name, r.Active, r.CurrentResidents,
            places.Where(p => p.RoomId == r.RoomId).Select(p => new LayoutPlace(p.PlaceId, p.Name, p.Active, p.Occupied)).ToList())).ToList();
    }

    private sealed record RoomRow(Guid RoomId, string Name, bool Active, int CurrentResidents);

    private sealed record PlaceRow(Guid RoomId, Guid PlaceId, string Name, bool Active, bool Occupied);

    private sealed record BuildingRow(Guid BuildingId, string Name, bool Active, int ActiveUnits);

    private sealed record FloorRow(Guid BuildingId, Guid FloorId, string Name, bool Active, int ActiveUnits);
}
