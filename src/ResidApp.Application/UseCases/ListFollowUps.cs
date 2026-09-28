using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ListFollowUpsCommand(Guid AmbitoPerfilId, CenterId CentroId);

/// <summary>ENF-08: bandeja compartida de seguimientos abiertos del ámbito, vencidos incluidos (nunca se
/// ocultan ni se cierran solos).</summary>
public sealed class ListFollowUps(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<FollowUpSummary>>> ExecuteAsync(
        ListFollowUpsCommand command, CancellationToken ct = default) =>
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

            return await directory.ListFollowUpsAsync(command.AmbitoPerfilId, command.CentroId, ct);
        });
}
