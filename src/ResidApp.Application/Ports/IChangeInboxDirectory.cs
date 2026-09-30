using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Una de las áreas observadas de un cambio, ya para lectura (ENF-04): mismo contenido que
/// DailyChangeAreaInput (escritura), pero como su propio record de puerto en vez de reutilizar el de
/// escritura, igual que el resto del esquema separa entrada y lectura.</summary>
public sealed record PendingChangeAreaSummary(
    DailyChangeAreaCode AreaCode, IReadOnlyList<DailyChangeAreaOptionCode> Options, string? FreeText);

/// <summary>ENF-02/ENF-03: una fila de bandeja, sea un cambio de Auxiliar o un evento propio de Enfermería
/// (dbo.eventos_asistenciales). Areas solo lleva los códigos y solo existe para los cambios de Auxiliar;
/// Observation solo para los eventos propios. La propia bandeja no necesita el detalle completo (ENF-04 sí).</summary>
public sealed record PendingChangeSummary(
    Guid EventId, ClinicalEventOrigin Origin, ResidentId ResidentId, string ResidentDisplayName, UnitId UnitId, string? UnitName,
    IReadOnlyList<DailyChangeAreaCode> Areas, string? Observation, SystemProfile AuthorProfile, DateTimeOffset OccurredAt,
    DailyChangePriorityReason? PriorityReason, string? DirectNoticeNotes, ClinicalEventStatus Status);

/// <summary>ENF-05: borrador de la valoración de Enfermería, con quién lo tocó por última vez (solo si fue la
/// cuenta del ámbito que consulta; el nombre de otros profesionales no se expone).</summary>
public sealed record NursingAssessmentDraft(
    NursingAssessmentContent Content, bool LastUpdatedByCurrentAccount, DateTimeOffset LastUpdatedAt,
    AssessmentAmendments? Amendments = null);

/// <summary>COR-01/COR-02: autoría de la última versión ordinaria de una valoración (solo su autor la corrige o
/// la rectifica, y la ventana empieza en ese guardado) y sus correcciones y rectificaciones, de la más antigua
/// a la más reciente.</summary>
public sealed record AssessmentAmendments(
    bool AuthoredByCurrentAccount, DateTimeOffset LastSavedAt,
    IReadOnlyList<AssessmentCorrectionSummary> Corrections, IReadOnlyList<AssessmentRectificationSummary> Rectifications);

public sealed record AssessmentCorrectionSummary(string Reason, DateTimeOffset CorrectedAt);

public sealed record AssessmentRectificationSummary(string Text, string Reason, DateTimeOffset RecordedAt);

/// <summary>ENF-15: comunicación familiar preparada al cerrar, pendiente de aprobación humana. Ante la
/// familia se firma siempre como FamilyCommunicationChoice.VisibleAuthor, nunca con el profesional.</summary>
public sealed record PreparedFamilyCommunication(FamilyCommunicationType Type, string Text, DateTimeOffset PreparedAt);

/// <summary>ENF-07A: cierre de un evento, con quién lo cerró (solo si fue la cuenta del ámbito que consulta;
/// el nombre de otros profesionales no se expone) y la decisión de comunicación familiar.</summary>
public sealed record ClinicalEventClosure(
    bool ClosedByCurrentAccount, DateTimeOffset ClosedAt, FamilyCommunicationDecision Decision,
    PreparedFamilyCommunication? Communication);

/// <summary>Una comunicación familiar pendiente de aprobación en el ámbito (tarjeta "Comunicaciones" de
/// ENF-01), con el evento del que procede.</summary>
public sealed record PendingFamilyCommunicationSummary(
    Guid EventId, ResidentId ResidentId, string ResidentDisplayName, string? UnitName, PreparedFamilyCommunication Communication);

/// <summary>ENF-08/ENF-09: una acción sobre un seguimiento, con su autoría (solo si fue la cuenta del ámbito
/// que consulta). Según el tipo trae el texto, el plan reprogramado, el equipo entrante o la transferencia
/// que confirma.</summary>
public sealed record FollowUpActionSummary(
    Guid Id, FollowUpActionType Type, string? Text, DateOnly? DueDate, string? Criterion, string? IncomingTeam,
    Guid? TransferId, bool ByCurrentAccount, DateTimeOffset RecordedAt);

/// <summary>ENF-07B a ENF-09: el seguimiento de un evento con su plan inicial y todas sus acciones, de la
/// más antigua a la más reciente. El plan vigente es el de la última reprogramación, si la hay; la
/// transferencia pendiente es la última que nadie ha confirmado.</summary>
public sealed record FollowUpDetail(
    DateOnly? InitialDueDate, string? InitialCriterion, string? ContinuityNotes, bool StartedByCurrentAccount,
    DateTimeOffset StartedAt, IReadOnlyList<FollowUpActionSummary> Actions)
{
    private FollowUpActionSummary? LastReschedule =>
        Actions.LastOrDefault(a => a.Type == FollowUpActionType.Reprogramacion);

    public DateOnly? DueDate => LastReschedule is { } r ? r.DueDate : InitialDueDate;

    public string? Criterion => LastReschedule is { } r ? r.Criterion : InitialCriterion;

    public FollowUpActionSummary? PendingTransfer =>
        Actions.LastOrDefault(a => a.Type == FollowUpActionType.Transferencia) is { } transfer
        && !Actions.Any(a => a.TransferId == transfer.Id)
            ? transfer
            : null;
}

/// <summary>Una fila de los escalados abiertos de Enfermería: el evento sigue en Medicina (Status es su estado actual).
/// EscalatedByCurrentAccount marca los que escaló la cuenta del ámbito que consulta.</summary>
public sealed record OpenEscalationSummary(
    Guid EventId, ResidentId ResidentId, string ResidentDisplayName, string? UnitName, string Reason, DateTimeOffset EscalatedAt,
    bool EscalatedByCurrentAccount, ClinicalEventStatus Status);

/// <summary>ENF-08: una fila de la bandeja compartida de seguimientos. El equipo responsable es la
/// Enfermería de la unidad; DueDate/Criterion son el plan vigente.</summary>
public sealed record FollowUpSummary(
    Guid EventId, ResidentId ResidentId, string ResidentDisplayName, string? UnitName, DateOnly? DueDate, string? Criterion,
    DateTimeOffset StartedAt, FollowUpActionType? LastActionType, DateTimeOffset? LastActionAt, bool TransferPending);

/// <summary>ENF-09/MED-03: escalado a Medicina, con el motivo que escribió Enfermería y si lo escaló la
/// cuenta del ámbito que consulta.</summary>
public sealed record ClinicalEventEscalation(string Reason, bool EscalatedByCurrentAccount, DateTimeOffset EscalatedAt);

/// <summary>MED-02: una fila de la bandeja de escalados de Medicina. Constantes y actuaciones proceden de
/// la valoración de Enfermería, ya cerrada; no hay resumen diagnóstico automático. Un evento propio de
/// Medicina (MED-18) no tiene motivo de escalado (Reason es null) y muestra su observación; ReceivedAt es la
/// hora del escalado o, en un evento propio, la de su registro.</summary>
public sealed record EscalationSummary(
    Guid EventId, ResidentId ResidentId, string ResidentDisplayName, string? UnitName, string? Reason, string? Observation,
    DateTimeOffset ReceivedAt, VitalSigns? Vitals, string? Actions, ClinicalEventStatus Status);

/// <summary>MED-05: borrador de la valoración médica (o ya cerrada), con quién la tocó por última vez (solo
/// si fue la cuenta del ámbito que consulta).</summary>
public sealed record MedicalAssessmentDraft(
    MedicalAssessmentContent Content, bool LastUpdatedByCurrentAccount, DateTimeOffset LastUpdatedAt,
    AssessmentAmendments? Amendments = null);

/// <summary>MED-07/MED-08/ENF-10: una indicación médica con su estado de lectura y realización. Revision es
/// la de la indicación, que hay que devolver al confirmar la lectura o registrar el resultado.</summary>
public sealed record MedicalIndicationSummary(
    Guid Id, string Text, DateOnly? DueDate, string? Criterion, string? AdditionalInformation, bool IssuedByCurrentAccount,
    DateTimeOffset IssuedAt, MedicalIndicationStatus Status, int Revision, DateTimeOffset? ReadAt, DateTimeOffset? ResolvedAt,
    string? Incident);

/// <summary>MED-10 a MED-12: el seguimiento médico de un evento, con su objetivo. Tracking reutiliza el
/// seguimiento de Enfermería (plan vigente, acciones y transferencia pendiente); no tiene indicaciones de
/// continuidad (ContinuityNotes siempre null).</summary>
public sealed record MedicalFollowUpDetail(string Objective, FollowUpDetail Tracking);

/// <summary>MED-11: una fila de la bandeja compartida de seguimientos médicos. El equipo responsable es
/// Medicina de la unidad; DueDate/Criterion son el plan vigente. LastContinuity es la última decisión de
/// continuidad al terminar un turno (transferir o conservar), con el equipo entrante si se transfirió.</summary>
public sealed record MedicalFollowUpSummary(
    Guid EventId, ResidentId ResidentId, string ResidentDisplayName, string? UnitName, string Objective, DateOnly? DueDate,
    string? Criterion, DateTimeOffset StartedAt, FollowUpActionType? LastActionType, DateTimeOffset? LastActionAt,
    FollowUpActionType? LastContinuity, string? LastContinuityTeam, bool LastContinuityByCurrentAccount, bool TransferPending);

/// <summary>ENF-11/MED-13: un registro del protocolo urgente, con su autoría (solo si fue la cuenta del
/// ámbito que consulta). Según el tipo trae el texto o el servicio y la hora del contacto (con nota opcional).</summary>
public sealed record UrgentProtocolEntrySummary(
    UrgentProtocolEntryType Type, string? Text, string? Service, DateTimeOffset? ContactedAt, bool ByCurrentAccount,
    DateTimeOffset RecordedAt);

/// <summary>ENF-11/MED-13: el protocolo urgente de un evento: qué perfil lo activó, con qué nota, quién y
/// cuándo, y sus registros del más antiguo al más reciente.</summary>
public sealed record UrgentProtocolDetail(
    SystemProfile Profile, string? ActivationNote, bool ActivatedByCurrentAccount, DateTimeOffset ActivatedAt,
    IReadOnlyList<UrgentProtocolEntrySummary> Entries);

/// <summary>ENF-11/MED-13: una fila de la bandeja de protocolos urgentes activos. Status dice de qué perfil
/// es el protocolo; cada caso de uso filtra el suyo.</summary>
public sealed record UrgentProtocolSummary(
    Guid EventId, ResidentId ResidentId, string ResidentDisplayName, string? UnitName, ClinicalEventStatus Status,
    DateTimeOffset ActivatedAt, UrgentProtocolEntryType? LastEntryType, DateTimeOffset? LastEntryAt);

/// <summary>DER-06: un intento de llamada al contacto familiar, con su autoría (solo si fue la cuenta del
/// ámbito que consulta).</summary>
public sealed record FamilyCallAttemptSummary(
    string Contact, DateTimeOffset CalledAt, FamilyCallResult Result, string? Note, bool ByCurrentAccount, DateTimeOffset RecordedAt);

/// <summary>ENF-12/MED-14: el informe de derivación firmado de un evento: qué perfil derivó, con qué motivo,
/// quién y cuándo firmó, la huella del contenido firmado y los intentos de llamada a la familia, del más
/// antiguo al más reciente. El PDF se descarga aparte (IReferralReportRepository).</summary>
public sealed record ReferralDetail(
    SystemProfile Profile, string Reason, bool SignedByCurrentAccount, DateTimeOffset SignedAt, string ContentHash,
    IReadOnlyList<FamilyCallAttemptSummary> CallAttempts);

/// <summary>DER-03: identificación del residente y del centro para el informe de derivación.</summary>
public sealed record ResidentIdentification(
    string DisplayName, DateOnly BirthDate, DocumentedSexCode DocumentedSex, string CenterName, string? UnitName);

/// <summary>MED-04 a MED-12: la parte médica de un evento: quién y cuándo empezó la valoración médica (null
/// si no se ha empezado), el borrador, las indicaciones emitidas, de la más antigua a la más reciente, y el
/// seguimiento médico si lo hubo.</summary>
public sealed record MedicalDetail(
    bool? StartedByCurrentAccount, DateTimeOffset? StartedAt, MedicalAssessmentDraft? Assessment,
    IReadOnlyList<MedicalIndicationSummary> Indications, MedicalFollowUpDetail? FollowUp);

/// <summary>ENF-10/MED-08: una indicación en una lista, con el evento y el residente del que procede, y cuándo
/// se cerró el evento (null si sigue abierto).</summary>
public sealed record MedicalIndicationListItem(
    Guid EventId, ResidentId ResidentId, string ResidentDisplayName, string? UnitName, ClinicalEventStatus EventStatus,
    DateTimeOffset? EventClosedAt, MedicalIndicationSummary Indication);

/// <summary>ENF-04: detalle completo de un evento recibido. Según el origen trae las áreas y la temperatura
/// del cambio de Auxiliar, o la observación y los datos clínicos del evento propio; en ambos casos la
/// observación original es inmutable. Revision es la que hay que devolver al empezar o guardar la
/// valoración (concurrencia optimista). ReferenceRanges son los rangos de referencia de constantes del
/// centro (vacío si no hay ninguno configurado), para el aviso visual de ENF-05. Assessment es el borrador
/// o, si el evento está cerrado, la valoración ya cerrada; Closure solo existe si el evento está cerrado.
/// Context es la instantánea de HIS-03 (null si el evento no la tiene).</summary>
public sealed record PendingChangeDetail(
    Guid EventId, ClinicalEventOrigin Origin, ResidentId ResidentId, string ResidentDisplayName, UnitId UnitId, string? UnitName,
    DailyChangeClassification Classification, IReadOnlyList<PendingChangeAreaSummary> Areas, decimal? TemperatureCelsius,
    string? Observation, string? ClinicalData,
    SystemProfile AuthorProfile, DailyChangePriorityReason? PriorityReason, string? DirectNoticeNotes, DateTimeOffset OccurredAt,
    ClinicalEventStatus Status, int Revision, bool? AssessmentStartedByCurrentAccount, DateTimeOffset? AssessmentStartedAt,
    NursingAssessmentDraft? Assessment, IReadOnlyList<VitalSignRange> ReferenceRanges, ClinicalEventClosure? Closure,
    FollowUpDetail? FollowUp, ClinicalEventEscalation? Escalation, MedicalDetail Medical, UrgentProtocolDetail? UrgentProtocol,
    ReferralDetail? Referral, ClinicalEventContext? Context = null);

/// <summary>HIS-03: basal y ubicación vigentes cuando se creó el evento (dbo.eventos_contexto). La versión del
/// basal es null si el residente no tenía basal firmado; la unidad, si no tenía ubicación registrada.</summary>
public sealed record ClinicalEventContext(int? BaselineVersionNumber, DateTimeOffset? BaselineSignedAt, string? UnitName);

/// <summary>ENF-18/MED-20: un evento todavía abierto en la ficha del residente, con su estado actual.</summary>
public sealed record OpenEventSummary(
    Guid EventId, ClinicalEventOrigin Origin, DailyChangeClassification Classification, IReadOnlyList<DailyChangeAreaCode> Areas,
    string? Observation, SystemProfile AuthorProfile, DateTimeOffset OccurredAt, ClinicalEventStatus Status, bool Escalated);

/// <summary>HIS-01/ENF-23/MED-22: un evento cerrado en el Historial del residente, con su contexto (HIS-03).</summary>
public sealed record ClosedEventSummary(
    Guid EventId, ClinicalEventOrigin Origin, DailyChangeClassification Classification, IReadOnlyList<DailyChangeAreaCode> Areas,
    string? Observation, SystemProfile AuthorProfile, DateTimeOffset OccurredAt, DateTimeOffset ClosedAt, bool Escalated,
    ClinicalEventContext? Context);

/// <summary>
/// Traduce las bandejas ENF-02 (cambios ordinarios) y ENF-03 (prioritaria), más el detalle ENF-04, sobre
/// dbo.eventos_asistenciales: los cambios que registra Auxiliar y los eventos que observa Enfermería
/// (ENF-16) siguen el mismo ciclo. Mismo criterio de ámbito "por defecto o restringido" que
/// IEnfermeriaResidentDirectory, porque una bandeja "compartida por unidad"
/// (docs/flujos-clinicos/valoracion-escalado-enfermeria.md) no debe mostrar más residentes de los que el
/// ámbito ya autoriza a nivel individual.
/// </summary>
public interface IChangeInboxDirectory
{
    Task<IReadOnlyList<PendingChangeSummary>> ListAsync(
        Guid profileScopeId, CenterId centerId, DailyChangeClassification classification, CancellationToken ct = default);

    Task<PendingChangeDetail?> FindAsync(Guid profileScopeId, CenterId centerId, Guid eventId, CancellationToken ct = default);

    Task<IReadOnlyList<PendingFamilyCommunicationSummary>> ListPendingFamilyCommunicationsAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default);

    Task<IReadOnlyList<FollowUpSummary>> ListFollowUpsAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default);

    /// <summary>Escalados abiertos de Enfermería: eventos del ámbito escalados a Medicina que aún no se han cerrado, del
    /// escalado más antiguo al más reciente.</summary>
    Task<IReadOnlyList<OpenEscalationSummary>> ListOpenEscalationsAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default);

    /// <summary>HIS-01: eventos cerrados de un residente visibles para el ámbito (la misma regla que las
    /// bandejas), del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<ClosedEventSummary>> ListClosedEventsAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default);

    /// <summary>ENF-18/MED-20: eventos abiertos de un residente visibles para el ámbito (la misma regla que las
    /// bandejas), del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<OpenEventSummary>> ListOpenEventsAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default);

    /// <summary>HIS-02: línea temporal del residente, del hito más reciente al más antiguo. Solo incluye los
    /// eventos visibles para el ámbito (la misma regla que las bandejas), abiertos y cerrados, más las versiones
    /// del basal y los cambios de ubicación del residente. Quien llama comprueba antes que el residente está en el
    /// ámbito.</summary>
    Task<IReadOnlyList<TimelineEntry>> ListTimelineAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default);

    /// <summary>MED-02: escalados pendientes para un ámbito de Medicina. Con un ámbito de Medicina, FindAsync
    /// solo devuelve eventos escalados a Medicina.</summary>
    Task<IReadOnlyList<EscalationSummary>> ListEscalationsAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default);

    /// <summary>ENF-10/MED-08: todas las indicaciones médicas de los eventos visibles para el ámbito, de la más
    /// antigua a la más reciente; cada caso de uso filtra las que le interesan.</summary>
    Task<IReadOnlyList<MedicalIndicationListItem>> ListIndicationsAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default);

    /// <summary>MED-11: seguimientos médicos abiertos para un ámbito de Medicina, vencidos incluidos.</summary>
    Task<IReadOnlyList<MedicalFollowUpSummary>> ListMedicalFollowUpsAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default);

    /// <summary>ENF-11/MED-13: protocolos urgentes activos de los eventos visibles para el ámbito, de los dos
    /// perfiles, del más antiguo al más reciente.</summary>
    Task<IReadOnlyList<UrgentProtocolSummary>> ListUrgentProtocolsAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default);

    /// <summary>DER-03: identificación del residente y del centro de un evento visible para el ámbito (null si
    /// no lo es).</summary>
    Task<ResidentIdentification?> FindResidentIdentificationAsync(
        Guid profileScopeId, CenterId centerId, Guid eventId, CancellationToken ct = default);
}
