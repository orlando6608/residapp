using ResidApp.Application.Errors;
using ResidApp.Application.Ports;

namespace ResidApp.Application.UseCases;

/// <summary>
/// Fachada del vertical Medicina: bandeja y detalle de escalados (MED-01 a MED-03), valoración médica
/// (MED-04/MED-05) e indicaciones a Enfermería con su seguimiento (MED-06 a MED-09), más el basal vigente
/// del residente. El resto de la conducta médica llegará con sus historias. Igual que
/// EnfermeriaApplicationService, es lo único que MedicinaController inyecta.
/// </summary>
public sealed class MedicinaApplicationService(
    ListEscalations listEscalations, FindEscalationDetail findEscalationDetail, ReadCurrentBaseline readCurrentBaseline,
    StartMedicalAssessment startMedicalAssessment, SaveMedicalAssessment saveMedicalAssessment,
    RegisterMedicalIndication registerMedicalIndication, ListMedicalIndications listMedicalIndications)
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

    public Task<ApplicationResult<int>> StartMedicalAssessmentAsync(StartMedicalAssessmentCommand command, CancellationToken ct = default) =>
        startMedicalAssessment.ExecuteAsync(command, ct);

    public Task<ApplicationResult<int>> SaveMedicalAssessmentAsync(SaveMedicalAssessmentCommand command, CancellationToken ct = default) =>
        saveMedicalAssessment.ExecuteAsync(command, ct);

    public Task<ApplicationResult<int>> RegisterMedicalIndicationAsync(
        RegisterMedicalIndicationCommand command, CancellationToken ct = default) =>
        registerMedicalIndication.ExecuteAsync(command, ct);

    public Task<ApplicationResult<IReadOnlyList<MedicalIndicationListItem>>> ListMedicalIndicationsAsync(
        ListMedicalIndicationsCommand command, CancellationToken ct = default) =>
        listMedicalIndications.ExecuteAsync(command, ct);
}
