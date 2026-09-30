using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ReadResidentTimelineCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, SystemProfile Perfil);

/// <summary>
/// HIS-02 (ENF-04, MED-03, MED-24): línea temporal completa del residente, en solo lectura. Decisión del usuario
/// (2026-09-30), como la matriz de permisos del prototipo: Enfermería y Medicina la leen dentro de su ámbito, sin
/// permiso aparte ni auditoría (la lectura auditada de Dirección Clínica queda para su vertical). Deny-by-default:
/// el residente tiene que estar en el ámbito, con el mismo criterio que la ficha (FindScopeResident); de los
/// eventos solo entran los visibles para el ámbito, con la regla de las bandejas.
/// </summary>
public sealed class ReadResidentTimeline(FindScopeResident findScopeResident, IChangeInboxDirectory directory)
{
    public async Task<ApplicationResult<IReadOnlyList<TimelineEntry>>> ExecuteAsync(
        ReadResidentTimelineCommand command, CancellationToken ct = default)
    {
        var found = await findScopeResident.ExecuteAsync(
            new FindScopeResidentCommand(command.AmbitoPerfilId, command.CentroId, command.ResidenteId, command.Perfil), ct);
        if (!found.Ok)
        {
            return ApplicationResult<IReadOnlyList<TimelineEntry>>.Failed(found.Error!);
        }

        return await ApplicationResultRunner.RunAsync(async () =>
        {
            if (found.Value is null)
            {
                throw new AccessDeniedException();
            }
            return await directory.ListTimelineAsync(command.AmbitoPerfilId, command.CentroId, command.ResidenteId, ct);
        });
    }
}
