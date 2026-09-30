using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ListOpenEventsCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, SystemProfile Perfil);

/// <summary>
/// ENF-18/MED-20: eventos abiertos de un residente en su ficha. Gemelo de ListClosedEvents: común a Enfermería y
/// Medicina, cada controlador pasa su perfil y el ámbito activo tiene que ser de ese perfil. La visibilidad es la de
/// las bandejas (SqlChangeInboxDirectory.ScopedEventsFrom): Medicina solo ve los escalados y sus eventos propios.
/// </summary>
public sealed class ListOpenEvents(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<OpenEventSummary>>> ExecuteAsync(
        ListOpenEventsCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var identity = await session.GetVerifiedIdentityAsync(ct);
            if (identity is null)
            {
                throw new AccessDeniedException();
            }

            var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
            var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == command.AmbitoPerfilId && s.CenterId == command.CentroId);
            if (scope is null || scope.Profile != command.Perfil
                || command.Perfil is not (SystemProfile.Enfermeria or SystemProfile.Medicina))
            {
                throw new AccessDeniedException();
            }

            return await directory.ListOpenEventsAsync(command.AmbitoPerfilId, command.CentroId, command.ResidenteId, ct);
        });
}
