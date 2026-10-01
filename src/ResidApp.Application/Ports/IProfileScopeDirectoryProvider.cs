using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Un ámbito activo (centro + perfil) que una cuenta puede seleccionar para operar. AccountId no
/// se expone nunca en la cookie de ámbito activo (ActiveProfileScopeCookie) — solo se usa en el servidor,
/// dentro de casos de uso que necesitan dejar autoría (p. ej. RegisterDailyClosure). AccountDisplayName es el nombre
/// visible de la cuenta (0023), null si nadie se lo ha puesto.</summary>
public sealed record ActiveProfileScope(
    Guid ProfileScopeId, AccountId AccountId, CenterId CenterId, string CenterName, SystemProfile Profile,
    string? AccountDisplayName = null);

/// <summary>
/// Lista los ambitos_perfil ACTIVOS de una cuenta, sin resolver ningún AuthorizationTarget. A diferencia de
/// IAuthorizationEvidenceProvider (que valida un par ProfileScopeId/CenterId ya conocido contra un target
/// concreto), este puerto los descubre: es lo que alimenta la pantalla de selección de ámbito activo.
/// </summary>
public interface IProfileScopeDirectoryProvider
{
    Task<IReadOnlyList<ActiveProfileScope>> ListActiveAsync(string externalSubject, CancellationToken ct = default);

    /// <summary>Unidades concedidas (sin revocar y activas) a un ámbito activo de la cuenta, con las mismas condiciones
    /// con las que SqlAuthorizationEvidenceProvider autoriza el alta en una unidad. Vacía si el ámbito no es suyo.</summary>
    Task<IReadOnlyList<ScopeUnit>> ListUnitsAsync(
        string externalSubject, Guid profileScopeId, CenterId centerId, CancellationToken ct = default);

    /// <summary>Códigos de los permisos vigentes de un ámbito activo de la cuenta (cuenta, ámbito y centro activos, como
    /// ListActiveAsync), para decidir qué fichas enseña el Inicio. Vacía si el ámbito no es suyo.</summary>
    Task<IReadOnlyList<string>> ListPermissionsAsync(
        string externalSubject, Guid profileScopeId, CenterId centerId, CancellationToken ct = default);
}

public sealed record ScopeUnit(UnitId UnitId, string Name);
