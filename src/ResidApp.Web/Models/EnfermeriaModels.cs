using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;

namespace ResidApp.Web.Models;

/// <summary>ENF-18: identidad mínima del residente del ámbito más el resumen del basal vigente (null si
/// todavía no tiene ninguno firmado).</summary>
public sealed record EnfermeriaResidentDetailViewModel(ScopeResidentSummary Resident, CurrentBaselineSummary? Baseline);

/// <summary>ENF-01: contadores de las dos bandejas ya construidas (grupo E4). Seguimientos, indicaciones y
/// comunicaciones siguen sin contador propio hasta que existan esos grupos.</summary>
public sealed record EnfermeriaInicioViewModel(int Ordinarios, int Prioritarios);

/// <summary>ENF-04: detalle de un cambio recibido más el resumen del basal vigente del residente (null si
/// todavía no tiene ninguno firmado), igual que EnfermeriaResidentDetailViewModel.</summary>
public sealed record EnfermeriaChangeDetailViewModel(PendingChangeDetail Change, CurrentBaselineSummary? Baseline);

/// <summary>ENF-02/ENF-03 "antigüedad": tiempo transcurrido desde el registro, en la unidad más grande que
/// tenga sentido (días, horas o minutos) — no hace falta más precisión para ordenar visualmente una
/// bandeja.</summary>
public static class ElapsedTimeDisplay
{
    public static string Since(DateTimeOffset occurredAt)
    {
        var elapsed = DateTimeOffset.UtcNow - occurredAt;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }
        if (elapsed.TotalDays >= 1)
        {
            return $"{(int)elapsed.TotalDays} d";
        }
        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours} h {elapsed.Minutes} min";
        }
        return $"{Math.Max(1, elapsed.Minutes)} min";
    }
}

/// <summary>ENF-16 "Registrar un evento observado por Enfermería", en su alcance mínimo: solo registrar y
/// guardar. Clasificacion reutiliza el mismo catálogo cerrado que AUX-10 (Ordinario/Prioritario); a
/// diferencia de RegistrarCambioFormModel (Auxiliar), aquí no hay motivo prioritario ni aviso directo: ese
/// ritual existe para que Auxiliar alerte a Enfermería antes de que exista una bandeja que lo recoja, y
/// aquí Enfermería ya es autora y destinataria a la vez.</summary>
public sealed class RegistrarEventoFormModel
{
    [Required]
    public Guid ResidenteId { get; set; }

    [Required]
    public Guid OperacionId { get; set; }

    [Required(ErrorMessage = "Indica qué observas.")]
    [StringLength(2000)]
    [Display(Name = "Observación")]
    public string Observacion { get; set; } = string.Empty;

    [Required(ErrorMessage = "Elige una clasificación.")]
    [Display(Name = "Clasificación inicial")]
    public DailyChangeClassification Clasificacion { get; set; } = DailyChangeClassification.Ordinario;

    [StringLength(2000)]
    [Display(Name = "Datos clínicos pertinentes")]
    public string? DatosClinicosPertinentes { get; set; }
}

/// <summary>Estado del evento en bandejas y detalle (ENF-02 a ENF-04). "En valoración" usa el amarillo de
/// aviso con texto oscuro (text-bg-warning), nunca texto blanco sobre amarillo.</summary>
public static class ClinicalEventStatusDisplay
{
    public static string Label(ClinicalEventStatus status) => status switch
    {
        ClinicalEventStatus.Pendiente => "Pendiente",
        ClinicalEventStatus.EnValoracion => "En valoración",
        _ => status.ToString(),
    };

    public static string BadgeClass(ClinicalEventStatus status, bool prioritario) => status switch
    {
        ClinicalEventStatus.EnValoracion => "text-bg-warning",
        _ => prioritario ? "text-bg-danger" : "text-bg-secondary",
    };
}

/// <summary>ENF-05 "Valoración de Enfermería": hallazgos, valoración, actuaciones, comunicaciones,
/// resultado y constantes opcionales. Revision es la del evento al abrir la pantalla (concurrencia
/// optimista). Las comprobaciones de formato se repiten en dominio (VitalSigns), que es quien decide.</summary>
public sealed class ValoracionFormModel
{
    [Required]
    public Guid EventoId { get; set; }

    [Required]
    public int Revision { get; set; }

    [StringLength(NursingAssessmentContent.MaxTextLength)]
    [Display(Name = "Hallazgos")]
    public string? Hallazgos { get; set; }

    [StringLength(NursingAssessmentContent.MaxTextLength)]
    [Display(Name = "Valoración")]
    public string? Valoracion { get; set; }

    [StringLength(NursingAssessmentContent.MaxTextLength)]
    [Display(Name = "Actuaciones")]
    public string? Actuaciones { get; set; }

    [StringLength(NursingAssessmentContent.MaxTextLength)]
    [Display(Name = "Comunicaciones")]
    public string? Comunicaciones { get; set; }

    [StringLength(NursingAssessmentContent.MaxTextLength)]
    [Display(Name = "Resultado")]
    public string? Resultado { get; set; }

    [Range(typeof(decimal), "0.1", "99.9", ParseLimitsInInvariantCulture = true, ErrorMessage = "Indica una temperatura válida.")]
    [Display(Name = "Temperatura (°C)")]
    public decimal? TemperaturaCelsius { get; set; }

    [Range(1, 999, ErrorMessage = "Indica un valor positivo.")]
    [Display(Name = "PA sistólica (mmHg)")]
    public int? TensionSistolica { get; set; }

    [Range(1, 999, ErrorMessage = "Indica un valor positivo.")]
    [Display(Name = "PA diastólica (mmHg)")]
    public int? TensionDiastolica { get; set; }

    [Range(1, 999, ErrorMessage = "Indica un valor positivo.")]
    [Display(Name = "Frecuencia cardíaca (lpm)")]
    public int? FrecuenciaCardiaca { get; set; }

    [Range(1, 999, ErrorMessage = "Indica un valor positivo.")]
    [Display(Name = "Frecuencia respiratoria (rpm)")]
    public int? FrecuenciaRespiratoria { get; set; }

    [Range(0, 100, ErrorMessage = "La saturación va de 0 a 100 %.")]
    [Display(Name = "Saturación de O₂ (%)")]
    public int? SaturacionO2 { get; set; }

    [Display(Name = "Aire ambiente u oxigenoterapia")]
    public RespiratorySupportCode? SoporteRespiratorio { get; set; }

    [Range(typeof(decimal), "0.1", "99.9", ParseLimitsInInvariantCulture = true, ErrorMessage = "Indica un flujo válido.")]
    [Display(Name = "Flujo de O₂ (L/min)")]
    public decimal? FlujoO2 { get; set; }

    [Range(1, 9999, ErrorMessage = "Indica un valor positivo.")]
    [Display(Name = "Glucemia (mg/dL)")]
    public int? Glucemia { get; set; }

    [StringLength(100)]
    [Display(Name = "Otra constante: nombre")]
    public string? OtraConstanteNombre { get; set; }

    [StringLength(50)]
    [Display(Name = "Valor")]
    public string? OtraConstanteValor { get; set; }

    [StringLength(30)]
    [Display(Name = "Unidad")]
    public string? OtraConstanteUnidad { get; set; }

    public static ValoracionFormModel From(PendingChangeDetail detail)
    {
        var form = new ValoracionFormModel { EventoId = detail.EventId, Revision = detail.Revision };
        if (detail.Assessment is not { Content: var content })
        {
            return form;
        }
        var vitals = content.Vitals;
        form.Hallazgos = content.Findings;
        form.Valoracion = content.Assessment;
        form.Actuaciones = content.Actions;
        form.Comunicaciones = content.Communications;
        form.Resultado = content.Outcome;
        form.TemperaturaCelsius = vitals.TemperatureCelsius;
        form.TensionSistolica = vitals.SystolicMmHg;
        form.TensionDiastolica = vitals.DiastolicMmHg;
        form.FrecuenciaCardiaca = vitals.HeartRateBpm;
        form.FrecuenciaRespiratoria = vitals.RespiratoryRateRpm;
        form.SaturacionO2 = vitals.OxygenSaturationPct;
        form.SoporteRespiratorio = vitals.RespiratorySupport;
        form.FlujoO2 = vitals.OxygenFlowLpm;
        form.Glucemia = vitals.GlucoseMgDl;
        form.OtraConstanteNombre = vitals.OtherName;
        form.OtraConstanteValor = vitals.OtherValue;
        form.OtraConstanteUnidad = vitals.OtherUnit;
        return form;
    }
}

/// <summary>ENF-05: el formulario más el evento del que cuelga (residente, origen y observación original,
/// que se muestra como referencia y nunca se edita).</summary>
public sealed record ValoracionViewModel(PendingChangeDetail Event, ValoracionFormModel Form);

/// <summary>Resumen en una línea de las constantes registradas, solo las que tienen valor.</summary>
public static class VitalSignsDisplay
{
    public static string Summary(VitalSigns v)
    {
        var parts = new List<string>();
        if (v.TemperatureCelsius is not null) parts.Add($"T {v.TemperatureCelsius:0.0} °C");
        if (v.SystolicMmHg is not null) parts.Add($"PA {v.SystolicMmHg}/{v.DiastolicMmHg} mmHg");
        if (v.HeartRateBpm is not null) parts.Add($"FC {v.HeartRateBpm} lpm");
        if (v.RespiratoryRateRpm is not null) parts.Add($"FR {v.RespiratoryRateRpm} rpm");
        if (v.OxygenSaturationPct is not null) parts.Add($"SpO₂ {v.OxygenSaturationPct} %");
        if (v.RespiratorySupport is not null)
        {
            parts.Add(v.RespiratorySupport == RespiratorySupportCode.Oxigenoterapia
                ? v.OxygenFlowLpm is null ? "oxigenoterapia" : $"oxigenoterapia a {v.OxygenFlowLpm:0.#} L/min"
                : "aire ambiente");
        }
        if (v.GlucoseMgDl is not null) parts.Add($"glucemia {v.GlucoseMgDl} mg/dL");
        if (v.OtherName is not null) parts.Add($"{v.OtherName} {v.OtherValue}{(v.OtherUnit is null ? "" : " " + v.OtherUnit)}");
        return string.Join(" · ", parts);
    }
}


/// <summary>Textos de los rangos de referencia de constantes y de sus avisos (ENF-05). El aviso nombra la
/// constante, el valor y el rango en texto: no depende solo del color.</summary>
public static class VitalSignRangeDisplay
{
    public static string Label(VitalSignCode code) => code switch
    {
        VitalSignCode.Temperatura => "Temperatura",
        VitalSignCode.TensionSistolica => "PA sistólica",
        VitalSignCode.TensionDiastolica => "PA diastólica",
        VitalSignCode.FrecuenciaCardiaca => "Frecuencia cardíaca",
        VitalSignCode.FrecuenciaRespiratoria => "Frecuencia respiratoria",
        VitalSignCode.SaturacionO2 => "Saturación de O₂",
        VitalSignCode.Glucemia => "Glucemia",
        _ => code.ToString(),
    };

    public static string Unit(VitalSignCode code) => code switch
    {
        VitalSignCode.Temperatura => "°C",
        VitalSignCode.TensionSistolica or VitalSignCode.TensionDiastolica => "mmHg",
        VitalSignCode.FrecuenciaCardiaca => "lpm",
        VitalSignCode.FrecuenciaRespiratoria => "rpm",
        VitalSignCode.SaturacionO2 => "%",
        VitalSignCode.Glucemia => "mg/dL",
        _ => string.Empty,
    };

    public static string Range(VitalSignRange range) => (range.Min, range.Max) switch
    {
        ({ } min, { } max) => $"{min:0.#}–{max:0.#} {Unit(range.Code)}",
        ({ } min, null) => $"≥ {min:0.#} {Unit(range.Code)}",
        (null, { } max) => $"≤ {max:0.#} {Unit(range.Code)}",
        _ => string.Empty,
    };

    public static string Alert(VitalSignAlert alert) =>
        $"{Label(alert.Code)} {alert.Value:0.#} {Unit(alert.Code)}: " +
        $"{(alert.Deviation == VitalSignDeviation.PorDebajo ? "por debajo" : "por encima")} del rango de referencia ({Range(alert.Range)})";

    /// <summary>Texto de ayuda bajo el campo del formulario, o null si el centro no tiene rango para esa constante.</summary>
    public static string? Hint(IReadOnlyList<VitalSignRange> ranges, VitalSignCode code) =>
        ranges.FirstOrDefault(r => r.Code == code) is { } range ? $"Referencia del centro: {Range(range)}" : null;
}
