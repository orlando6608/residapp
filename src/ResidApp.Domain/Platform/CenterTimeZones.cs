using ResidApp.Shared;

namespace ResidApp.Domain.Platform;

// Zona horaria por centro (script 0109; estudio docs/producto/zonas-horarias-por-centro-estudio.md). Todo se guarda en UTC: la zona solo decide cómo se muestran las horas y qué es «hoy».

/// <summary>Una zona del catálogo que ofrece Plataforma: el nombre estándar (<c>Europe/Madrid</c>) y la etiqueta que ve la persona.</summary>
public sealed record CenterTimeZone(string Code, string Label);

/// <summary>Por qué Plataforma corrige la zona de un centro (lista cerrada, sin texto libre). El alta del centro no es una corrección: lleva el motivo ALTA en la base de datos.</summary>
public enum CenterTimeZoneChangeReason
{
    [Code("ERROR_DE_ALTA")] ErrorDeAlta,
    [Code("CAMBIO_DE_UBICACION")] CambioDeUbicacion,
}

/// <summary>Lo que se vería afectado al cambiar la zona: las franjas de citas activas (se guardan como hora local, así que sus horas reales se mueven) y las citas futuras
/// sin cancelar (se guardan en UTC y no se tocan, pero se verán a otra hora). Son recuentos, no identifican a nadie.</summary>
public sealed record CenterTimeZoneImpact(int Slots, int Appointments);

/// <summary>Una fila de la historia de zonas de un centro. Sin la cuenta de quien la fijó: Plataforma ve qué se cambió y por qué, no una cuenta.</summary>
public sealed record CenterTimeZoneChange(int Number, string ZoneCode, string ZoneLabel, string Reason, CenterTimeZoneImpact Confirmed, DateTimeOffset ChangedAt);

/// <summary>La zona vigente de un centro, su historia (la más reciente primero) y lo que afectaría cambiarla ahora.</summary>
public sealed record CenterTimeZoneState(CenterTimeZone Current, IReadOnlyList<CenterTimeZoneChange> History, CenterTimeZoneImpact Impact);

public static class CenterTimeZoneRules
{
    public const string InvalidZone = "CENTER_TIMEZONE_INVALID";
    public const string InvalidReason = "CENTER_TIMEZONE_REASON_INVALID";

    /// <summary>La zona de un centro sin fila en <c>zonas_centro</c> (los que ya existían) y la de Plataforma.</summary>
    public const string DefaultZoneCode = CenterTime.DefaultZoneId;

    /// <summary>La zona elegida tiene que ser una del catálogo activo.</summary>
    public static string ValidateZone(string? code, IEnumerable<CenterTimeZone> catalog) =>
        !string.IsNullOrWhiteSpace(code) && catalog.Any(zone => zone.Code == code) ? code : throw new DomainValidationException(InvalidZone);

    public static CenterTimeZoneChangeReason ValidateReason(CenterTimeZoneChangeReason? reason) =>
        reason is { } value && Enum.IsDefined(value) ? value : throw new DomainValidationException(InvalidReason);
}
