using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record SaveFamilyCommunicationScheduleCommand(
    Guid AmbitoPerfilId, CenterId CentroId, int Version, TimeOnly? HoraPublicacion, int? MargenMinutos,
    FamilyCommunicationSummaryFrequency ResumenFrecuencia = FamilyCommunicationSummaryFrequency.Semanal);

/// <summary>
/// Pantalla del horario de los comunicados a la familia del centro (CJ, 2026-10-10; confirmaciones 5.1): la hora de publicación y el margen, y
/// la frecuencia del resumen periódico. La edita Administración del centro. Cambiar la hora o el margen solo afecta a los comunicados que se
/// guarden a partir de entonces: la hora de los ya guardados queda fijada. La frecuencia rige desde que se guarda (el resumen se calcula al leer).
/// </summary>
public sealed class FamilyCommunicationScheduleApplicationService(
    AdministrationAccessResolver administrationAccess, IFamilyCommunicationScheduleRepository repository)
{
    public Task<ApplicationResult<FamilyCommunicationScheduleView>> ReadAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await administrationAccess.ResolveAsync(profileScopeId, centerId, ct);
            return await repository.ReadAsync(access, ct);
        });

    /// <summary>Devuelve la versión nueva (la misma si no cambia nada).</summary>
    public Task<ApplicationResult<int>> SaveAsync(SaveFamilyCommunicationScheduleCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var desired = FamilyCommunicationScheduleSettings.Validate(command.HoraPublicacion, command.MargenMinutos, command.ResumenFrecuencia);
            var (access, _) = await administrationAccess.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            return await repository.SaveAsync(access, command.Version, desired, ct);
        });
}
