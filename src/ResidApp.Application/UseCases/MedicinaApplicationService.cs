using ResidApp.Application.Errors;
using ResidApp.Application.Ports;

namespace ResidApp.Application.UseCases;

/// <summary>
/// Fachada del vertical Medicina: bandeja y detalle de escalados (MED-01 a MED-03), valoración médica
/// (MED-04/MED-05) e indicaciones a Enfermería con su seguimiento (MED-06 a MED-09), más el basal vigente
/// del residente, el seguimiento médico con la continuidad entre turnos (MED-10 a MED-12) y el cierre médico
/// (MED-15 a MED-17) y el protocolo urgente (MED-13; la derivación llegará en su bloque), más la lista y la
/// ficha de residentes y el evento propio (MED-18 a MED-20). Igual que EnfermeriaApplicationService, es lo
/// único que MedicinaController inyecta.
/// </summary>
public sealed class MedicinaApplicationService(
    ListEscalations listEscalations, FindEscalationDetail findEscalationDetail, ReadCurrentBaseline readCurrentBaseline,
    StartMedicalAssessment startMedicalAssessment, SaveMedicalAssessment saveMedicalAssessment,
    RegisterMedicalIndication registerMedicalIndication, ListMedicalIndications listMedicalIndications,
    CloseMedicalEvent closeMedicalEvent, StartMedicalFollowUp startMedicalFollowUp,
    RecordMedicalFollowUpAction recordMedicalFollowUpAction, ListMedicalFollowUps listMedicalFollowUps,
    ActivateMedicalUrgentProtocol activateMedicalUrgentProtocol, RecordMedicalUrgentProtocolEntry recordMedicalUrgentProtocolEntry,
    ListMedicalUrgentProtocols listMedicalUrgentProtocols, SignMedicalReferralReport signMedicalReferralReport,
    RecordMedicalFamilyCallAttempt recordMedicalFamilyCallAttempt, FindResidentIdentification findResidentIdentification,
    DownloadReferralReport downloadReferralReport, ListScopeResidents listScopeResidents,
    FindScopeResident findScopeResident, RegisterClinicalEvent registerClinicalEvent,
    ListClosedEvents listClosedEvents, ReadBaselineHistory readBaselineHistory, ReadResidentTimeline readResidentTimeline,
    CorrectMedicalAssessment correctMedicalAssessment, RectifyAssessment rectifyAssessment,
    AssessmentCorrectionSettings correctionSettings)
{
    /// <summary>COR-01: ventana de corrección, para ofrecer corregir o rectificar en el detalle.</summary>
    public TimeSpan CorrectionWindow => correctionSettings.Window;

    public Task<ApplicationResult<bool>> CorrectMedicalAssessmentAsync(
        CorrectMedicalAssessmentCommand command, CancellationToken ct = default) =>
        correctMedicalAssessment.ExecuteAsync(command, ct);

    public Task<ApplicationResult<bool>> RectifyAssessmentAsync(RectifyAssessmentCommand command, CancellationToken ct = default) =>
        rectifyAssessment.ExecuteAsync(command with { Perfil = Shared.SystemProfile.Medicina }, ct);

    public Task<ApplicationResult<IReadOnlyList<ClosedEventSummary>>> ListClosedEventsAsync(
        ListClosedEventsCommand command, CancellationToken ct = default) =>
        listClosedEvents.ExecuteAsync(command with { Perfil = Shared.SystemProfile.Medicina }, ct);

    public Task<ApplicationResult<IReadOnlyList<BaselineHistoryEntry>>> ReadBaselineHistoryAsync(
        ReadBaselineHistoryCommand command, CancellationToken ct = default) =>
        readBaselineHistory.ExecuteAsync(command, ct);

    public Task<ApplicationResult<BaselineVersionDetail?>> ReadBaselineVersionAsync(
        ReadBaselineVersionCommand command, CancellationToken ct = default) =>
        readBaselineHistory.ExecuteVersionAsync(command, ct);

    public Task<ApplicationResult<IReadOnlyList<TimelineEntry>>> ReadResidentTimelineAsync(
        ReadResidentTimelineCommand command, CancellationToken ct = default) =>
        readResidentTimeline.ExecuteAsync(command with { Perfil = Shared.SystemProfile.Medicina }, ct);

    public Task<ApplicationResult<IReadOnlyList<ScopeResidentSummary>>> ListScopeResidentsAsync(
        ListScopeResidentsCommand command, CancellationToken ct = default) =>
        listScopeResidents.ExecuteAsync(command, ct);

    public Task<ApplicationResult<ScopeResidentSummary?>> FindScopeResidentAsync(
        FindScopeResidentCommand command, CancellationToken ct = default) =>
        findScopeResident.ExecuteAsync(command, ct);

    public Task<ApplicationResult<ClinicalEventResult>> RegisterClinicalEventAsync(
        RegisterClinicalEventCommand command, CancellationToken ct = default) =>
        registerClinicalEvent.ExecuteAsync(command, ct);

    public Task<ApplicationResult<int>> SignReferralReportAsync(SignReferralReportCommand command, CancellationToken ct = default) =>
        signMedicalReferralReport.ExecuteAsync(command, ct);

    public Task<ApplicationResult<int>> RecordFamilyCallAttemptAsync(RecordFamilyCallAttemptCommand command, CancellationToken ct = default) =>
        recordMedicalFamilyCallAttempt.ExecuteAsync(command, ct);

    public Task<ApplicationResult<ResidentIdentification>> FindResidentIdentificationAsync(
        FindResidentIdentificationCommand command, CancellationToken ct = default) =>
        findResidentIdentification.ExecuteAsync(command, ct);

    public Task<ApplicationResult<ReferralReportPdf>> DownloadReferralReportAsync(
        DownloadReferralReportCommand command, CancellationToken ct = default) =>
        downloadReferralReport.ExecuteAsync(command, ct);

    public Task<ApplicationResult<int>> ActivateUrgentProtocolAsync(ActivateUrgentProtocolCommand command, CancellationToken ct = default) =>
        activateMedicalUrgentProtocol.ExecuteAsync(command, ct);

    public Task<ApplicationResult<int>> RecordUrgentProtocolEntryAsync(RecordUrgentProtocolEntryCommand command, CancellationToken ct = default) =>
        recordMedicalUrgentProtocolEntry.ExecuteAsync(command, ct);

    public Task<ApplicationResult<IReadOnlyList<UrgentProtocolSummary>>> ListUrgentProtocolsAsync(
        ListUrgentProtocolsCommand command, CancellationToken ct = default) =>
        listMedicalUrgentProtocols.ExecuteAsync(command, ct);

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

    public Task<ApplicationResult<int>> CloseMedicalEventAsync(CloseMedicalEventCommand command, CancellationToken ct = default) =>
        closeMedicalEvent.ExecuteAsync(command, ct);

    public Task<ApplicationResult<int>> StartMedicalFollowUpAsync(StartMedicalFollowUpCommand command, CancellationToken ct = default) =>
        startMedicalFollowUp.ExecuteAsync(command, ct);

    public Task<ApplicationResult<int>> RecordMedicalFollowUpActionAsync(
        RecordMedicalFollowUpActionCommand command, CancellationToken ct = default) =>
        recordMedicalFollowUpAction.ExecuteAsync(command, ct);

    public Task<ApplicationResult<IReadOnlyList<MedicalFollowUpSummary>>> ListMedicalFollowUpsAsync(
        ListMedicalFollowUpsCommand command, CancellationToken ct = default) =>
        listMedicalFollowUps.ExecuteAsync(command, ct);
}
