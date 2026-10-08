using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>DIR-14: un hito de la trazabilidad clínica de un residente, leído de la auditoría (dbo.eventos_auditoria, de solo inserción):
/// quién (nombre visible de la cuenta, null si no tiene, y perfil), en qué unidad, qué acción, sobre qué recurso y cuándo. No lleva
/// texto clínico ni valores, ni la justificación de las lecturas de Dirección; Purpose es la finalidad declarada en esas lecturas.</summary>
public sealed record ClinicalTraceabilityEntry(
    DateTimeOffset OccurredAt, string? ActorName, SystemProfile ActorProfile, string? UnitName, string Action, string ResourceType,
    string? Purpose);

/// <summary>Los hitos más recientes primero. Truncated dice que había más de MaxEntries y se han quitado los más antiguos.</summary>
public sealed record ClinicalTraceabilityPage(IReadOnlyList<ClinicalTraceabilityEntry> Entries, bool Truncated)
{
    public const int MaxEntries = 500;
}

/// <summary>Las acciones de auditoría que son hitos clínicos de un residente: solo estas se leen en DIR-14. Las administrativas (altas,
/// familiares, permisos) tienen su propia auditoría (ADM-28) y no entran. Toda acción nueva de aquí necesita su etiqueta en español
/// (ClinicalTraceabilityDisplay; lo comprueba un test).</summary>
public static class ClinicalTraceability
{
    public static readonly IReadOnlyList<string> ActionCodes =
    [
        "CLINICAL_EVENT_REGISTER", "CLINICAL_EVENT_ESCALATE", "CLINICAL_EVENT_CLOSE",
        "DAILY_CLOSURE_CHANGE_REPORTED", "DAILY_CLOSURE_NO_CHANGE", "DAILY_CLOSURE_NOT_ASSESSABLE",
        "NURSING_ASSESSMENT_START", "NURSING_ASSESSMENT_SAVE", "NURSING_ASSESSMENT_CORRECT",
        "MEDICAL_ASSESSMENT_START", "MEDICAL_ASSESSMENT_SAVE", "MEDICAL_ASSESSMENT_CORRECT", "ASSESSMENT_RECTIFY",
        "MEDICAL_INDICATION_ISSUE", "MEDICAL_INDICATION_READ", "MEDICAL_INDICATION_DONE", "MEDICAL_INDICATION_NOT_DONE",
        "FOLLOW_UP_START", "FOLLOW_UP_NOTE", "FOLLOW_UP_RESCHEDULE", "FOLLOW_UP_TRANSFER", "FOLLOW_UP_RECEIVE",
        "MEDICAL_FOLLOW_UP_START", "MEDICAL_FOLLOW_UP_NOTE", "MEDICAL_FOLLOW_UP_KEEP", "MEDICAL_FOLLOW_UP_RESCHEDULE",
        "MEDICAL_FOLLOW_UP_TRANSFER", "MEDICAL_FOLLOW_UP_RECEIVE",
        "URGENT_PROTOCOL_ACTIVATE", "URGENT_PROTOCOL_ACTION", "URGENT_PROTOCOL_CONTACT", "URGENT_PROTOCOL_EVOLUTION",
        "REFERRAL_REPORT_SIGN", "REFERRAL_REPORT_DOWNLOAD", "FAMILY_CALL_ATTEMPT", "FAMILY_COMMUNICATION_PREPARE", "FAMILY_COMMUNICATION_CORRECT",
        "BASELINE_DRAFT_CREATE", "BASELINE_SIGN", "CLINICAL_DETAIL_READ",
    ];
}
