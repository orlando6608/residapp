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

/// <summary>Cómo está el plazo de conservación de un residente dado de baja respecto de hoy.</summary>
public enum ResidentRetentionStatus { Vigente, VenceEnBreve, Vencido }

/// <summary>Reglas de la baja: los datos no se borran solos y se conservan 15 años desde la baja. Fuente: art. 12 de la Llei 21/2000 de Cataluña, en la
/// redacción de la Ley 16/2010 (BOE-A-2010-10215): 15 años desde el alta de cada proceso asistencial para consentimientos, informes de alta y de pruebas, entre otros;
/// el resto «puede destruirse» a los 5 años, y la Comissió Tècnica de Documentació Clínica recomienda 15 años para el curso clínico. Un plazo único y prudente para todo
/// lo clínico; el centro, como responsable, y su delegado de protección de datos pueden acortar lo que la ley permita (docs/privacidad/expurgo-y-supresion.md).
/// Sustituye a los 5 años de CJ, que citaba «la RGPD» (el Reglamento no fija ese plazo).</summary>
public static class ResidentDischarge
{
    public const int RetentionYears = 15;

    /// <summary>Con cuántos meses de antelación se avisa de que el plazo está a punto de vencer.</summary>
    public const int RetentionWarningMonths = 12;
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

    /// <summary>Vencido si la fecha de conservación ya pasó; «vence en breve» si llega dentro de los próximos 12 meses. Solo informa: ResidApp no borra nada.</summary>
    public static ResidentRetentionStatus RetentionStatus(DateOnly retainUntil, DateOnly today) =>
        retainUntil < today ? ResidentRetentionStatus.Vencido
        : retainUntil <= today.AddMonths(RetentionWarningMonths) ? ResidentRetentionStatus.VenceEnBreve
        : ResidentRetentionStatus.Vigente;

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
