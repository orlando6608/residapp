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
        new("(?:RESIDENT_CREATE|BASELINE_SIGN|CLINICAL_DETAIL_READ|BASELINE_DRAFT)_NOT_AUTHORIZED", RegexOptions.Compiled);

    private static readonly Regex InvalidInputPattern = new(
        "^(?:RESIDENT_CREATE_INPUT_INVALID|BASELINE_SIGN_INPUT_INVALID|CLINICAL_DETAIL_READ_INPUT_INVALID|" +
        "APPLICATION_INPUT_INVALID|BASELINE_AREAS_INCOMPLETE|BASELINE_BARTHEL_INCOMPLETE|" +
        "DAILY_CLOSURE_REASON_REQUIRED|DAILY_CHANGE_AREAS_REQUIRED|DAILY_CHANGE_AREA_CONTENT_REQUIRED|" +
        "DAILY_CHANGE_AREA_DUPLICATED|DAILY_CHANGE_AREA_OPTION_INVALID|DAILY_CHANGE_PRIORITY_DOCUMENTATION_REQUIRED|" +
        "BASELINE_DRAFT_ALREADY_ACTIVE|BASELINE_MULTI_VALUE_INVALID|BASELINE_MULTI_VALUE_EXCLUSIVE|" +
        "BASELINE_OPEN_TEXT_REQUIRED|BASELINE_OPEN_TEXT_WITHOUT_OPTION|BASELINE_AREA_PAYLOAD_INVALID|" +
        "BASELINE_FEEDING_NOT_APPLICABLE_INVALID|BASELINE_COGNITION_ETIOLOGY_INVALID|BASELINE_COGNITION_SOURCE_INVALID|" +
        "BASELINE_COGNITION_REFERENCE_WITHOUT_DATA|BASELINE_BEHAVIOR_PATTERNS_INVALID|BASELINE_BARTHEL_OPTION_INVALID|" +
        "BASELINE_DRAFT_CANCELLATION_REASON_REQUIRED)$", RegexOptions.Compiled);

    private static readonly Regex ConflictPattern = new(
        "^(?:IDEMPOTENCY_KEY_REUSED(?:_WITH_DIFFERENT_REQUEST)?|IDEMPOTENCY_OPERATION_IN_PROGRESS|" +
        "BASELINE_DRAFT_REVISION_CONFLICT)$", RegexOptions.Compiled);

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

    public static ApplicationFailure Normalize(Exception error)
    {
        var message = error.Message;
        var code = ApplicationFailureCode.Unavailable;

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
