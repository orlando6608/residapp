using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>
/// Zona horaria por centro (script 0109): el catálogo que ofrece Plataforma y la zona del centro del ámbito activo. Sin datos personales. La zona del ámbito se comprueba en SQL contra la
/// cuenta de la sesión (la cookie de ámbito no es de fiar, ADR 0008): quien no tiene ese ámbito no obtiene nada y la aplicación usa la hora de la península.
/// </summary>
public interface ICenterTimeZoneDirectory
{
    /// <summary>Las zonas que Plataforma ofrece al crear un centro o corregir su zona, en orden de lista.</summary>
    Task<IReadOnlyList<CenterTimeZone>> ListCatalogAsync(CancellationToken ct = default);

    /// <summary>El código de la zona del centro del ámbito activo de esa cuenta (<c>Europe/Madrid</c> si el centro no tiene fila: los que ya existían y Plataforma), o null si el ámbito no
    /// es de la cuenta, no está vigente o la cuenta está inactiva.</summary>
    Task<string?> FindZoneCodeForScopeAsync(string externalSubject, Guid profileScopeId, CancellationToken ct = default);
}

/// <summary>
/// La zona de un centro vista y corregida por Plataforma (script 0109). La tabla es de solo añadir: la zona vigente es la última fila, y un centro sin fila usa la de la península.
/// Corregir es una sola transacción que comprueba de nuevo el ámbito de Plataforma, bloquea la fila del centro, vuelve a contar lo afectado y deja la auditoría
/// <c>CENTER_TIMEZONE_CHANGE</c> sin datos.
/// </summary>
public interface ICenterTimeZoneRepository
{
    /// <summary>La zona vigente de un centro, su historia y lo que afectaría cambiarla ahora. El centro de Plataforma o uno que no existe: CENTER_TIMEZONE_CENTER_INVALID.</summary>
    Task<CenterTimeZoneState> GetAsync(PlatformAccess access, CenterId centerId, CancellationToken ct = default);

    /// <summary>
    /// Cambia la zona de un centro. <paramref name="confirmed"/> es lo que Plataforma vio y confirmó: si al guardar los recuentos son otros, CENTER_TIMEZONE_IMPACT_CHANGED. La misma zona
    /// que ya tiene: CENTER_TIMEZONE_CONFLICT. Un centro que aún no tenía fila recibe antes su fila de alta con la zona de la península.
    /// </summary>
    Task ChangeAsync(
        PlatformAccess access, CenterId centerId, string zoneCode, CenterTimeZoneChangeReason reason, CenterTimeZoneImpact confirmed, CancellationToken ct = default);
}
