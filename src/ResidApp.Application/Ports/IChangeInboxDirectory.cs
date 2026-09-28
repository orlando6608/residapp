using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
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
    NursingAssessmentContent Content, bool LastUpdatedByCurrentAccount, DateTimeOffset LastUpdatedAt);

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

/// <summary>ENF-08: una fila de la bandeja compartida de seguimientos. El equipo responsable es la
/// Enfermería de la unidad; DueDate/Criterion son el plan vigente.</summary>
public sealed record FollowUpSummary(
    Guid EventId, ResidentId ResidentId, string ResidentDisplayName, string? UnitName, DateOnly? DueDate, string? Criterion,
    DateTimeOffset StartedAt, FollowUpActionType? LastActionType, DateTimeOffset? LastActionAt, bool TransferPending);

/// <summary>ENF-09/MED-03: escalado a Medicina, con el motivo que escribió Enfermería y si lo escaló la
/// cuenta del ámbito que consulta.</summary>
public sealed record ClinicalEventEscalation(string Reason, bool EscalatedByCurrentAccount, DateTimeOffset EscalatedAt);

/// <summary>MED-02: una fila de la bandeja de escalados de Medicina. Constantes y actuaciones proceden de
/// la valoración de Enfermería, ya cerrada; no hay resumen diagnóstico automático.</summary>
public sealed record EscalationSummary(
    Guid EventId, ResidentId ResidentId, string ResidentDisplayName, string? UnitName, string Reason, DateTimeOffset EscalatedAt,
    VitalSigns? Vitals, string? Actions, ClinicalEventStatus Status);

/// <summary>MED-05: borrador de la valoración médica (o ya cerrada), con quién la tocó por última vez (solo
/// si fue la cuenta del ámbito que consulta).</summary>
public sealed record MedicalAssessmentDraft(
    MedicalAssessmentContent Content, bool LastUpdatedByCurrentAccount, DateTimeOffset LastUpdatedAt);

/// <summary>MED-07/MED-08/ENF-10: una indicación médica con su estado de lectura y realización. Revision es
/// la de la indicación, que hay que devolver al confirmar la lectura o registrar el resultado.</summary>
public sealed record MedicalIndicationSummary(
    Guid Id, string Text, DateOnly? DueDate, string? Criterion, string? AdditionalInformation, bool IssuedByCurrentAccount,
    DateTimeOffset IssuedAt, MedicalIndicationStatus Status, int Revision, DateTimeOffset? ReadAt, DateTimeOffset? ResolvedAt,
    string? Incident);

/// <summary>MED-04 a MED-08: la parte médica de un evento: quién y cuándo empezó la valoración médica (null
/// si no se ha empezado), el borrador y las indicaciones emitidas, de la más antigua a la más reciente.</summary>
public sealed record MedicalDetail(
    bool? StartedByCurrentAccount, DateTimeOffset? StartedAt, MedicalAssessmentDraft? Assessment,
    IReadOnlyList<MedicalIndicationSummary> Indications);

/// <summary>ENF-10/MED-08: una indicación en una lista, con el evento y el residente del que procede.</summary>
public sealed record MedicalIndicationListItem(
    Guid EventId, ResidentId ResidentId, string ResidentDisplayName, string? UnitName, ClinicalEventStatus EventStatus,
    MedicalIndicationSummary Indication);

/// <summary>ENF-04: detalle completo de un evento recibido. Según el origen trae las áreas y la temperatura
/// del cambio de Auxiliar, o la observación y los datos clínicos del evento propio; en ambos casos la
/// observación original es inmutable. Revision es la que hay que devolver al empezar o guardar la
/// valoración (concurrencia optimista). ReferenceRanges son los rangos de referencia de constantes del
/// centro (vacío si no hay ninguno configurado), para el aviso visual de ENF-05. Assessment es el borrador
/// o, si el evento está cerrado, la valoración ya cerrada; Closure solo existe si el evento está cerrado.</summary>
public sealed record PendingChangeDetail(
    Guid EventId, ClinicalEventOrigin Origin, ResidentId ResidentId, string ResidentDisplayName, UnitId UnitId, string? UnitName,
    DailyChangeClassification Classification, IReadOnlyList<PendingChangeAreaSummary> Areas, decimal? TemperatureCelsius,
    string? Observation, string? ClinicalData,
    SystemProfile AuthorProfile, DailyChangePriorityReason? PriorityReason, string? DirectNoticeNotes, DateTimeOffset OccurredAt,
    ClinicalEventStatus Status, int Revision, bool? AssessmentStartedByCurrentAccount, DateTimeOffset? AssessmentStartedAt,
    NursingAssessmentDraft? Assessment, IReadOnlyList<VitalSignRange> ReferenceRanges, ClinicalEventClosure? Closure,
    FollowUpDetail? FollowUp, ClinicalEventEscalation? Escalation, MedicalDetail Medical);

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

    /// <summary>MED-02: escalados pendientes para un ámbito de Medicina. Con un ámbito de Medicina, FindAsync
    /// solo devuelve eventos escalados a Medicina.</summary>
    Task<IReadOnlyList<EscalationSummary>> ListEscalationsAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default);

    /// <summary>ENF-10/MED-08: todas las indicaciones médicas de los eventos visibles para el ámbito, de la más
    /// antigua a la más reciente; cada caso de uso filtra las que le interesan.</summary>
    Task<IReadOnlyList<MedicalIndicationListItem>> ListIndicationsAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default);
}
