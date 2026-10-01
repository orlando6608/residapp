using ResidApp.Shared;

namespace ResidApp.Domain.Structure;

/// <summary>
/// Historia 2 (script 0029): reglas de los nombres de edificios, plantas, habitaciones y plazas. Todos llevan de 1 a 200 caracteres
/// tras recortar; el código de error dice de cuál se trata. Edificios y plantas son del centro; habitaciones y plazas, de una unidad.
/// </summary>
public static class CenterLayout
{
    public const int MaxNameLength = 200;
    public const string BuildingInvalidCode = "BUILDING_INVALID";
    public const string FloorInvalidCode = "FLOOR_INVALID";
    public const string RoomInvalidCode = "ROOM_INVALID";
    public const string PlaceInvalidCode = "PLACE_INVALID";

    public static string ValidateBuildingName(string? name) => Validate(name, BuildingInvalidCode);

    public static string ValidateFloorName(string? name) => Validate(name, FloorInvalidCode);

    public static string ValidateRoomName(string? name) => Validate(name, RoomInvalidCode);

    public static string ValidatePlaceName(string? name) => Validate(name, PlaceInvalidCode);

    private static string Validate(string? name, string invalidCode)
    {
        var text = name?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > MaxNameLength ? throw new DomainValidationException(invalidCode) : text;
    }
}
