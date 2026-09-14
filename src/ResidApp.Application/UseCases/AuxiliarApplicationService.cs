using ResidApp.Application.Errors;
using ResidApp.Application.Ports;

namespace ResidApp.Application.UseCases;

/// <summary>
/// Fachada del vertical Auxiliar (grupo A1: AUX-01/AUX-02/AUX-03). Se registra en el contenedor de DI de
/// ResidApp.Web y es lo único que AuxiliarController debería inyectar de este vertical, igual que
/// ResidentBaselineApplicationService para Residente/Basal.
/// </summary>
public sealed class AuxiliarApplicationService(
    ListAssignedResidents listAssignedResidents, FindAssignedResident findAssignedResident,
    ReadCurrentBaseline readCurrentBaseline, RegisterDailyClosure registerDailyClosure, RegisterDailyChange registerDailyChange)
{
    public Task<ApplicationResult<IReadOnlyList<AssignedResidentSummary>>> ListAssignedResidentsAsync(
        ListAssignedResidentsCommand command, CancellationToken ct = default) =>
        listAssignedResidents.ExecuteAsync(command, ct);

    public Task<ApplicationResult<AssignedResidentSummary?>> FindAssignedResidentAsync(
        FindAssignedResidentCommand command, CancellationToken ct = default) =>
        findAssignedResident.ExecuteAsync(command, ct);

    public Task<ApplicationResult<CurrentBaselineSummary?>> ReadCurrentBaselineAsync(
        ReadCurrentBaselineCommand command, CancellationToken ct = default) =>
        readCurrentBaseline.ExecuteAsync(command, ct);

    public Task<ApplicationResult<DailyClosureResult>> RegisterDailyClosureAsync(
        RegisterDailyClosureCommand command, CancellationToken ct = default) =>
        registerDailyClosure.ExecuteAsync(command, ct);

    public Task<ApplicationResult<DailyClosureResult>> RegisterDailyChangeAsync(
        RegisterDailyChangeCommand command, CancellationToken ct = default) =>
        registerDailyChange.ExecuteAsync(command, ct);
}
