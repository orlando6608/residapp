using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record LoadBaselineDraftCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId);

/// <summary>ENF-20/ENF-21/ENF-22: lee el borrador activo propio (o null si no hay ninguno, o el que hay no
/// es propio). No pasa por RequestAuthorizationContextResolver (pensado para Create/Read/Sign/Draft de un
/// residente ya conocido, no para "continuar editando lo que ya empecé"): resuelve la cuenta/perfil del
/// ámbito activo igual que ListScopeResidents, y deja que IBaselineRepository re-compruebe propiedad y
/// permiso vigente antes de devolver nada.</summary>
public sealed class LoadBaselineDraft(
    IProfileScopeDirectoryProvider scopes, IBaselineRepository repository, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<BaselineDraftDetail?>> ExecuteAsync(
        LoadBaselineDraftCommand command, CancellationToken ct = default) =>
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
            return await repository.LoadOwnedDraftAsync(owner, ct);
        });
}
