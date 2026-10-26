using ResidApp.Shared;

namespace ResidApp.Domain.Supervision;

/// <summary>
/// Cierre excepcional de un episodio con indicaciones médicas que nadie va a cumplir (CJ, tercera tanda 5.1; script 0075): lo hace solo
/// Dirección Clínica y exige un motivo, que queda en la auditoría con el nombre de quien cierra. Las indicaciones no se tocan.
/// </summary>
public static class ExceptionalEpisodeClosure
{
    /// <summary>Como la justificación de las lecturas clínicas (eventos_auditoria.justificacion es NVARCHAR(300)).</summary>
    public const int MaxReasonLength = 300;

    public const string InvalidCode = "EXCEPTIONAL_CLOSURE_INVALID";

    /// <summary>Se rechaza el episodio sin indicaciones abiertas: no es un cierre libre, es la salida de un episodio que no se puede cerrar de otra forma.</summary>
    public const string NotApplicableCode = "EXCEPTIONAL_CLOSURE_NOT_APPLICABLE";

    /// <summary>El motivo sin espacios sobrantes; si falta o se pasa de largo, EXCEPTIONAL_CLOSURE_INVALID.</summary>
    public static string NormalizeReason(string? reason) =>
        reason?.Trim() is { Length: > 0 and <= MaxReasonLength } trimmed ? trimmed : throw new DomainValidationException(InvalidCode);
}
