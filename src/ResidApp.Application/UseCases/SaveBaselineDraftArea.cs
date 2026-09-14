using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Baseline.Answers;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record SaveBaselineDraftAreaCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, BaselineArea AreaCode, IBaselineAreaAnswer Respuesta, string? Observacion);

/// <summary>ENF-20: guarda la respuesta de una de las nueve áreas del borrador activo propio. La respuesta
/// llega ya construida como el record de dominio correcto (Answers/*.cs), así que sus reglas cruzadas ya
/// se validaron al construirla, antes de llegar aquí.</summary>
public sealed class SaveBaselineDraftArea(IProfileScopeDirectoryProvider scopes, IBaselineRepository repository, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<bool>> ExecuteAsync(SaveBaselineDraftAreaCommand command, CancellationToken ct = default) =>
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
            await repository.SaveAreaAsync(new SaveBaselineDraftAreaInput(owner, command.AreaCode, command.Respuesta, command.Observacion), ct);
            return true;
        });
}
