using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record EscalateClinicalEventCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, int Revision, string? Motivo);

/// <summary>
/// ENF-06/ENF-09 "escalar a Medicina", una de las cuatro salidas de la decisión asistencial, desde la
/// valoración o desde un seguimiento. Transmite la información ya reunida y el motivo, sin ningún resumen
/// automático; no cierra el evento. Mismo criterio de ámbito que cerrar el evento.
/// </summary>
public sealed class EscalateClinicalEvent(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory,
    ISessionIdentityProvider session, INursingAssessmentRepository repository)
{
    public Task<ApplicationResult<int>> ExecuteAsync(EscalateClinicalEventCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var reason = new EscalationReason(command.Motivo);

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
            if (await directory.FindAsync(command.AmbitoPerfilId, command.CentroId, command.EventoId, ct) is null)
            {
                throw new AccessDeniedException();
            }

            return await repository.EscalateAsync(new EscalateClinicalEventInput(
                scope.AccountId, command.CentroId, command.EventoId, command.Revision, reason), ct);
        });
}
