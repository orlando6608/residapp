using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record CloseClinicalEventCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, int Revision, Guid OperacionId,
    FamilyCommunicationDecision? Comunicacion, FamilyCommunicationType? TipoComunicacion, string? TextoComunicacion);

/// <summary>
/// ENF-06/ENF-07A "cerrar el evento", una de las cuatro salidas de la decisión asistencial, con la decisión
/// explícita de comunicación familiar (ENF-12, ENF-14, ENF-15). Mismo criterio de ámbito que empezar y
/// guardar la valoración. El cierre es idempotente y el evento cerrado sale de las bandejas.
/// </summary>
public sealed class CloseClinicalEvent(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory,
    ISessionIdentityProvider session, INursingAssessmentRepository repository)
{
    public Task<ApplicationResult<int>> ExecuteAsync(CloseClinicalEventCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var communication = new FamilyCommunicationChoice(
                command.Comunicacion, command.TipoComunicacion, command.TextoComunicacion);

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

            return await repository.CloseAsync(new CloseClinicalEventInput(
                scope.AccountId, command.CentroId, command.EventoId, command.Revision, command.OperacionId, communication), ct);
        });
}
