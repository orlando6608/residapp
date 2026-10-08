namespace ResidApp.Domain.Enfermeria;

/// <summary>
/// Cuándo ve la familia una comunicación (CJ, 2026-10-07, continuidad-supervision-comunicacion 5.1 y 5.2). Quien la prepara la
/// aprueba al guardarla. Se deja un margen de 1 hora para corregir errores y después sale a una hora fija del día (19:00) en la
/// zona horaria del servidor: la primera de esas horas que cae a partir del margen. Administración puede publicarla antes.
/// Si la información es urgente este no es el canal: se usan otros canales además.
/// </summary>
public static class FamilyCommunicationSchedule
{
    public static readonly TimeSpan Margin = TimeSpan.FromHours(1);

    public static readonly TimeOnly PublicationTime = new(19, 0);

    /// <summary>El momento en que la comunicación se publica sola: las 19:00 locales siguientes a preparada + 1 h (el mismo día si
    /// aún no han pasado).</summary>
    public static DateTimeOffset ScheduledAt(DateTimeOffset preparedAt, TimeZoneInfo zone)
    {
        var earliest = TimeZoneInfo.ConvertTime(preparedAt + Margin, zone).DateTime;
        var candidate = DateOnly.FromDateTime(earliest).ToDateTime(PublicationTime);
        if (candidate < earliest)
        {
            candidate = candidate.AddDays(1);
        }

        return new DateTimeOffset(candidate, zone.GetUtcOffset(candidate));
    }

    /// <summary>Se puede corregir el texto durante el margen de 1 hora, y mientras Administración no la haya publicado antes.</summary>
    public static bool CanCorrect(DateTimeOffset preparedAt, DateTimeOffset? publishedEarlyAt, DateTimeOffset now) =>
        publishedEarlyAt is null && now < preparedAt + Margin;

    /// <summary>Publicada: llegó su hora o Administración la publicó antes.</summary>
    public static bool IsPublished(DateTimeOffset preparedAt, DateTimeOffset? publishedEarlyAt, DateTimeOffset now, TimeZoneInfo zone) =>
        publishedEarlyAt is not null || now >= ScheduledAt(preparedAt, zone);
}
