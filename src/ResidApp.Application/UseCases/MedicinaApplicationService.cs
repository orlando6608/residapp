using ResidApp.Application.Errors;
using ResidApp.Application.Ports;

namespace ResidApp.Application.UseCases;

/// <summary>
/// Fachada del vertical Medicina, por ahora solo su historia 1 en lectura (MED-01 a MED-03): bandeja de
/// escalados, su detalle y el basal vigente del residente. La valoración médica y la conducta llegarán con
/// el resto del vertical. Igual que EnfermeriaApplicationService, es lo único que MedicinaController inyecta.
/// </summary>
public sealed class MedicinaApplicationService(
    ListEscalations listEscalations, FindEscalationDetail findEscalationDetail, ReadCurrentBaseline readCurrentBaseline)
{
    public Task<ApplicationResult<IReadOnlyList<EscalationSummary>>> ListEscalationsAsync(
        ListEscalationsCommand command, CancellationToken ct = default) =>
        listEscalations.ExecuteAsync(command, ct);

    public Task<ApplicationResult<PendingChangeDetail?>> FindEscalationDetailAsync(
        FindEscalationDetailCommand command, CancellationToken ct = default) =>
        findEscalationDetail.ExecuteAsync(command, ct);

    public Task<ApplicationResult<CurrentBaselineSummary?>> ReadCurrentBaselineAsync(
        ReadCurrentBaselineCommand command, CancellationToken ct = default) =>
        readCurrentBaseline.ExecuteAsync(command, ct);
}
