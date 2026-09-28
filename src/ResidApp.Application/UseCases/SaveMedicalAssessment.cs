using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record SaveMedicalAssessmentCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, int Revision,
    string? HallazgosExploracion, string? Valoracion, string? Actuaciones,
    decimal? TemperaturaCelsius, int? TensionSistolica, int? TensionDiastolica, int? FrecuenciaCardiaca,
    int? FrecuenciaRespiratoria, int? SaturacionO2, RespiratorySupportCode? SoporteRespiratorio, decimal? FlujoO2,
    int? Glucemia, string? OtraConstanteNombre, string? OtraConstanteValor, string? OtraConstanteUnidad);

/// <summary>MED-05 "guardar borrador" de la valoración médica: exige haberla empezado y la revisión con la
/// que se abrió el evento. Nunca toca la observación original ni la valoración de Enfermería.</summary>
public sealed class SaveMedicalAssessment(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory,
    ISessionIdentityProvider session, IMedicalAssessmentRepository repository)
{
    public Task<ApplicationResult<int>> ExecuteAsync(SaveMedicalAssessmentCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var content = new MedicalAssessmentContent(
                command.HallazgosExploracion, command.Valoracion, command.Actuaciones,
                new VitalSigns(
                    command.TemperaturaCelsius, command.TensionSistolica, command.TensionDiastolica, command.FrecuenciaCardiaca,
                    command.FrecuenciaRespiratoria, command.SaturacionO2, command.SoporteRespiratorio, command.FlujoO2,
                    command.Glucemia, command.OtraConstanteNombre, command.OtraConstanteValor, command.OtraConstanteUnidad));

            var scope = await MedicinaScope.RequireAsync(scopes, session, directory, command.AmbitoPerfilId, command.CentroId, command.EventoId, ct);
            return await repository.SaveAsync(
                new SaveMedicalAssessmentInput(scope.AccountId, command.CentroId, command.EventoId, command.Revision, content), ct);
        });
}
