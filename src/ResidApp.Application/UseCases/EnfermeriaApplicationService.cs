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
    RegisterClinicalEvent registerClinicalEvent, ListPendingChanges listPendingChanges, FindPendingChangeDetail findPendingChangeDetail,
    StartNursingAssessment startNursingAssessment, SaveNursingAssessment saveNursingAssessment,
    CloseClinicalEvent closeClinicalEvent, ListPendingFamilyCommunications listPendingFamilyCommunications,
    StartFollowUp startFollowUp, RecordFollowUpAction recordFollowUpAction, ListFollowUps listFollowUps,
    EscalateClinicalEvent escalateClinicalEvent, ListPendingIndications listPendingIndications,
    RecordIndicationProgress recordIndicationProgress, ActivateUrgentProtocol activateUrgentProtocol,
    RecordUrgentProtocolEntry recordUrgentProtocolEntry, ListUrgentProtocols listUrgentProtocols,
    SignReferralReport signReferralReport, RecordFamilyCallAttempt recordFamilyCallAttempt,
    FindResidentIdentification findResidentIdentification, DownloadReferralReport downloadReferralReport)
{
    public Task<ApplicationResult<int>> SignReferralReportAsync(SignReferralReportCommand command, CancellationToken ct = default) =>
        signReferralReport.ExecuteAsync(command, ct);

    public Task<ApplicationResult<int>> RecordFamilyCallAttemptAsync(RecordFamilyCallAttemptCommand command, CancellationToken ct = default) =>
        recordFamilyCallAttempt.ExecuteAsync(command, ct);

    public Task<ApplicationResult<ResidentIdentification>> FindResidentIdentificationAsync(
        FindResidentIdentificationCommand command, CancellationToken ct = default) =>
        findResidentIdentification.ExecuteAsync(command, ct);

    public Task<ApplicationResult<ReferralReportPdf>> DownloadReferralReportAsync(
        DownloadReferralReportCommand command, CancellationToken ct = default) =>
        downloadReferralReport.ExecuteAsync(command, ct);

    public Task<ApplicationResult<int>> ActivateUrgentProtocolAsync(ActivateUrgentProtocolCommand command, CancellationToken ct = default) =>
        activateUrgentProtocol.ExecuteAsync(command, ct);

    public Task<ApplicationResult<int>> RecordUrgentProtocolEntryAsync(RecordUrgentProtocolEntryCommand command, CancellationToken ct = default) =>
        recordUrgentProtocolEntry.ExecuteAsync(command, ct);

    public Task<ApplicationResult<IReadOnlyList<UrgentProtocolSummary>>> ListUrgentProtocolsAsync(
        ListUrgentProtocolsCommand command, CancellationToken ct = default) =>
        listUrgentProtocols.ExecuteAsync(command, ct);

    public Task<ApplicationResult<IReadOnlyList<MedicalIndicationListItem>>> ListPendingIndicationsAsync(
        ListPendingIndicationsCommand command, CancellationToken ct = default) =>
        listPendingIndications.ExecuteAsync(command, ct);

    public Task<ApplicationResult<int>> RecordIndicationProgressAsync(RecordIndicationProgressCommand command, CancellationToken ct = default) =>
        recordIndicationProgress.ExecuteAsync(command, ct);

    public Task<ApplicationResult<int>> EscalateClinicalEventAsync(EscalateClinicalEventCommand command, CancellationToken ct = default) =>
        escalateClinicalEvent.ExecuteAsync(command, ct);

    public Task<ApplicationResult<int>> StartFollowUpAsync(StartFollowUpCommand command, CancellationToken ct = default) =>
        startFollowUp.ExecuteAsync(command, ct);

    public Task<ApplicationResult<int>> RecordFollowUpActionAsync(RecordFollowUpActionCommand command, CancellationToken ct = default) =>
        recordFollowUpAction.ExecuteAsync(command, ct);

    public Task<ApplicationResult<IReadOnlyList<FollowUpSummary>>> ListFollowUpsAsync(
        ListFollowUpsCommand command, CancellationToken ct = default) =>
        listFollowUps.ExecuteAsync(command, ct);

    public Task<ApplicationResult<int>> CloseClinicalEventAsync(
        CloseClinicalEventCommand command, CancellationToken ct = default) =>
        closeClinicalEvent.ExecuteAsync(command, ct);

    public Task<ApplicationResult<IReadOnlyList<PendingFamilyCommunicationSummary>>> ListPendingFamilyCommunicationsAsync(
        ListPendingFamilyCommunicationsCommand command, CancellationToken ct = default) =>
        listPendingFamilyCommunications.ExecuteAsync(command, ct);

    public Task<ApplicationResult<int>> StartNursingAssessmentAsync(
        StartNursingAssessmentCommand command, CancellationToken ct = default) =>
        startNursingAssessment.ExecuteAsync(command, ct);

    public Task<ApplicationResult<int>> SaveNursingAssessmentAsync(
        SaveNursingAssessmentCommand command, CancellationToken ct = default) =>
        saveNursingAssessment.ExecuteAsync(command, ct);

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

    public Task<ApplicationResult<IReadOnlyList<PendingChangeSummary>>> ListPendingChangesAsync(
        ListPendingChangesCommand command, CancellationToken ct = default) =>
        listPendingChanges.ExecuteAsync(command, ct);

    public Task<ApplicationResult<PendingChangeDetail?>> FindPendingChangeDetailAsync(
        FindPendingChangeDetailCommand command, CancellationToken ct = default) =>
        findPendingChangeDetail.ExecuteAsync(command, ct);
}
