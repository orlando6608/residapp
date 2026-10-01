using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Historia 2 (0029): una ubicación que se puede elegir al dar de alta a un residente dentro de una unidad: una habitación (PlaceId null)
/// o una plaza libre de una habitación.</summary>
public sealed record LocationOption(UnitId UnitId, string UnitName, Guid RoomId, string RoomName, Guid? PlaceId, string? PlaceName);

/// <summary>Historia 2: las habitaciones activas y las plazas activas y libres de las unidades activas del ámbito activo de la cuenta de la sesión
/// (Administración o Enfermería), para el selector del alta. Solo orienta el formulario: el alta vuelve a validar la elección.</summary>
public interface ILocationOptionsDirectory
{
    Task<IReadOnlyList<LocationOption>> ListAsync(string externalSubject, Guid profileScopeId, CenterId centerId, CancellationToken ct = default);
}
