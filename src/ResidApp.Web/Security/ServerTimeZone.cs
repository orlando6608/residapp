namespace ResidApp.Web.Security;

/// <summary>La aplicación calcula las franjas de las citas, el horario de los comunicados, los días de los indicadores y todas las horas que muestra
/// con la zona horaria del servidor (<see cref="TimeZoneInfo.Local"/>). En Azure sale de WEBSITE_TIME_ZONE=Europe/Madrid, que fija el despliegue; si
/// falta (un entorno nuevo, otra región, una Web App recreada) el servidor arranca en UTC y las horas salen desplazadas sin que nada avise. Al
/// arrancar se comprueba: en producción la aplicación no arranca, y en desarrollo y pruebas solo avisa en el registro.</summary>
public static class ServerTimeZone
{
    public const string Expected = "Europe/Madrid";

    /// <summary>Que las dos zonas den el mismo desfase en invierno y en verano. No se compara con HasSameRules porque la misma zona de Madrid se
    /// representa distinto en Windows ("Romance Standard Time") y en Linux, y las reglas internas no coinciden aunque las horas sí.</summary>
    public static bool SameOffsets(TimeZoneInfo actual, TimeZoneInfo expected)
    {
        var winter = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var summer = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        return actual.GetUtcOffset(winter) == expected.GetUtcOffset(winter) && actual.GetUtcOffset(summer) == expected.GetUtcOffset(summer);
    }

    public static void Verify(IHostEnvironment environment, ILogger logger, TimeZoneInfo? actual = null)
    {
        actual ??= TimeZoneInfo.Local;
        if (SameOffsets(actual, TimeZoneInfo.FindSystemTimeZoneById(Expected)))
        {
            return;
        }

        var message = $"La zona horaria del servidor es {actual.Id} y ResidApp espera {Expected}: las horas de citas, comunicados e indicadores " +
                      "saldrían desplazadas. En Azure se fija con el ajuste WEBSITE_TIME_ZONE=Europe/Madrid de la Web App.";
        if (environment.IsProduction())
        {
            throw new InvalidOperationException(message);
        }

        logger.LogWarning("{Message}", message);
    }
}
