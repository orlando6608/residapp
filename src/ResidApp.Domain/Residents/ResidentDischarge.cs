using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Residents;

/// <summary>Motivo de la baja de un residente (CJ, 2026-10-07). La lista no es cerrada para el centro: «Otro» exige escribir el motivo.</summary>
public enum ResidentDischargeReason
{
    [Code("FALLECIMIENTO")] [Display(Name = "Fallecimiento")] Fallecimiento,
    [Code("ALTA_VOLUNTARIA")] [Display(Name = "Alta voluntaria")] AltaVoluntaria,
    [Code("TRASLADO_OTRO_CENTRO")] [Display(Name = "Traslado a otro centro")] TrasladoOtroCentro,
    [Code("OTRO")] [Display(Name = "Otro motivo")] Otro,
}

/// <summary>Reglas de la baja: los datos nunca se borran y se conservan 5 años desde la baja (CJ: la normativa de protección de datos
/// y de documentación clínica que aplique al centro; en Cataluña, 5 años).</summary>
public static class ResidentDischarge
{
    public const int RetentionYears = 5;
    public const int MaxReasonTextLength = 500;
    public const string InvalidCode = "RESIDENT_DISCHARGE_INVALID";

    /// <summary>El texto del motivo, recortado; «Otro» lo exige y los demás motivos lo admiten opcional.</summary>
    public static string? ValidateReasonText(ResidentDischargeReason reason, string? text)
    {
        var trimmed = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if ((reason == ResidentDischargeReason.Otro && trimmed is null) || trimmed?.Length > MaxReasonTextLength)
        {
            throw new DomainValidationException(InvalidCode);
        }

        return trimmed;
    }

    /// <summary>Hasta cuándo se conservan los datos.</summary>
    public static DateOnly RetainUntil(DateTimeOffset dischargedAt) => DateOnly.FromDateTime(dischargedAt.UtcDateTime).AddYears(RetentionYears);

    /// <summary>El texto que se guarda como motivo de inactivación del residente.</summary>
    public static string Describe(ResidentDischargeReason reason, string? text) => reason switch
    {
        ResidentDischargeReason.Fallecimiento => Join("Fallecimiento", text),
        ResidentDischargeReason.AltaVoluntaria => Join("Alta voluntaria", text),
        ResidentDischargeReason.TrasladoOtroCentro => Join("Traslado a otro centro", text),
        _ => Join("Otro motivo", text),
    };

    private static string Join(string label, string? text) => text is null ? label : $"{label}: {text}";
}
