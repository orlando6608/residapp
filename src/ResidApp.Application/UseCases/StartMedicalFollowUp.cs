using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record StartMedicalFollowUpCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, int Revision,
    DateOnly? FechaPrevista, string? Criterio, string? Objetivo);

/// <summary>MED-10 "iniciar seguimiento médico", una de las salidas de la conducta médica: fecha prevista o
/// criterio y objetivo. Exige la valoración médica guardada; un solo seguimiento médico por evento. El equipo
/// responsable es Medicina de la unidad del evento.</summary>
public sealed class StartMedicalFollowUp(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory,
    ISessionIdentityProvider session, IMedicalAssessmentRepository repository)
{
    public Task<ApplicationResult<int>> ExecuteAsync(StartMedicalFollowUpCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var followUp = new MedicalFollowUp(new FollowUpPlan(command.FechaPrevista, command.Criterio), command.Objetivo);

            var scope = await MedicinaScope.RequireAsync(scopes, session, directory, command.AmbitoPerfilId, command.CentroId, command.EventoId, ct);
            return await repository.StartFollowUpAsync(
                new StartMedicalFollowUpInput(scope.AccountId, command.CentroId, command.EventoId, command.Revision, followUp), ct);
        });
}
