namespace ResidApp.Shared;

/// <summary>
/// La hora del centro: el único sitio donde la aplicación pregunta «¿qué hora es aquí?» y «¿qué día es hoy?». Todo se guarda en UTC; lo que se muestra y lo que cuenta
/// como «hoy» sale de la zona del centro de la petición en curso (estudio: docs/producto/zonas-horarias-por-centro-estudio.md). Se acabó <c>TimeZoneInfo.Local</c>,
/// <c>ToLocalTime()</c>, <c>DateTime.Today</c> y <c>DateTime.Now</c>, que dependen de la zona del servidor: una prueba de arquitectura los prohíbe fuera de esta clase.
/// La zona es de la petición (<see cref="AsyncLocal{T}"/>, como la cultura): quien conozca el centro la fija con <see cref="Use"/>; sin fijar, es la de la península
/// (Europe/Madrid), que es la de todos los centros y la de Plataforma.
/// </summary>
public static class CenterTime
{
    public const string DefaultZoneId = "Europe/Madrid";

    private static readonly TimeZoneInfo Default = TimeZoneInfo.FindSystemTimeZoneById(DefaultZoneId);
    private static readonly AsyncLocal<TimeZoneInfo?> Current = new();

    /// <summary>La zona de la península (Europe/Madrid).</summary>
    public static TimeZoneInfo Madrid => Default;

    /// <summary>La zona del centro de la petición en curso.</summary>
    public static TimeZoneInfo Zone => Current.Value ?? Default;

    /// <summary>Fija la zona hasta que se libere el resultado (<c>using</c>); al liberarlo vuelve la anterior.</summary>
    public static IDisposable Use(TimeZoneInfo zone)
    {
        var previous = Current.Value;
        Current.Value = zone;
        return new Restore(previous);
    }

    /// <summary>Un instante, con la hora del centro.</summary>
    public static DateTimeOffset Local(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Zone);

    /// <summary>Una fecha y hora guardada en UTC (la base de datos devuelve <c>DateTimeKind.Unspecified</c>), con la hora del centro.</summary>
    public static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>La hora de un campo <c>datetime-local</c> (sin zona) como instante, entendida con la hora del centro. Una hora que no existe ese día (el cambio de hora) se
    /// toma con el desfase estándar de la zona.</summary>
    public static DateTimeOffset FromLocal(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, Zone.GetUtcOffset(unspecified));
    }

    /// <summary>La fecha de hoy en el centro.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(Local(DateTimeOffset.UtcNow).DateTime);

    /// <summary>La fecha del centro en un instante dado (para cálculos y pruebas con un reloj fijo).</summary>
    public static DateOnly TodayAt(DateTimeOffset now) => DateOnly.FromDateTime(Local(now).DateTime);

    /// <summary>La fecha y hora de ahora en el centro (sin zona).</summary>
    public static DateTime Now => Local(DateTimeOffset.UtcNow).DateTime;

    private sealed class Restore(TimeZoneInfo? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
