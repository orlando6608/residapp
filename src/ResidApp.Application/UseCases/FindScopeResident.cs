using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record FindScopeResidentCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId);

/// <summary>
/// Traduce la entrada a ENF-18 desde ENF-17: reutiliza ListScopeResidents y filtra por ResidentId, igual
/// que FindAssignedResident hace para Auxiliar, para garantizar que "puedo abrir este residente" es
/// exactamente el mismo criterio que "aparece en mi lista".
/// </summary>
public sealed class FindScopeResident(ListScopeResidents listScopeResidents)
{
    public async Task<ApplicationResult<ScopeResidentSummary?>> ExecuteAsync(
        FindScopeResidentCommand command, CancellationToken ct = default)
    {
        var result = await listScopeResidents.ExecuteAsync(
            new ListScopeResidentsCommand(command.AmbitoPerfilId, command.CentroId), ct);
        if (!result.Ok)
        {
            return ApplicationResult<ScopeResidentSummary?>.Failed(result.Error!);
        }

        var match = result.Value!.FirstOrDefault(r => r.ResidentId == command.ResidenteId);
        return ApplicationResult<ScopeResidentSummary?>.Success(match);
    }
}
