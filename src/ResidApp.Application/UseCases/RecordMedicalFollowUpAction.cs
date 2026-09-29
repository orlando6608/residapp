using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record RecordMedicalFollowUpActionCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, int Revision, FollowUpActionType Tipo,
    string? Texto = null, DateOnly? FechaPrevista = null, string? Criterio = null, string? EquipoEntrante = null,
    Guid? TransferenciaId = null);

/// <summary>
/// MED-11/MED-12: registrar una revisión, reprogramar con justificación y, al terminar el turno, transferir al
/// equipo entrante o conservar para la propia próxima revisión, sobre un seguimiento médico abierto. La
/// recepción de una transferencia se puede confirmar, pero no hace falta. Mismo criterio que
/// RecordFollowUpAction de Enfermería; cada acción conserva su autoría y avanza la revisión del evento.
/// </summary>
public sealed class RecordMedicalFollowUpAction(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory,
    ISessionIdentityProvider session, IMedicalAssessmentRepository repository)
{
    public Task<ApplicationResult<int>> ExecuteAsync(RecordMedicalFollowUpActionCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var action = command.Tipo switch
            {
                FollowUpActionType.Actuacion => FollowUpAction.Note(command.Texto),
                FollowUpActionType.Reprogramacion => FollowUpAction.Reschedule(
                    new FollowUpPlan(command.FechaPrevista, command.Criterio), command.Texto),
                FollowUpActionType.Transferencia => FollowUpAction.Transfer(command.EquipoEntrante, command.Texto),
                FollowUpActionType.Conservacion => FollowUpAction.Keep(command.Texto),
                FollowUpActionType.Recepcion => FollowUpAction.Receive(command.TransferenciaId ?? Guid.Empty),
                _ => throw new DomainValidationException("FOLLOW_UP_ACTION_INVALID"),
            };

            var scope = await MedicinaScope.RequireAsync(scopes, session, directory, command.AmbitoPerfilId, command.CentroId, command.EventoId, ct);
            return await repository.RecordFollowUpActionAsync(
                new RecordMedicalFollowUpActionInput(scope.AccountId, command.CentroId, command.EventoId, command.Revision, action), ct);
        });
}
