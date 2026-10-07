using ResidApp.Application.Authorization;
using ResidApp.Shared;

namespace ResidApp.Web.Models;

/// <summary>DIR-14: textos en español de la trazabilidad clínica. Cada acción de ClinicalTraceability.ActionCodes tiene su etiqueta
/// (lo comprueba un test); un código desconocido se ve genérico, nunca en inglés.</summary>
public static class ClinicalTraceabilityDisplay
{
    private static readonly Dictionary<string, string> Actions = new()
    {
        ["CLINICAL_EVENT_REGISTER"] = "Evento registrado",
        ["CLINICAL_EVENT_ESCALATE"] = "Evento escalado a Medicina",
        ["CLINICAL_EVENT_CLOSE"] = "Evento cerrado",
        ["DAILY_CLOSURE_CHANGE_REPORTED"] = "Cambio registrado por Auxiliar",
        ["DAILY_CLOSURE_NO_CHANGE"] = "Cierre del día sin cambios (Auxiliar)",
        ["DAILY_CLOSURE_NOT_ASSESSABLE"] = "Cierre del día: no se pudo valorar (Auxiliar)",
        ["NURSING_ASSESSMENT_START"] = "Valoración de Enfermería iniciada",
        ["NURSING_ASSESSMENT_SAVE"] = "Valoración de Enfermería guardada",
        ["NURSING_ASSESSMENT_CORRECT"] = "Valoración de Enfermería corregida",
        ["MEDICAL_ASSESSMENT_START"] = "Valoración médica iniciada",
        ["MEDICAL_ASSESSMENT_SAVE"] = "Valoración médica guardada",
        ["MEDICAL_ASSESSMENT_CORRECT"] = "Valoración médica corregida",
        ["ASSESSMENT_RECTIFY"] = "Valoración rectificada",
        ["MEDICAL_INDICATION_ISSUE"] = "Indicación médica emitida",
        ["MEDICAL_INDICATION_READ"] = "Indicación médica leída",
        ["MEDICAL_INDICATION_DONE"] = "Indicación médica realizada",
        ["MEDICAL_INDICATION_NOT_DONE"] = "Indicación médica no realizada",
        ["FOLLOW_UP_START"] = "Seguimiento de Enfermería iniciado",
        ["FOLLOW_UP_NOTE"] = "Nota en el seguimiento de Enfermería",
        ["FOLLOW_UP_RESCHEDULE"] = "Seguimiento de Enfermería reprogramado",
        ["FOLLOW_UP_TRANSFER"] = "Seguimiento de Enfermería transferido",
        ["FOLLOW_UP_RECEIVE"] = "Recepción de la transferencia del seguimiento de Enfermería",
        ["MEDICAL_FOLLOW_UP_START"] = "Seguimiento médico iniciado",
        ["MEDICAL_FOLLOW_UP_NOTE"] = "Nota en el seguimiento médico",
        ["MEDICAL_FOLLOW_UP_KEEP"] = "Seguimiento médico mantenido",
        ["MEDICAL_FOLLOW_UP_RESCHEDULE"] = "Seguimiento médico reprogramado",
        ["MEDICAL_FOLLOW_UP_TRANSFER"] = "Seguimiento médico transferido",
        ["MEDICAL_FOLLOW_UP_RECEIVE"] = "Recepción de la transferencia del seguimiento médico",
        ["URGENT_PROTOCOL_ACTIVATE"] = "Protocolo urgente activado",
        ["URGENT_PROTOCOL_ACTION"] = "Actuación en el protocolo urgente",
        ["URGENT_PROTOCOL_CONTACT"] = "Contacto en el protocolo urgente",
        ["URGENT_PROTOCOL_EVOLUTION"] = "Evolución en el protocolo urgente",
        ["REFERRAL_REPORT_SIGN"] = "Informe de derivación firmado",
        ["REFERRAL_REPORT_DOWNLOAD"] = "Informe de derivación descargado",
        ["FAMILY_CALL_ATTEMPT"] = "Llamada a la familia registrada",
        ["FAMILY_COMMUNICATION_PREPARE"] = "Comunicación a la familia preparada",
        ["BASELINE_DRAFT_CREATE"] = "Borrador de basal creado",
        ["BASELINE_SIGN"] = "Basal firmado",
        ["CLINICAL_DETAIL_READ"] = "Lectura clínica de Dirección Clínica",
    };

    private static readonly Dictionary<string, string> Resources = new()
    {
        ["CLINICAL_EVENT"] = "Evento asistencial",
        ["DAILY_CLOSURE"] = "Cierre del día",
        ["MEDICAL_INDICATION"] = "Indicación médica",
        ["REFERRAL_REPORT"] = "Informe de derivación",
        ["FAMILY_CALL_ATTEMPT"] = "Llamada a la familia",
        ["FAMILY_COMMUNICATION"] = "Comunicación a la familia",
        ["BASELINE_DRAFT"] = "Borrador de basal",
        ["BASELINE_VERSION"] = "Versión del basal",
        ["BASELINE"] = "Basal",
    };

    public static string Action(string code) => Actions.TryGetValue(code, out var label) ? label : "Acción clínica";

    public static bool HasLabel(string code) => Actions.ContainsKey(code);

    /// <summary>El recurso: el propio de la acción o, en las lecturas de Dirección, el tipo de recurso leído (basal vigente, línea temporal…).</summary>
    public static string Resource(string code) =>
        Resources.TryGetValue(code, out var label) ? label
        : EnumCode.TryParseCode<ClinicalResourceType>(code, out var resource) ? EnumDisplay.Label(resource)
        : "Registro clínico";

    /// <summary>La finalidad declarada de una lectura de Dirección; las anteriores a las tres finalidades de CJ (SUPERVISION_CLINICA) se avisan como tales.</summary>
    public static string? Purpose(string? code) => code is null ? null
        : EnumCode.TryParseCode<ClinicalDetailAccessPurpose>(code, out var purpose) ? EnumDisplay.Label(purpose)
        : "Finalidad anterior (supervisión clínica)";
}
