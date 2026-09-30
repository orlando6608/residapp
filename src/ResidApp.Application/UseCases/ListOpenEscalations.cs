using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ListOpenEscalationsCommand(Guid AmbitoPerfilId, CenterId CentroId);

/// <summary>Escalados abiertos de Enfermería: los eventos que la Enfermería de sus unidades escaló a Medicina y siguen
/// abiertos, que ya no están en sus bandejas. Compartida por la unidad, como el resto de bandejas (decisión del
/// usuario, 2026-09-30); marca los que escaló la propia cuenta.</summary>
public sealed class ListOpenEscalations(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<OpenEscalationSummary>>> ExecuteAsync(
        ListOpenEscalationsCommand command, CancellationToken ct = default) =>
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

            return await directory.ListOpenEscalationsAsync(command.AmbitoPerfilId, command.CentroId, ct);
        });
}
