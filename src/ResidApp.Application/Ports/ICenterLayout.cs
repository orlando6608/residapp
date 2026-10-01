using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Historia 2 (0029): una planta de un edificio. ActiveUnits son las unidades activas del centro que están en ella; mientras haya
/// alguna, no se puede inactivar.</summary>
public sealed record LayoutFloor(Guid FloorId, string Name, bool Active, int ActiveUnits);

/// <summary>Historia 2 (0029): un edificio del centro con sus plantas. ActiveUnits son las unidades activas del centro que están en él.</summary>
public sealed record LayoutBuilding(Guid BuildingId, string Name, bool Active, int ActiveUnits, IReadOnlyList<LayoutFloor> Floors);

/// <summary>Historia 2 (0029): edificios y plantas del centro (no dependen de las unidades del ámbito de quien consulta, como el catálogo de
/// turnos). Los recuentos son solo números.</summary>
public interface ICenterLayoutDirectory
{
    Task<IReadOnlyList<LayoutBuilding>> ListBuildingsAsync(AccountAdministrationAccess access, CancellationToken ct = default);
}

/// <summary>
/// Historia 2 (0029): escrituras sobre edificios, plantas y la colocación de las unidades. Cada una va en una transacción que repite el
/// ámbito de Administración, bloquea la fila del centro y deja su evento en dbo.eventos_auditoria, sin datos. Nada se borra. Colocar una
/// unidad exige que esté en el ámbito de quien gestiona.
/// </summary>
public interface ICenterLayoutRepository
{
    /// <summary>Crea el edificio (con OperationId como id). Reenviar la misma operación devuelve el ya creado.</summary>
    Task<Guid> CreateBuildingAsync(AccountAdministrationAccess access, Guid operationId, string name, CancellationToken ct = default);

    Task RenameBuildingAsync(AccountAdministrationAccess access, Guid buildingId, string name, CancellationToken ct = default);

    /// <summary>Inactiva (Active = false) o reactiva. Inactivar con plantas o unidades activas es inválido.</summary>
    Task ChangeBuildingStatusAsync(AccountAdministrationAccess access, Guid buildingId, bool active, CancellationToken ct = default);

    /// <summary>Crea la planta en un edificio activo (con OperationId como id).</summary>
    Task<Guid> CreateFloorAsync(AccountAdministrationAccess access, Guid operationId, Guid buildingId, string name, CancellationToken ct = default);

    Task RenameFloorAsync(AccountAdministrationAccess access, Guid floorId, string name, CancellationToken ct = default);

    /// <summary>Inactiva o reactiva (reactivar exige el edificio activo). Inactivar con unidades activas es inválido.</summary>
    Task ChangeFloorStatusAsync(AccountAdministrationAccess access, Guid floorId, bool active, CancellationToken ct = default);

    /// <summary>Coloca la unidad (del ámbito) en un edificio y, si se da, en una de sus plantas; null en ambos la quita del edificio.</summary>
    Task SetUnitLocationAsync(AccountAdministrationAccess access, UnitId unitId, Guid? buildingId, Guid? floorId, CancellationToken ct = default);
}
