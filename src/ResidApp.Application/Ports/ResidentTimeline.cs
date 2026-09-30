using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>
/// HIS-02 (ENF-04, MED-03, MED-24): un hito de la línea temporal del residente, con su texto. Solo lectura. At es
/// cuándo se registró; EventId, el evento al que pertenece (null en los hitos del residente: basal y ubicación);
/// Profile, el perfil que lo hizo (las cuentas no tienen nombre). Cada tipo de hito lleva sus propios campos.
/// </summary>
public abstract record TimelineEntry(DateTimeOffset At, Guid? EventId, SystemProfile? Profile)
{
    public sealed record EventRegistered(
        DateTimeOffset At, Guid? EventId, SystemProfile? Profile, ClinicalEventOrigin Origin, DailyChangeClassification Classification,
        string? Observation, IReadOnlyList<DailyChangeAreaCode> Areas) : TimelineEntry(At, EventId, Profile);

    public sealed record NursingAssessmentSaved(DateTimeOffset At, Guid? EventId, NursingAssessmentContent Content)
        : TimelineEntry(At, EventId, SystemProfile.Enfermeria);

    public sealed record MedicalAssessmentSaved(DateTimeOffset At, Guid? EventId, MedicalAssessmentContent Content)
        : TimelineEntry(At, EventId, SystemProfile.Medicina);

    /// <summary>COR-01: versión corregida por su autor dentro de la ventana, con el motivo.</summary>
    public sealed record NursingAssessmentCorrected(DateTimeOffset At, Guid? EventId, NursingAssessmentContent Content, string Reason)
        : TimelineEntry(At, EventId, SystemProfile.Enfermeria);

    public sealed record MedicalAssessmentCorrected(DateTimeOffset At, Guid? EventId, MedicalAssessmentContent Content, string Reason)
        : TimelineEntry(At, EventId, SystemProfile.Medicina);

    /// <summary>COR-02: rectificación añadida a la valoración de Profile, fuera de la ventana.</summary>
    public sealed record AssessmentRectified(DateTimeOffset At, Guid? EventId, SystemProfile? Profile, string Text, string Reason)
        : TimelineEntry(At, EventId, Profile);

    public sealed record Escalated(DateTimeOffset At, Guid? EventId, string Reason) : TimelineEntry(At, EventId, SystemProfile.Enfermeria);

    public sealed record IndicationIssued(
        DateTimeOffset At, Guid? EventId, string Text, DateOnly? DueDate, string? Criterion, string? AdditionalInformation)
        : TimelineEntry(At, EventId, SystemProfile.Medicina);

    public sealed record IndicationRead(DateTimeOffset At, Guid? EventId, string Text) : TimelineEntry(At, EventId, SystemProfile.Enfermeria);

    public sealed record IndicationResolved(DateTimeOffset At, Guid? EventId, string Text, MedicalIndicationStatus Status, string? Incident)
        : TimelineEntry(At, EventId, SystemProfile.Enfermeria);

    /// <summary>Seguimiento de Enfermería (Notes = indicaciones de continuidad) o médico (Notes = objetivo).</summary>
    public sealed record FollowUpStarted(
        DateTimeOffset At, Guid? EventId, SystemProfile? Profile, DateOnly? DueDate, string? Criterion, string? Notes)
        : TimelineEntry(At, EventId, Profile);

    public sealed record FollowUpActionRecorded(
        DateTimeOffset At, Guid? EventId, SystemProfile? Profile, FollowUpActionType Type, string? Text, DateOnly? DueDate,
        string? Criterion, string? IncomingTeam) : TimelineEntry(At, EventId, Profile);

    public sealed record UrgentProtocolActivated(DateTimeOffset At, Guid? EventId, SystemProfile? Profile, string? Note)
        : TimelineEntry(At, EventId, Profile);

    public sealed record UrgentProtocolEntryRecorded(
        DateTimeOffset At, Guid? EventId, SystemProfile? Profile, UrgentProtocolEntryType Type, string? Text, string? Service,
        DateTimeOffset? ContactedAt) : TimelineEntry(At, EventId, Profile);

    public sealed record ReferralSigned(DateTimeOffset At, Guid? EventId, SystemProfile? Profile, string Reason)
        : TimelineEntry(At, EventId, Profile);

    public sealed record FamilyCallAttempted(
        DateTimeOffset At, Guid? EventId, SystemProfile? Profile, string Contact, DateTimeOffset CalledAt, FamilyCallResult Result,
        string? Note) : TimelineEntry(At, EventId, Profile);

    public sealed record FamilyCommunicationPrepared(
        DateTimeOffset At, Guid? EventId, SystemProfile? Profile, FamilyCommunicationType Type, string Text)
        : TimelineEntry(At, EventId, Profile);

    public sealed record EventClosed(DateTimeOffset At, Guid? EventId, SystemProfile? Profile, FamilyCommunicationDecision Decision)
        : TimelineEntry(At, EventId, Profile);

    public sealed record BaselineSigned(
        DateTimeOffset At, SystemProfile? Profile, int VersionNumber, BaselineReason Reason, int BarthelTotal)
        : TimelineEntry(At, null, Profile);

    public sealed record LocationStarted(DateTimeOffset At, string? UnitName) : TimelineEntry(At, null, null);
}
