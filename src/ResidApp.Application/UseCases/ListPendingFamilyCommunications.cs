using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ListPendingFamilyCommunicationsCommand(Guid AmbitoPerfilId, CenterId CentroId);

/// <summary>Comunicaciones familiares preparadas al cerrar un evento en los últimos 30 días, con su estado de publicación,
/// dentro del mismo ámbito que las bandejas (tarjeta "Comunicaciones" de ENF-01).</summary>
public sealed class ListPendingFamilyCommunications(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<PendingFamilyCommunicationSummary>>> ExecuteAsync(
        ListPendingFamilyCommunicationsCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var identity = await session.GetVerifiedIdentityAsync(ct);
            if (identity is null)
            {
                throw new AccessDeniedException();
            }

            var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
            var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == command.AmbitoPerfilId && s.CenterId == command.CentroId);
            if (scope is null || scope.Profile != SystemProfile.Enfermeria)
            {
                throw new AccessDeniedException();
            }

            return await directory.ListPendingFamilyCommunicationsAsync(command.AmbitoPerfilId, command.CentroId, ct);
        });
}
