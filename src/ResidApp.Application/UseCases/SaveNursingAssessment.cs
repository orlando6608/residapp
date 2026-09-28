using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record SaveNursingAssessmentCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, int Revision,
    string? Hallazgos, string? Valoracion, string? Actuaciones, string? Comunicaciones, string? Resultado,
    decimal? TemperaturaCelsius, int? TensionSistolica, int? TensionDiastolica, int? FrecuenciaCardiaca,
    int? FrecuenciaRespiratoria, int? SaturacionO2, RespiratorySupportCode? SoporteRespiratorio, decimal? FlujoO2,
    int? Glucemia, string? OtraConstanteNombre, string? OtraConstanteValor, string? OtraConstanteUnidad);

/// <summary>
/// ENF-05 "guardar borrador" de la valoración de Enfermería: exige que la valoración se haya empezado
/// (ENF-03) y la revisión con la que se abrió el evento. Nunca toca la observación original.
/// </summary>
public sealed class SaveNursingAssessment(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory,
    ISessionIdentityProvider session, INursingAssessmentRepository repository)
{
    public Task<ApplicationResult<int>> ExecuteAsync(SaveNursingAssessmentCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var content = new NursingAssessmentContent(
                command.Hallazgos, command.Valoracion, command.Actuaciones, command.Comunicaciones, command.Resultado,
                new VitalSigns(
                    command.TemperaturaCelsius, command.TensionSistolica, command.TensionDiastolica, command.FrecuenciaCardiaca,
                    command.FrecuenciaRespiratoria, command.SaturacionO2, command.SoporteRespiratorio, command.FlujoO2,
                    command.Glucemia, command.OtraConstanteNombre, command.OtraConstanteValor, command.OtraConstanteUnidad));

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

            return await repository.SaveAsync(
                new SaveNursingAssessmentInput(scope.AccountId, command.CentroId, command.EventoId, command.Revision, content), ct);
        });
}
