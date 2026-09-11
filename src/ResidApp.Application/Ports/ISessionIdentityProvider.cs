using ResidApp.Application.Authorization;

namespace ResidApp.Application.Ports;

/// <summary>Traduce SessionIdentityProvider de lib/session/session-provider.ts: puerto neutral que
/// entrega únicamente un sujeto externo verificado. No transporta roles, permisos, centro, unidad,
/// residente ni perfil activo — eso lo resuelve siempre IAuthorizationEvidenceProvider en D1/SQL Server.</summary>
public sealed record VerifiedIdentity(string ExternalSubject);

public interface ISessionIdentityProvider
{
    Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default);
}

/// <summary>Traduce loadAuthorizationEvidence de db/repositories/authorization-subject-repository.ts.</summary>
public interface IAuthorizationEvidenceProvider
{
    Task<AuthorizationEvidence?> LoadEvidenceAsync(
        string externalSubject, AuthorizationSelection selection, AuthorizationTarget target, CancellationToken ct = default);
}
