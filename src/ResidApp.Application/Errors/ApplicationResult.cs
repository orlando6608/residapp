using System.Text.RegularExpressions;

namespace ResidApp.Application.Errors;

/// <summary>Traduce AccessDeniedError de lib/application/errors.ts.</summary>
public sealed class AccessDeniedException() : Exception("ACCESS_DENIED");

/// <summary>Traduce el literal de código de ApplicationFailure en errors.ts.</summary>
public enum ApplicationFailureCode
{
    AccessDenied,
    InvalidInput,
    Conflict,
    Unavailable,
}

public sealed record ApplicationFailure(ApplicationFailureCode Code, string Message);

/// <summary>Traduce ApplicationResult&lt;T&gt; de errors.ts (unión ok:true/ok:false).</summary>
public sealed class ApplicationResult<T>
{
    public bool Ok { get; }
    public T? Value { get; }
    public ApplicationFailure? Error { get; }

    private ApplicationResult(bool ok, T? value, ApplicationFailure? error)
    {
        Ok = ok;
        Value = value;
        Error = error;
    }

    public static ApplicationResult<T> Success(T value) => new(true, value, null);
    public static ApplicationResult<T> Failed(ApplicationFailure error) => new(false, default, error);
}

/// <summary>
/// Traduce normalizeApplicationError y applicationResult de errors.ts. Sin causas, SQL, identificadores ni
/// distinción entre recurso ausente y ajeno en el mensaje expuesto. Añade BASELINE_DRAFT_REVISION_CONFLICT
/// al patrón de Conflict: código nuevo introducido por la corrección de concurrencia del Paso 3 (ver
/// SqlBaselineRepository), que no existía en el TS original.
/// </summary>
public static class ApplicationResultRunner
{
    private static readonly Regex AccessDeniedPattern =
        new("(?:RESIDENT_CREATE|BASELINE_SIGN|CLINICAL_DETAIL_READ|BASELINE_DRAFT|REFERENCE_RANGES|PROCESS_DEADLINES|ADMIN_PRINCIPAL)_NOT_AUTHORIZED", RegexOptions.Compiled);

    private static readonly Regex InvalidInputPattern = new(
        "^(?:RESIDENT_CREATE_INPUT_INVALID|BASELINE_SIGN_INPUT_INVALID|CLINICAL_DETAIL_READ_INPUT_INVALID|" +
        "APPLICATION_INPUT_INVALID|BASELINE_AREAS_INCOMPLETE|BASELINE_BARTHEL_INCOMPLETE|" +
        "DAILY_CLOSURE_REASON_REQUIRED|VOICE_DICTATION_INPUT_INVALID|DAILY_CHANGE_AREAS_REQUIRED|DAILY_CHANGE_TEMPERATURE_OUT_OF_RANGE|DAILY_CHANGE_AREA_CONTENT_REQUIRED|" +
        "DAILY_CHANGE_AREA_DUPLICATED|DAILY_CHANGE_AREA_OPTION_INVALID|DAILY_CHANGE_PRIORITY_DOCUMENTATION_REQUIRED|" +
        "BASELINE_DRAFT_ALREADY_ACTIVE|BASELINE_MULTI_VALUE_INVALID|BASELINE_MULTI_VALUE_EXCLUSIVE|" +
        "BASELINE_OPEN_TEXT_REQUIRED|BASELINE_OPEN_TEXT_WITHOUT_OPTION|BASELINE_AREA_PAYLOAD_INVALID|" +
        "BASELINE_FEEDING_NOT_APPLICABLE_INVALID|BASELINE_COGNITION_ETIOLOGY_INVALID|BASELINE_COGNITION_SOURCE_INVALID|" +
        "BASELINE_COGNITION_REFERENCE_WITHOUT_DATA|BASELINE_BEHAVIOR_PATTERNS_INVALID|BASELINE_BARTHEL_OPTION_INVALID|" +
        "BASELINE_DRAFT_CANCELLATION_REASON_REQUIRED|CLINICAL_EVENT_OBSERVATION_REQUIRED|" +
        "NURSING_ASSESSMENT_VITALS_INVALID|NURSING_ASSESSMENT_TEXT_TOO_LONG|NURSING_ASSESSMENT_EMPTY|REFERENCE_RANGE_INVALID|PROCESS_DEADLINES_INVALID|FAMILY_COMMUNICATION_SCHEDULE_INVALID|" +
        "NURSING_ASSESSMENT_REQUIRED|FAMILY_COMMUNICATION_DECISION_REQUIRED|FAMILY_COMMUNICATION_INVALID|FAMILY_COMMUNICATION_WITHDRAWAL_INVALID|APPOINTMENT_SERVICE_INVALID|APPOINTMENT_WINDOW_INVALID|APPOINTMENT_BLOCK_INVALID|APPOINTMENT_REASON_INVALID|APPOINTMENT_REQUEST_INVALID|APPOINTMENT_PROPOSAL_INVALID|APPOINTMENT_MODALITY_UNAVAILABLE|" +
        "FOLLOW_UP_PLAN_REQUIRED|FOLLOW_UP_ACTION_INVALID|FOLLOW_UP_TEAM_INVALID|CLINICAL_EVENT_ESCALATION_REASON_INVALID|" +
        "MEDICAL_ASSESSMENT_TEXT_TOO_LONG|MEDICAL_ASSESSMENT_EMPTY|MEDICAL_ASSESSMENT_REQUIRED|MEDICAL_INDICATIONS_PENDING|MEDICAL_INDICATION_INVALID|" +
        "MEDICAL_INDICATION_INCIDENT_INVALID|MEDICAL_FOLLOW_UP_OBJECTIVE_REQUIRED|MEDICAL_FOLLOW_UP_ALREADY_STARTED|" +
        "URGENT_PROTOCOL_ACTIVATION_INVALID|URGENT_PROTOCOL_ENTRY_INVALID|REFERRAL_REPORT_INVALID|FAMILY_CALL_ATTEMPT_INVALID|" +
        "REFERRAL_REPORT_REQUIRED|REFERRAL_FAMILY_UPDATE_REQUIRED|REFERRAL_CALL_ATTEMPT_REQUIRED|" +
        "ASSESSMENT_CORRECTION_NOT_AVAILABLE|ASSESSMENT_CORRECTION_WINDOW_EXPIRED|ASSESSMENT_RECTIFICATION_WINDOW_OPEN|" +
        "ASSESSMENT_CORRECTION_REASON_INVALID|ASSESSMENT_RECTIFICATION_TEXT_INVALID|RESIDENT_IDENTITY_CORRECTION_INVALID|RESIDENT_PHOTO_INVALID|CENTER_LOGO_INVALID|RESIDENT_TRANSFER_INVALID|RESIDENT_DISCHARGE_INVALID|RESIDENT_SUSPENSION_INVALID|" +
        "FAMILY_MEMBER_INVALID|FAMILY_AUTHORIZATION_CHANGE_INVALID|EMERGENCY_CONTACT_INVALID|" +
        "ACCOUNT_INVALID|ACCOUNT_SELF_CHANGE|ACCOUNT_OTHER_CENTERS|ACCOUNT_LINK_INVALID|ACCOUNT_LINK_SECOND_FACTOR|PROFILE_UNIT_LAST|ADMIN_PRINCIPAL_LAST|PROFILE_SCOPE_INVALID|UNIT_INVALID|UNIT_HAS_RESIDENTS|BUILDING_INVALID|FLOOR_INVALID|ROOM_INVALID|PLACE_INVALID|STRUCTURE_HAS_ACTIVE_CHILDREN|LAYOUT_HAS_RESIDENTS|CENTER_INVALID|SHIFT_INVALID|TEAM_INVALID|SCHEDULE_INVALID)$",
        RegexOptions.Compiled);

    private static readonly Regex ConflictPattern = new(
        "^(?:IDEMPOTENCY_KEY_REUSED(?:_WITH_DIFFERENT_REQUEST)?|IDEMPOTENCY_OPERATION_IN_PROGRESS|CLINICAL_ACCESS_DECLARATION_INVALID|" +
        "BASELINE_DRAFT_REVISION_CONFLICT|CLINICAL_EVENT_REVISION_CONFLICT|REFERENCE_RANGES_REVISION_CONFLICT|PROCESS_DEADLINES_REVISION_CONFLICT|FAMILY_COMMUNICATION_SCHEDULE_REVISION_CONFLICT|FAMILY_COMMUNICATION_ALREADY_PUBLISHED|APPOINTMENT_SERVICE_REVISION_CONFLICT|APPOINTMENT_WINDOW_OVERLAP|APPOINTMENT_SLOT_UNAVAILABLE|APPOINTMENT_SERVICE_DISABLED|APPOINTMENT_LIMIT_REACHED|APPOINTMENT_CHANGE_TOO_LATE|APPOINTMENT_NOT_CHANGEABLE|APPOINTMENT_NOT_FINISHABLE|APPOINTMENT_REQUEST_DISABLED|APPOINTMENT_REQUEST_LIMIT_REACHED|APPOINTMENT_REQUEST_NOT_OPEN|APPOINTMENT_REQUEST_EXPIRED|FAMILY_COMMUNICATION_NOT_PUBLISHED|FAMILY_COMMUNICATION_WITHDRAWN|FAMILY_COMMUNICATION_NOT_VISIBLE|RESIDENT_PHOTO_REFUSED|FAMILY_COMMUNICATION_REVISION_CONFLICT|FAMILY_COMMUNICATION_CORRECTION_CLOSED|" +
        "FOLLOW_UP_TRANSFER_NOT_PENDING|MEDICAL_INDICATION_REVISION_CONFLICT|REFERRAL_REPORT_CHANGED|" +
        "REFERRAL_REPORT_ALREADY_SIGNED|ASSESSMENT_CORRECTION_CONFLICT|RESIDENT_IDENTITY_CORRECTION_CONFLICT|" +
        "FAMILY_AUTHORIZATION_CONFLICT|FAMILY_PORTAL_ACCESS_CONFLICT|FAMILY_MEMBER_CONFLICT|EMERGENCY_CONTACT_CONFLICT|ACCOUNT_SUBJECT_TAKEN|ACCOUNT_CHANGE_CONFLICT|" +
        "PROFILE_SCOPE_DUPLICATE|UNIT_CODE_TAKEN|UNIT_NAME_TAKEN|UNIT_CHANGE_CONFLICT|BUILDING_NAME_TAKEN|FLOOR_NAME_TAKEN|ROOM_NAME_TAKEN|PLACE_NAME_TAKEN|PLACE_OCCUPIED|RESIDENT_TRANSFER_CONFLICT|RESIDENT_STATUS_CONFLICT|STRUCTURE_CHANGE_CONFLICT|CENTER_CODE_TAKEN|SHIFT_NAME_TAKEN|TEAM_NAME_TAKEN|SCHEDULING_CHANGE_CONFLICT|SCHEDULE_CONFLICTS)$", RegexOptions.Compiled);

    private static readonly Dictionary<string, (ApplicationFailureCode Code, string Text)> PositionFailures = new()
    {
        ["POSITION_INVALID"] = (ApplicationFailureCode.InvalidInput,
            "Revisa los datos del cargo: el nombre es obligatorio (hasta 100 caracteres), la nota admite hasta 500 y las unidades tienen que ser de tu ámbito."),
        ["POSITION_OCCUPANT_INVALID"] = (ApplicationFailureCode.InvalidInput,
            "Elige una cuenta del centro o escribe un nombre (hasta 200 caracteres), pero no las dos cosas."),
        ["POSITION_CYCLE"] = (ApplicationFailureCode.InvalidInput, "Un cargo no puede depender de sí mismo ni de uno de sus subordinados."),
        ["POSITION_NAME_TAKEN"] = (ApplicationFailureCode.Conflict, "Ya existe un cargo con ese nombre en el centro."),
        ["POSITION_SINGLE_OCCUPANT"] = (ApplicationFailureCode.Conflict,
            "Un cargo de una sola persona no puede tener más de una: quita antes a las demás."),
        ["POSITION_HAS_OCCUPANTS"] = (ApplicationFailureCode.Conflict, "Quita antes a las personas que ocupan este cargo."),
        ["POSITION_HAS_SUBORDINATES"] = (ApplicationFailureCode.Conflict,
            "Hay cargos activos que dependen de este: cámbiales el superior o inactívalos antes."),
        ["POSITION_INACTIVE"] = (ApplicationFailureCode.Conflict, "El cargo está inactivo: reactívalo para ponerle personas."),
        ["POSITION_SUPERIOR_INACTIVE"] = (ApplicationFailureCode.Conflict,
            "Su cargo superior está inactivo: reactiva antes ese cargo o elige otro superior."),
        ["POSITION_TEMPLATE_NOT_EMPTY"] = (ApplicationFailureCode.Conflict, "La plantilla solo se carga cuando el centro aún no tiene cargos."),
        ["POSITION_CHANGE_CONFLICT"] = (ApplicationFailureCode.Conflict,
            "El cargo ya estaba así, o alguien lo ha cambiado mientras tanto: vuelve a cargar la pantalla."),
    };

    public static async Task<ApplicationResult<T>> RunAsync<T>(Func<Task<T>> run)
    {
        try
        {
            return ApplicationResult<T>.Success(await run());
        }
        catch (Exception error)
        {
            return ApplicationResult<T>.Failed(Normalize(error));
        }
    }

    /// <summary>El alta sin cama en una unidad con camas libres (CJ, foto 3.1): el formulario lo reconoce por este texto.</summary>
    public const string PlaceRequiredMessage = "Esta unidad tiene camas libres: elige una o «Pendiente de asignar».";

    public static ApplicationFailure Normalize(Exception error)
    {
        var message = error.Message;
        var code = ApplicationFailureCode.Unavailable;

        if (message == "PLACE_REQUIRED")
        {
            return new ApplicationFailure(ApplicationFailureCode.InvalidInput, PlaceRequiredMessage);
        }

        if (message == "RESIDENT_SUSPENDED")
        {
            // Script 0041: la base de datos impide escribir sobre un residente suspendido por un ingreso hospitalario prolongado.
            return new ApplicationFailure(ApplicationFailureCode.Conflict,
                "El residente está suspendido por un ingreso hospitalario: no se puede actuar sobre él hasta que Administración reanude su atención.");
        }

        if (PositionFailures.TryGetValue(message, out var position))
        {
            // ADM-07 (script 0073): el organigrama dice qué pasa con el cargo, no un mensaje genérico. Los triggers de la BD lanzan los mismos códigos.
            return new ApplicationFailure(position.Code, position.Text);
        }

        if (message.StartsWith("EXCHANGE_", StringComparison.Ordinal))
        {
            // ADR 0010: el fichero de la importación (o el de ejemplo de una exportación) o lo elegido en la pantalla no se puede usar; se
            // dice por qué.
            var exchangeText = message switch
            {
                "EXCHANGE_FILE_INVALID" => "No se puede leer el fichero: tiene que ser CSV, XLS o XLSX, con los nombres de las columnas en la primera fila.",
                "EXCHANGE_FILE_TOO_LARGE" => "El fichero es demasiado grande: como mucho 5 MB, 2.000 filas y 60 columnas.",
                "EXCHANGE_MAPPING_INVALID" => "Lo elegido en la pantalla no corresponde al fichero: vuelve a pulsar «Analizar el fichero».",
                "EXCHANGE_TEMPLATE_NAME_INVALID" => "Escribe un nombre para la plantilla (hasta 100 caracteres).",
                "EXCHANGE_EXPORT_EMPTY" => "Elige al menos una columna que se rellene con datos de ResidApp.",
                _ => null,
            };
            if (exchangeText is not null)
            {
                return new ApplicationFailure(ApplicationFailureCode.InvalidInput, exchangeText);
            }
        }

        if (error is AccessDeniedException || AccessDeniedPattern.IsMatch(message))
        {
            code = ApplicationFailureCode.AccessDenied;
        }
        else if (InvalidInputPattern.IsMatch(message))
        {
            code = ApplicationFailureCode.InvalidInput;
        }
        else if (ConflictPattern.IsMatch(message))
        {
            code = ApplicationFailureCode.Conflict;
        }

        var text = code switch
        {
            ApplicationFailureCode.AccessDenied => "No se puede acceder a esta operación.",
            ApplicationFailureCode.InvalidInput => "Revisa los datos de la operación.",
            ApplicationFailureCode.Conflict => "La operación entra en conflicto con una solicitud anterior.",
            _ => "No se ha podido completar la operación.",
        };
        return new ApplicationFailure(code, text);
    }
}
