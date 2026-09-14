using ResidApp.Application.Errors;
using ResidApp.Application.Ports;

namespace ResidApp.Application.UseCases;

/// <summary>
/// Fachada del vertical Enfermería (grupo E1: ENF-01/ENF-17/ENF-18). Se registra en el contenedor de DI
/// de ResidApp.Web y es lo único que EnfermeriaController debería inyectar de este vertical, igual que
/// AuxiliarApplicationService para Auxiliar.
/// </summary>
public sealed class EnfermeriaApplicationService(
    ListScopeResidents listScopeResidents, FindScopeResident findScopeResident, ReadCurrentBaseline readCurrentBaseline,
    RegisterClinicalEvent registerClinicalEvent)
{
    public Task<ApplicationResult<IReadOnlyList<ScopeResidentSummary>>> ListScopeResidentsAsync(
        ListScopeResidentsCommand command, CancellationToken ct = default) =>
        listScopeResidents.ExecuteAsync(command, ct);

    public Task<ApplicationResult<ScopeResidentSummary?>> FindScopeResidentAsync(
        FindScopeResidentCommand command, CancellationToken ct = default) =>
        findScopeResident.ExecuteAsync(command, ct);

    public Task<ApplicationResult<CurrentBaselineSummary?>> ReadCurrentBaselineAsync(
        ReadCurrentBaselineCommand command, CancellationToken ct = default) =>
        readCurrentBaseline.ExecuteAsync(command, ct);

    public Task<ApplicationResult<ClinicalEventResult>> RegisterClinicalEventAsync(
        RegisterClinicalEventCommand command, CancellationToken ct = default) =>
        registerClinicalEvent.ExecuteAsync(command, ct);
}
