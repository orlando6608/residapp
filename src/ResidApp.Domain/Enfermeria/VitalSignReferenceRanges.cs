using ResidApp.Shared;

namespace ResidApp.Domain.Enfermeria;

/// <summary>Constantes que admiten rango de referencia (dbo.rangos_referencia_constantes.constante_codigo).</summary>
public enum VitalSignCode
{
    [Code("TEMPERATURA")] Temperatura,
    [Code("TENSION_SISTOLICA")] TensionSistolica,
    [Code("TENSION_DIASTOLICA")] TensionDiastolica,
    [Code("FRECUENCIA_CARDIACA")] FrecuenciaCardiaca,
    [Code("FRECUENCIA_RESPIRATORIA")] FrecuenciaRespiratoria,
    [Code("SATURACION_O2")] SaturacionO2,
    [Code("GLUCEMIA")] Glucemia,
}

/// <summary>Rango de referencia de una constante en un centro: mínimo, máximo o ambos.</summary>
public sealed record VitalSignRange(VitalSignCode Code, decimal? Min, decimal? Max);

public enum VitalSignDeviation
{
    PorDebajo,
    PorEncima,
}

/// <summary>Una constante registrada fuera del rango de referencia de su centro.</summary>
public sealed record VitalSignAlert(VitalSignCode Code, decimal Value, VitalSignRange Range, VitalSignDeviation Deviation);

/// <summary>
/// Compara las constantes registradas con los rangos de referencia del centro. Solo produce avisos
/// informativos: no bloquea el guardado ni cambia clasificación, prioridad ni desenlace (el sistema no
/// decide clínicamente, docs/producto/alcance.md). Una constante sin valor o sin rango no genera aviso.
/// </summary>
public static class VitalSignReferenceRanges
{
    /// <summary>Un rango configurable: al menos un límite, positivos, mínimo menor que máximo y SpO2 como
    /// máximo 100 %. Solo coherencia de formato: qué valores son clínicamente adecuados lo decide quien
    /// tiene el permiso REFERENCE_RANGES_MANAGE.</summary>
    public static void Validate(VitalSignRange range)
    {
        if ((range.Min is null && range.Max is null)
            || range.Min is <= 0 || range.Max is <= 0
            || (range.Min is not null && range.Max is not null && range.Min >= range.Max)
            || (range.Code == VitalSignCode.SaturacionO2 && (range.Min is > 100 || range.Max is > 100)))
        {
            throw new DomainValidationException("REFERENCE_RANGE_INVALID");
        }
    }

    public static IReadOnlyList<VitalSignAlert> Evaluate(VitalSigns vitals, IReadOnlyList<VitalSignRange> ranges)
    {
        var alerts = new List<VitalSignAlert>();
        foreach (var range in ranges)
        {
            var value = ValueOf(vitals, range.Code);
            if (value is null)
            {
                continue;
            }
            if (range.Min is not null && value < range.Min)
            {
                alerts.Add(new VitalSignAlert(range.Code, value.Value, range, VitalSignDeviation.PorDebajo));
            }
            else if (range.Max is not null && value > range.Max)
            {
                alerts.Add(new VitalSignAlert(range.Code, value.Value, range, VitalSignDeviation.PorEncima));
            }
        }
        return alerts.OrderBy(a => a.Code).ToList();
    }

    public static decimal? ValueOf(VitalSigns vitals, VitalSignCode code) => code switch
    {
        VitalSignCode.Temperatura => vitals.TemperatureCelsius,
        VitalSignCode.TensionSistolica => vitals.SystolicMmHg,
        VitalSignCode.TensionDiastolica => vitals.DiastolicMmHg,
        VitalSignCode.FrecuenciaCardiaca => vitals.HeartRateBpm,
        VitalSignCode.FrecuenciaRespiratoria => vitals.RespiratoryRateRpm,
        VitalSignCode.SaturacionO2 => vitals.OxygenSaturationPct,
        VitalSignCode.Glucemia => vitals.GlucoseMgDl,
        _ => null,
    };
}
