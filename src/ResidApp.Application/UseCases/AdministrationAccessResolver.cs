using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>El ámbito activo de Administración de la cuenta de la sesión: lo comparten todos los servicios de Administración.</summary>
public sealed class AdministrationAccessResolver(IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session)
{
    /// <summary>El ámbito activo de Administración de la cuenta de la sesión y su sujeto externo; si no lo es, acceso denegado.</summary>
    public async Task<(AccountAdministrationAccess Access, string Subject)> ResolveAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
        var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
        var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == profileScopeId && s.CenterId == centerId);
        if (scope is null || scope.Profile != SystemProfile.Administracion)
        {
            throw new AccessDeniedException();
        }

        return (new AccountAdministrationAccess(scope.ProfileScopeId, scope.AccountId, scope.CenterId), identity.ExternalSubject);
    }
}
