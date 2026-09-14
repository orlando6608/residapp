using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record FindAssignedResidentCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId);

/// <summary>
/// Traduce la entrada a AUX-02/AUX-03 desde AUX-01: reutiliza ListAssignedResidents y filtra por
/// ResidentId, en vez de un método de repositorio propio, para garantizar que "puedo abrir este residente"
/// es exactamente el mismo criterio que "aparece en mi lista" (AUX-01, criterio de aceptación: "intentar
/// acceder a un residente no asignado se rechaza, sin revelar si el residente existe").
/// </summary>
public sealed class FindAssignedResident(ListAssignedResidents listAssignedResidents)
{
    public async Task<ApplicationResult<AssignedResidentSummary?>> ExecuteAsync(
        FindAssignedResidentCommand command, CancellationToken ct = default)
    {
        var result = await listAssignedResidents.ExecuteAsync(
            new ListAssignedResidentsCommand(command.AmbitoPerfilId, command.CentroId), ct);
        if (!result.Ok)
        {
            return ApplicationResult<AssignedResidentSummary?>.Failed(result.Error!);
        }

        var match = result.Value!.FirstOrDefault(r => r.ResidentId == command.ResidenteId);
        return ApplicationResult<AssignedResidentSummary?>.Success(match);
    }
}
