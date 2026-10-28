using ResidApp.Shared;

namespace ResidApp.Domain.Residents;

/// <summary>
/// Petición del historial tras un traslado (CJ, tercera tanda 5.2 C; script 0077): la unidad de destino abre los episodios cerrados de la unidad anterior solo
/// si lo pide con un motivo. Vale 1 hora por cuenta y residente y cada lectura queda auditada con ese motivo.
/// </summary>
public static class TransferHistoryRequest
{
    /// <summary>Como la justificación de las lecturas clínicas (eventos_auditoria.justificacion es NVARCHAR(300)).</summary>
    public const int MaxReasonLength = 300;

    public const string InvalidCode = "TRANSFER_HISTORY_INVALID";

    /// <summary>El residente no tiene episodios cerrados de una unidad anterior que este ámbito pueda abrir (o no está en el ámbito).</summary>
    public const string NotApplicableCode = "TRANSFER_HISTORY_NOT_APPLICABLE";

    /// <summary>El motivo sin espacios sobrantes; si falta o se pasa de largo, TRANSFER_HISTORY_INVALID.</summary>
    public static string NormalizeReason(string? reason) =>
        reason?.Trim() is { Length: > 0 and <= MaxReasonLength } trimmed ? trimmed : throw new DomainValidationException(InvalidCode);
}
