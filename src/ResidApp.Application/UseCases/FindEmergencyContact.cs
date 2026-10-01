using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>
/// ADM-08 (0022): el contacto urgente del residente en la ficha de Enfermería y de Medicina, en solo lectura (decisión del
/// usuario, 2026-10-01; la matriz da «LECT» a esos dos perfiles y «NO» a Auxiliar y Dirección). Deny-by-default: el
/// residente tiene que estar en el ámbito, con el mismo criterio que la ficha (FindScopeResident, que solo acepta
/// Enfermería y Medicina). Devuelve null si no hay contacto designado.
/// </summary>
public sealed class FindEmergencyContact(FindScopeResident findScopeResident, IEnfermeriaResidentDirectory directory)
{
    public async Task<ApplicationResult<EmergencyContactSummary?>> ExecuteAsync(
        FindScopeResidentCommand command, CancellationToken ct = default)
    {
        var found = await findScopeResident.ExecuteAsync(command, ct);
        if (!found.Ok)
        {
            return ApplicationResult<EmergencyContactSummary?>.Failed(found.Error!);
        }

        return await ApplicationResultRunner.RunAsync(async () =>
        {
            if (found.Value is null)
            {
                throw new AccessDeniedException();
            }
            return await directory.FindEmergencyContactAsync(command.CentroId, command.ResidenteId, ct);
        });
    }
}
