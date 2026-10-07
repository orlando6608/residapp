namespace ResidApp.Domain.Auxiliar;

/// <summary>Aviso visual sobre la temperatura que registra la Auxiliar (CJ, 2026-10-06). No bloquea el
/// registro ni cambia la clasificación del cambio: la Auxiliar sigue eligiendo si es ordinario o prioritario.</summary>
public enum AuxiliarTemperatureAlert
{
    Ninguno,

    /// <summary>Por encima de 37 °C: se mantiene el seguimiento (por las auxiliares o por Enfermería).</summary>
    Seguimiento,

    /// <summary>Por encima de 38 °C: hay que avisar a Enfermería.</summary>
    AvisarEnfermeria,
}

public static class AuxiliarTemperatureAlerts
{
    public const decimal FollowUpAboveCelsius = 37m;
    public const decimal NotifyNursingAboveCelsius = 38m;

    /// <summary>Umbrales estrictos: 37,0 no avisa y 38,0 solo mantiene el seguimiento. Sin valor, sin aviso.</summary>
    public static AuxiliarTemperatureAlert Evaluate(decimal? temperatureCelsius) => temperatureCelsius switch
    {
        > NotifyNursingAboveCelsius => AuxiliarTemperatureAlert.AvisarEnfermeria,
        > FollowUpAboveCelsius => AuxiliarTemperatureAlert.Seguimiento,
        _ => AuxiliarTemperatureAlert.Ninguno,
    };
}
