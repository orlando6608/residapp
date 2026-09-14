using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record CancelBaselineDraftCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, string Motivo);

/// <summary>ENF-20 "cancelar el borrador propio con motivo": solo quien lo creó, con el mismo perfil,
/// puede cancelarlo (D1-P05 deja sin resolver cualquier cancelación excepcional más allá de esto).</summary>
public sealed class CancelBaselineDraft(IProfileScopeDirectoryProvider scopes, IBaselineRepository repository, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<bool>> ExecuteAsync(CancelBaselineDraftCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(command.Motivo))
            {
                throw new DomainValidationException("BASELINE_DRAFT_CANCELLATION_REASON_REQUIRED");
            }

            var identity = await session.GetVerifiedIdentityAsync(ct);
            if (identity is null)
            {
                throw new AccessDeniedException();
            }

            var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
            var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == command.AmbitoPerfilId && s.CenterId == command.CentroId);
            if (scope is null || scope.Profile is not (SystemProfile.Enfermeria or SystemProfile.Medicina))
            {
                throw new AccessDeniedException();
            }

            var owner = new OwnedActiveDraftInput(scope.AccountId, scope.Profile, command.CentroId, command.ResidenteId);
            await repository.CancelDraftAsync(new CancelBaselineDraftInput(owner, command.Motivo), ct);
            return true;
        });
}
