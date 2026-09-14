using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Baseline;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record SaveBaselineDraftBarthelCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, DateOnly FechaValoracion, IReadOnlyList<BarthelItem> Items);

/// <summary>ENF-21: guarda los diez ítems de Barthel del borrador activo propio. AwardedScore de cada
/// BarthelItem ya se deriva del catálogo (BarthelCatalog.ScoreOf) antes de llegar aquí, nunca se acepta
/// como dato de entrada independiente.</summary>
public sealed class SaveBaselineDraftBarthel(IProfileScopeDirectoryProvider scopes, IBaselineRepository repository, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<bool>> ExecuteAsync(SaveBaselineDraftBarthelCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
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
            await repository.SaveBarthelAsync(new SaveBaselineDraftBarthelInput(owner, command.FechaValoracion, command.Items), ct);
            return true;
        });
}
