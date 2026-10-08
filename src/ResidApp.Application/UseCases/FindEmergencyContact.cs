using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>
/// ADM-08 (0022): los contactos urgentes del residente en la ficha de Enfermería y de Medicina, en solo lectura (decisión del
/// usuario, 2026-10-01; la matriz da «LECT» a esos dos perfiles y «NO» a Auxiliar y Dirección). Deny-by-default: el
/// residente tiene que estar en el ámbito, con el mismo criterio que la ficha (FindScopeResident, que solo acepta
/// Enfermería y Medicina). Devuelve los contactos vigentes (0044), vacío si no hay ninguno.
/// </summary>
public sealed class FindEmergencyContact(FindScopeResident findScopeResident, IEnfermeriaResidentDirectory directory)
{
    public async Task<ApplicationResult<IReadOnlyList<EmergencyContactSummary>>> ExecuteAsync(
        FindScopeResidentCommand command, CancellationToken ct = default)
    {
        var found = await findScopeResident.ExecuteAsync(command, ct);
        if (!found.Ok)
        {
            return ApplicationResult<IReadOnlyList<EmergencyContactSummary>>.Failed(found.Error!);
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
