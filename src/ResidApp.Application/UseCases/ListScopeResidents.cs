using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ListScopeResidentsCommand(Guid AmbitoPerfilId, CenterId CentroId);

/// <summary>
/// Traduce ENF-17 "Lista de residentes". Igual que ListAssignedResidents (Auxiliar), valida directamente
/// con IProfileScopeDirectoryProvider que el ámbito pertenece a la cuenta autenticada y que su perfil es
/// ENFERMERIA — el listado ya viene acotado por construcción (IEnfermeriaResidentDirectory) al criterio de
/// ámbito por defecto o restringido.
/// </summary>
public sealed class ListScopeResidents(
    IProfileScopeDirectoryProvider scopes, IEnfermeriaResidentDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<ScopeResidentSummary>>> ExecuteAsync(
        ListScopeResidentsCommand command, CancellationToken ct = default) =>
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

            return await directory.ListAsync(command.AmbitoPerfilId, command.CentroId, ct);
        });
}
