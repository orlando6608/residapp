using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Un ámbito activo (centro + perfil) que una cuenta puede seleccionar para operar. AccountId no
/// se expone nunca en la cookie de ámbito activo (ActiveProfileScopeCookie) — solo se usa en el servidor,
/// dentro de casos de uso que necesitan dejar autoría (p. ej. RegisterDailyClosure).</summary>
public sealed record ActiveProfileScope(Guid ProfileScopeId, AccountId AccountId, CenterId CenterId, string CenterName, SystemProfile Profile);

/// <summary>
/// Lista los ambitos_perfil ACTIVOS de una cuenta, sin resolver ningún AuthorizationTarget. A diferencia de
/// IAuthorizationEvidenceProvider (que valida un par ProfileScopeId/CenterId ya conocido contra un target
/// concreto), este puerto los descubre: es lo que alimenta la pantalla de selección de ámbito activo.
/// </summary>
public interface IProfileScopeDirectoryProvider
{
    Task<IReadOnlyList<ActiveProfileScope>> ListActiveAsync(string externalSubject, CancellationToken ct = default);
}
