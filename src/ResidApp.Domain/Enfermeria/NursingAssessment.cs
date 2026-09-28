using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Enfermeria;

/// <summary>ENF-05 "aire/oxigenoterapia": en qué condiciones se midió la saturación.</summary>
public enum RespiratorySupportCode
{
    [Code("AIRE_AMBIENTE")] [Display(Name = "Aire ambiente")] AireAmbiente,
    [Code("OXIGENOTERAPIA")] [Display(Name = "Oxigenoterapia")] Oxigenoterapia,
}

/// <summary>
/// Constantes opcionales de la valoración de Enfermería (ENF-05): T, PA, FC, FR, SpO2, aire/oxigenoterapia,
/// flujo de O2, glucemia y otra constante con nombre/valor/unidad. Solo se valida formato y coherencia; el
/// sistema no aplica rangos clínicos de alarma ni infiere prioridad.
/// </summary>
public sealed record VitalSigns
{
    public decimal? TemperatureCelsius { get; }
    public int? SystolicMmHg { get; }
    public int? DiastolicMmHg { get; }
    public int? HeartRateBpm { get; }
    public int? RespiratoryRateRpm { get; }
    public int? OxygenSaturationPct { get; }
    public RespiratorySupportCode? RespiratorySupport { get; }
    public decimal? OxygenFlowLpm { get; }
    public int? GlucoseMgDl { get; }
    public string? OtherName { get; }
    public string? OtherValue { get; }
    public string? OtherUnit { get; }

    public VitalSigns(
        decimal? temperatureCelsius, int? systolicMmHg, int? diastolicMmHg, int? heartRateBpm, int? respiratoryRateRpm,
        int? oxygenSaturationPct, RespiratorySupportCode? respiratorySupport, decimal? oxygenFlowLpm, int? glucoseMgDl,
        string? otherName, string? otherValue, string? otherUnit)
    {
        otherName = Normalize(otherName);
        otherValue = Normalize(otherValue);
        otherUnit = Normalize(otherUnit);

        var positives = new decimal?[] { temperatureCelsius, systolicMmHg, diastolicMmHg, heartRateBpm, respiratoryRateRpm, oxygenFlowLpm, glucoseMgDl };
        if (positives.Any(v => v is <= 0)
            || systolicMmHg.HasValue != diastolicMmHg.HasValue
            || oxygenSaturationPct is < 0 or > 100
            || (oxygenFlowLpm.HasValue && respiratorySupport != RespiratorySupportCode.Oxigenoterapia)
            || ((otherName is null) != (otherValue is null))
            || (otherUnit is not null && otherName is null))
        {
            throw new DomainValidationException("NURSING_ASSESSMENT_VITALS_INVALID");
        }

        TemperatureCelsius = temperatureCelsius;
        SystolicMmHg = systolicMmHg;
        DiastolicMmHg = diastolicMmHg;
        HeartRateBpm = heartRateBpm;
        RespiratoryRateRpm = respiratoryRateRpm;
        OxygenSaturationPct = oxygenSaturationPct;
        RespiratorySupport = respiratorySupport;
        OxygenFlowLpm = oxygenFlowLpm;
        GlucoseMgDl = glucoseMgDl;
        OtherName = otherName;
        OtherValue = otherValue;
        OtherUnit = otherUnit;
    }

    public bool IsEmpty =>
        TemperatureCelsius is null && SystolicMmHg is null && HeartRateBpm is null && RespiratoryRateRpm is null
        && OxygenSaturationPct is null && RespiratorySupport is null && GlucoseMgDl is null && OtherName is null;

    internal static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>
/// Contenido de la valoración de Enfermería (ENF-05): hallazgos, valoración, actuaciones, comunicaciones,
/// resultado y constantes. Nunca incluye ni modifica la observación original del evento, que vive en su
/// tabla de origen. Un guardado sin ningún dato se rechaza: no aporta nada y ocultaría que no se valoró.
/// </summary>
public sealed record NursingAssessmentContent
{
    public const int MaxTextLength = 2000;

    public string? Findings { get; }
    public string? Assessment { get; }
    public string? Actions { get; }
    public string? Communications { get; }
    public string? Outcome { get; }
    public VitalSigns Vitals { get; }

    public NursingAssessmentContent(
        string? findings, string? assessment, string? actions, string? communications, string? outcome, VitalSigns vitals)
    {
        findings = VitalSigns.Normalize(findings);
        assessment = VitalSigns.Normalize(assessment);
        actions = VitalSigns.Normalize(actions);
        communications = VitalSigns.Normalize(communications);
        outcome = VitalSigns.Normalize(outcome);

        var texts = new[] { findings, assessment, actions, communications, outcome };
        if (texts.Any(t => t is { Length: > MaxTextLength }))
        {
            throw new DomainValidationException("NURSING_ASSESSMENT_TEXT_TOO_LONG");
        }
        if (texts.All(t => t is null) && vitals.IsEmpty)
        {
            throw new DomainValidationException("NURSING_ASSESSMENT_EMPTY");
        }

        Findings = findings;
        Assessment = assessment;
        Actions = actions;
        Communications = communications;
        Outcome = outcome;
        Vitals = vitals;
    }
}
