using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ListAssignedResidentsCommand(Guid AmbitoPerfilId, CenterId CentroId);

/// <summary>
/// Traduce AUX-01 "Mis residentes". A diferencia del resto del vertical Residente/Basal, no pasa por
/// RequestAuthorizationContext (pensado para una operación sobre un ResidentId ya conocido): aquí se
/// valida directamente, con IProfileScopeDirectoryProvider, que el ámbito de perfil pertenece a la cuenta
/// autenticada y que su perfil es AUXILIAR — el listado en sí ya viene acotado por construcción a lo
/// asignado (IAssignedResidentDirectory), sin revelar residentes fuera de esa asignación.
/// </summary>
public sealed class ListAssignedResidents(
    IProfileScopeDirectoryProvider scopes, IAssignedResidentDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<AssignedResidentSummary>>> ExecuteAsync(
        ListAssignedResidentsCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var identity = await session.GetVerifiedIdentityAsync(ct);
            if (identity is null)
            {
                throw new AccessDeniedException();
            }

            var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
            var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == command.AmbitoPerfilId && s.CenterId == command.CentroId);
            if (scope is null || scope.Profile != SystemProfile.Auxiliar)
            {
                throw new AccessDeniedException();
            }

            return await directory.ListAsync(command.AmbitoPerfilId, command.CentroId, ct);
        });
}
