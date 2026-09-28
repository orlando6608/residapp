using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record CloseMedicalEventCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, int Revision, Guid OperacionId,
    FamilyCommunicationDecision? Comunicacion, FamilyCommunicationType? TipoComunicacion, string? TextoComunicacion);

/// <summary>
/// MED-15 a MED-17 "cerrar el evento", salida de la conducta médica: cierre idempotente sin segundo cierre de
/// Enfermería, con la decisión explícita de comunicación familiar (mismo mecanismo que CloseClinicalEvent).
/// Se puede cerrar con indicaciones todavía pendientes: siguen visibles para Enfermería hasta resolverse.
/// </summary>
public sealed class CloseMedicalEvent(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory,
    ISessionIdentityProvider session, IMedicalAssessmentRepository repository)
{
    public Task<ApplicationResult<int>> ExecuteAsync(CloseMedicalEventCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var communication = new FamilyCommunicationChoice(
                command.Comunicacion, command.TipoComunicacion, command.TextoComunicacion);

            var scope = await MedicinaScope.RequireAsync(scopes, session, directory, command.AmbitoPerfilId, command.CentroId, command.EventoId, ct);
            return await repository.CloseAsync(new CloseClinicalEventInput(
                scope.AccountId, command.CentroId, command.EventoId, command.Revision, command.OperacionId, communication), ct);
        });
}
