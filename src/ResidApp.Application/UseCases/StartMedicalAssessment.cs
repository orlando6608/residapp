using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record StartMedicalAssessmentCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, int Revision);

/// <summary>MED-04 "iniciar valoración médica" sobre un escalado: registra quién y cuándo, sin propiedad
/// permanente sobre el evento; si cambió desde que se abrió, conflicto y recarga.</summary>
public sealed class StartMedicalAssessment(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory,
    ISessionIdentityProvider session, IMedicalAssessmentRepository repository)
{
    public Task<ApplicationResult<int>> ExecuteAsync(StartMedicalAssessmentCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var scope = await MedicinaScope.RequireAsync(scopes, session, directory, command.AmbitoPerfilId, command.CentroId, command.EventoId, ct);
            return await repository.StartAsync(
                new StartMedicalAssessmentInput(scope.AccountId, command.CentroId, command.EventoId, command.Revision), ct);
        });
}
