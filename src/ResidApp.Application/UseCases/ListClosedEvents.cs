using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ListClosedEventsCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, SystemProfile Perfil);

/// <summary>
/// HIS-01 (ENF-23/MED-22): eventos cerrados de un residente en su Historial, con la instantánea de basal y
/// ubicación de su fecha (HIS-03). Común a Enfermería y Medicina, como ListScopeResidents: cada controlador pasa
/// su perfil y el ámbito activo tiene que ser de ese perfil. La visibilidad es la de las bandejas
/// (SqlChangeInboxDirectory.ScopedEventsFrom): Medicina solo ve los escalados y sus eventos propios.
/// </summary>
public sealed class ListClosedEvents(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<ClosedEventSummary>>> ExecuteAsync(
        ListClosedEventsCommand command, CancellationToken ct = default) =>
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

            return await directory.ListClosedEventsAsync(command.AmbitoPerfilId, command.CentroId, command.ResidenteId, ct);
        });
}
