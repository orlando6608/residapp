using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Comprobación de ámbito común a los casos de uso de Medicina que escriben o listan (deny-by-default):
/// identidad verificada, ámbito activo de Medicina en el centro y, si se indica, evento escalado visible para
/// ese ámbito.</summary>
internal static class MedicinaScope
{
    public static async Task<ActiveProfileScope> RequireAsync(
        IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, IChangeInboxDirectory directory,
        Guid ambitoPerfilId, CenterId centroId, Guid? eventoId, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
        var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
        var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == ambitoPerfilId && s.CenterId == centroId);
        if (scope is null || scope.Profile != SystemProfile.Medicina)
        {
            throw new AccessDeniedException();
        }
        if (eventoId is { } id && await directory.FindAsync(ambitoPerfilId, centroId, id, ct) is null)
        {
            throw new AccessDeniedException();
        }
        return scope;
    }
}
