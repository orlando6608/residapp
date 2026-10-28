using ResidApp.Shared;

namespace ResidApp.Domain.Residents;

/// <summary>Para qué se piden los episodios de la unidad anterior (CJ, 2026-10-28, 3.2 B): las tres finalidades que ya usa Dirección para su lectura declarada más
/// «Otro motivo», que obliga a escribir un texto corto. Lista cerrada, con su código en la base de datos (script 0079).</summary>
public enum TransferHistoryPurpose
{
    [Code("CONTINUIDAD_ASISTENCIAL")] ContinuidadAsistencial,
    [Code("INCIDENCIA_RECLAMACION")] IncidenciaReclamacion,
    [Code("TRAZABILIDAD_DOCUMENTAL")] TrazabilidadDocumental,
    [Code("OTRO")] Otro,
}

/// <summary>El motivo ya validado: la finalidad, el texto opcional que la acompaña y lo que se guarda como motivo (la etiqueta de la finalidad y, si lo hay, el texto).</summary>
public sealed record TransferHistoryReason(TransferHistoryPurpose Purpose, string? Text, string Reason);

/// <summary>
/// Petición del historial tras un traslado (CJ, tercera tanda 5.2 C; script 0077): la unidad de destino abre los episodios cerrados de la unidad anterior solo
/// si lo pide con un motivo. Vale 1 hora por cuenta y residente y cada lectura queda auditada con ese motivo. Desde el 2026-10-28 el motivo es una finalidad de
/// una lista cerrada con un texto corto opcional (obligatorio en «Otro motivo»).
/// </summary>
public static class TransferHistoryRequest
{
    /// <summary>Largo del texto que acompaña a la finalidad: corto a propósito, para que no se cuelen datos de salud ni nombres (recomendación del agente DPO; lo fija CJ).</summary>
    public const int MaxTextLength = 150;

    public const string InvalidCode = "TRANSFER_HISTORY_INVALID";

    /// <summary>El residente no tiene episodios cerrados de una unidad anterior que este ámbito pueda abrir (o no está en el ámbito).</summary>
    public const string NotApplicableCode = "TRANSFER_HISTORY_NOT_APPLICABLE";

    public static string Label(TransferHistoryPurpose purpose) => purpose switch
    {
        TransferHistoryPurpose.ContinuidadAsistencial => "Revisión de continuidad asistencial",
        TransferHistoryPurpose.IncidenciaReclamacion => "Revisión de una incidencia o reclamación asistencial",
        TransferHistoryPurpose.TrazabilidadDocumental => "Verificación de trazabilidad documental",
        _ => "Otro motivo",
    };

    /// <summary>Valida la finalidad y el texto. Sin finalidad cuenta como «Otro motivo»; el texto sin espacios sobrantes, de hasta <see cref="MaxTextLength"/>, es obligatorio
    /// con «Otro motivo». Si no, TRANSFER_HISTORY_INVALID.</summary>
    public static TransferHistoryReason Normalize(TransferHistoryPurpose? purpose, string? text)
    {
        var chosen = purpose ?? TransferHistoryPurpose.Otro;
        var trimmed = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (!Enum.IsDefined(chosen) || trimmed?.Length > MaxTextLength || (chosen == TransferHistoryPurpose.Otro && trimmed is null))
        {
            throw new DomainValidationException(InvalidCode);
        }

        return new TransferHistoryReason(chosen, trimmed, trimmed is null ? Label(chosen) : $"{Label(chosen)} — {trimmed}");
    }
}
