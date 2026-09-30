using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>COR-01: ventana de corrección, global (Correccion:VentanaHoras en appsettings, 6 h). La
/// configuración por centro queda para el vertical Administración.</summary>
public sealed record AssessmentCorrectionSettings(TimeSpan Window);

public sealed record CorrectNursingAssessmentCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, int Correcciones, string? Motivo,
    string? Hallazgos, string? Valoracion, string? Actuaciones, string? Comunicaciones, string? Resultado,
    decimal? TemperaturaCelsius, int? TensionSistolica, int? TensionDiastolica, int? FrecuenciaCardiaca,
    int? FrecuenciaRespiratoria, int? SaturacionO2, RespiratorySupportCode? SoporteRespiratorio, decimal? FlujoO2,
    int? Glucemia, string? OtraConstanteNombre, string? OtraConstanteValor, string? OtraConstanteUnidad);

public sealed record CorrectMedicalAssessmentCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, int Correcciones, string? Motivo,
    string? HallazgosExploracion, string? Valoracion, string? Actuaciones,
    decimal? TemperaturaCelsius, int? TensionSistolica, int? TensionDiastolica, int? FrecuenciaCardiaca,
    int? FrecuenciaRespiratoria, int? SaturacionO2, RespiratorySupportCode? SoporteRespiratorio, decimal? FlujoO2,
    int? Glucemia, string? OtraConstanteNombre, string? OtraConstanteValor, string? OtraConstanteUnidad);

/// <summary>Perfil dice qué valoración se rectifica; lo fija la fachada de cada vertical.</summary>
public sealed record RectifyAssessmentCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, int Rectificaciones, string? Texto, string? Motivo, SystemProfile Perfil);

/// <summary>COR-01: el autor corrige su valoración de Enfermería dentro de la ventana, con motivo. Las reglas
/// de disponibilidad, autoría y ventana están en IAssessmentCorrectionRepository.</summary>
public sealed class CorrectNursingAssessment(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session,
    IAssessmentCorrectionRepository repository, AssessmentCorrectionSettings settings)
{
    public Task<ApplicationResult<bool>> ExecuteAsync(CorrectNursingAssessmentCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var content = new NursingAssessmentContent(
                command.Hallazgos, command.Valoracion, command.Actuaciones, command.Comunicaciones, command.Resultado,
                new VitalSigns(
                    command.TemperaturaCelsius, command.TensionSistolica, command.TensionDiastolica, command.FrecuenciaCardiaca,
                    command.FrecuenciaRespiratoria, command.SaturacionO2, command.SoporteRespiratorio, command.FlujoO2,
                    command.Glucemia, command.OtraConstanteNombre, command.OtraConstanteValor, command.OtraConstanteUnidad));
            var reason = new AssessmentCorrectionReason(command.Motivo);

            var scope = await AssessmentCorrectionScope.RequireAsync(
                scopes, session, directory, command.AmbitoPerfilId, command.CentroId, command.EventoId, SystemProfile.Enfermeria, ct);
            await repository.CorrectNursingAsync(new CorrectNursingAssessmentInput(
                scope.AccountId, command.CentroId, command.EventoId, command.Correcciones, content, reason, settings.Window), ct);
            return true;
        });
}

/// <summary>COR-01: el autor corrige su valoración médica dentro de la ventana, con motivo.</summary>
public sealed class CorrectMedicalAssessment(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session,
    IAssessmentCorrectionRepository repository, AssessmentCorrectionSettings settings)
{
    public Task<ApplicationResult<bool>> ExecuteAsync(CorrectMedicalAssessmentCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var content = new MedicalAssessmentContent(
                command.HallazgosExploracion, command.Valoracion, command.Actuaciones,
                new VitalSigns(
                    command.TemperaturaCelsius, command.TensionSistolica, command.TensionDiastolica, command.FrecuenciaCardiaca,
                    command.FrecuenciaRespiratoria, command.SaturacionO2, command.SoporteRespiratorio, command.FlujoO2,
                    command.Glucemia, command.OtraConstanteNombre, command.OtraConstanteValor, command.OtraConstanteUnidad));
            var reason = new AssessmentCorrectionReason(command.Motivo);

            var scope = await AssessmentCorrectionScope.RequireAsync(
                scopes, session, directory, command.AmbitoPerfilId, command.CentroId, command.EventoId, SystemProfile.Medicina, ct);
            await repository.CorrectMedicalAsync(new CorrectMedicalAssessmentInput(
                scope.AccountId, command.CentroId, command.EventoId, command.Correcciones, content, reason, settings.Window), ct);
            return true;
        });
}

/// <summary>COR-02: el autor añade una rectificación a su valoración, fuera de la ventana.</summary>
public sealed class RectifyAssessment(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session,
    IAssessmentCorrectionRepository repository, AssessmentCorrectionSettings settings)
{
    public Task<ApplicationResult<bool>> ExecuteAsync(RectifyAssessmentCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var rectification = new AssessmentRectification(command.Texto, command.Motivo);
            var scope = await AssessmentCorrectionScope.RequireAsync(
                scopes, session, directory, command.AmbitoPerfilId, command.CentroId, command.EventoId, command.Perfil, ct);
            await repository.RectifyAsync(new RectifyAssessmentInput(
                scope.AccountId, command.CentroId, command.EventoId, command.Perfil, command.Rectificaciones, rectification,
                settings.Window), ct);
            return true;
        });
}

/// <summary>Deny-by-default, como MedicinaScope: ámbito activo del perfil de la valoración y evento visible
/// para ese ámbito.</summary>
internal static class AssessmentCorrectionScope
{
    public static async Task<ActiveProfileScope> RequireAsync(
        IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, IChangeInboxDirectory directory,
        Guid ambitoPerfilId, CenterId centroId, Guid eventoId, SystemProfile profile, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
        var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
        var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == ambitoPerfilId && s.CenterId == centroId);
        if (scope is null || scope.Profile != profile || profile is not (SystemProfile.Enfermeria or SystemProfile.Medicina))
        {
            throw new AccessDeniedException();
        }
        if (await directory.FindAsync(ambitoPerfilId, centroId, eventoId, ct) is null)
        {
            throw new AccessDeniedException();
        }
        return scope;
    }
}
