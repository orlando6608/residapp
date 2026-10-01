using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record RecordFollowUpActionCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, int Revision, FollowUpActionType Tipo,
    string? Texto = null, DateOnly? FechaPrevista = null, string? Criterio = null, Guid? EquipoEntranteId = null,
    Guid? TransferenciaId = null);

/// <summary>
/// ENF-08/ENF-09: registrar una actuación, reprogramar con justificación, transferir al equipo o turno
/// entrante o confirmar la recepción de una transferencia, sobre un seguimiento abierto. Un solo caso de
/// uso parametrizado por el tipo de acción, mismo criterio que ListPendingChanges; cada acción conserva
/// su autoría y avanza la revisión del evento.
/// </summary>
public sealed class RecordFollowUpAction(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory,
    ISessionIdentityProvider session, INursingAssessmentRepository repository)
{
    public Task<ApplicationResult<int>> ExecuteAsync(RecordFollowUpActionCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var action = command.Tipo switch
            {
                FollowUpActionType.Actuacion => FollowUpAction.Note(command.Texto),
                FollowUpActionType.Reprogramacion => FollowUpAction.Reschedule(
                    new FollowUpPlan(command.FechaPrevista, command.Criterio), command.Texto),
                FollowUpActionType.Transferencia => FollowUpAction.Transfer(command.EquipoEntranteId, command.Texto),
                FollowUpActionType.Recepcion => FollowUpAction.Receive(command.TransferenciaId ?? Guid.Empty),
                _ => throw new DomainValidationException("FOLLOW_UP_ACTION_INVALID"),
            };

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

            return await repository.RecordFollowUpActionAsync(new RecordFollowUpActionInput(
                scope.AccountId, command.CentroId, command.EventoId, command.Revision, action), ct);
        });
}
