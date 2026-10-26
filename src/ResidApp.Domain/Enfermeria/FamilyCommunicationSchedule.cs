using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Enfermeria;

/// <summary>Cada cuánto recibe la familia el resumen de los comunicados ya publicados (CJ, tercera 1.2: una frecuencia para todo el centro; las
/// dos opciones y «semanal» de partida las decidió Orlando el 2026-10-26).</summary>
public enum FamilyCommunicationSummaryFrequency
{
    [Code("SEMANAL")] [Display(Name = "Semanal")] Semanal,
    [Code("DIARIA")] [Display(Name = "Diaria")] Diaria,
}

/// <summary>
/// La hora de publicación de los comunicados y el margen de un centro (CJ, 2026-10-10; confirmaciones-segunda-tanda-cj 5.1): cada centro puede
/// elegirlos. Los valores de CJ (19:00 y 1 hora) son los de partida. El margen es el tiempo mínimo que pasa entre guardar el comunicado y
/// publicarlo: si a la hora de hoy ya no queda ese margen, sale a la de mañana. La frecuencia del resumen (por defecto semanal) va en el mismo
/// horario: el resumen se corta a la hora de publicación.
/// </summary>
public sealed record FamilyCommunicationScheduleSettings(
    TimeOnly PublicationTime, TimeSpan Margin, FamilyCommunicationSummaryFrequency SummaryFrequency = FamilyCommunicationSummaryFrequency.Semanal)
{
    public const int MaxMarginMinutes = 1440;

    public const string InvalidCode = "FAMILY_COMMUNICATION_SCHEDULE_INVALID";

    public static FamilyCommunicationScheduleSettings Default { get; } = new(new TimeOnly(19, 0), TimeSpan.FromHours(1));

    public int MarginMinutes => (int)Margin.TotalMinutes;

    /// <summary>La hora (con minutos, sin segundos) y el margen en minutos, de 0 a un día; si no, FAMILY_COMMUNICATION_SCHEDULE_INVALID.</summary>
    public static FamilyCommunicationScheduleSettings Validate(
        TimeOnly? publicationTime, int? marginMinutes,
        FamilyCommunicationSummaryFrequency summaryFrequency = FamilyCommunicationSummaryFrequency.Semanal) =>
        publicationTime is { } time && marginMinutes is >= 0 and <= MaxMarginMinutes && Enum.IsDefined(summaryFrequency)
            ? new FamilyCommunicationScheduleSettings(
                new TimeOnly(time.Hour, time.Minute), TimeSpan.FromMinutes(marginMinutes.Value), summaryFrequency)
            : throw new DomainValidationException(InvalidCode);
}

/// <summary>
/// Cuándo ve la familia una comunicación (CJ, 2026-10-07, continuidad-supervision-comunicacion 5.1 y 5.2). Quien la prepara la
/// aprueba al guardarla. Se deja un margen y después sale a una hora fija del día (por defecto las 19:00) en la zona horaria del servidor:
/// la primera de esas horas que cae a partir del margen. Hasta que se publica se puede corregir (CJ, 2026-10-10). Administración y Dirección
/// Clínica pueden publicarla antes. La hora se fija al guardar el comunicado, con el horario que tenga entonces el centro. Si la información
/// es urgente este no es el canal: se usan otros canales además.
/// </summary>
public static class FamilyCommunicationSchedule
{
    public static readonly TimeSpan Margin = FamilyCommunicationScheduleSettings.Default.Margin;

    public static readonly TimeOnly PublicationTime = FamilyCommunicationScheduleSettings.Default.PublicationTime;

    /// <summary>El momento en que la comunicación se publica sola con el horario de CJ: las 19:00 locales siguientes a preparada + 1 h (el mismo
    /// día si aún no han pasado). Es lo que vale para los comunicados guardados antes de que cada centro tuviera su horario.</summary>
    public static DateTimeOffset ScheduledAt(DateTimeOffset preparedAt, TimeZoneInfo zone) =>
        ScheduledAt(preparedAt, FamilyCommunicationScheduleSettings.Default, zone);

    /// <summary>El momento en que se publica sola una comunicación preparada en <paramref name="preparedAt"/> con el horario del centro.</summary>
    public static DateTimeOffset ScheduledAt(DateTimeOffset preparedAt, FamilyCommunicationScheduleSettings settings, TimeZoneInfo zone)
    {
        var earliest = TimeZoneInfo.ConvertTime(preparedAt + settings.Margin, zone).DateTime;
        var candidate = DateOnly.FromDateTime(earliest).ToDateTime(settings.PublicationTime);
        if (candidate < earliest)
        {
            candidate = candidate.AddDays(1);
        }

        return new DateTimeOffset(candidate, zone.GetUtcOffset(candidate));
    }

    /// <summary>Se puede corregir el texto hasta que se publique: mientras no llegue su hora ni Administración o Dirección la hayan publicado
    /// antes (CJ, 2026-10-10).</summary>
    public static bool CanCorrect(DateTimeOffset scheduledAt, DateTimeOffset? publishedEarlyAt, DateTimeOffset now) =>
        publishedEarlyAt is null && now < scheduledAt;

    /// <summary>Publicada: llegó su hora o se publicó antes.</summary>
    public static bool IsPublished(DateTimeOffset scheduledAt, DateTimeOffset? publishedEarlyAt, DateTimeOffset now) =>
        publishedEarlyAt is not null || now >= scheduledAt;

    /// <summary>
    /// El último periodo cerrado del resumen para la familia: el que termina en el último corte (la hora de publicación del centro) que ya pasó
    /// y empieza en el corte anterior. Diaria: de la hora de ayer a la de hoy. Semanal: el corte es el domingo a esa hora, de domingo a domingo.
    /// El periodo no deja huecos con el siguiente: un comunicado publicado justo en el corte entra en el periodo que cierra ese corte (empieza
    /// después de su inicio y termina en su fin, incluido).
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End) LastClosedSummaryPeriod(
        FamilyCommunicationSummaryFrequency frequency, TimeOnly cut, TimeZoneInfo zone, DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        var end = local.Date + cut.ToTimeSpan();
        if (end > local)
        {
            end = end.AddDays(-1);
        }

        if (frequency == FamilyCommunicationSummaryFrequency.Semanal)
        {
            end = end.AddDays(-(int)end.DayOfWeek);
        }

        var start = end.AddDays(frequency == FamilyCommunicationSummaryFrequency.Semanal ? -7 : -1);
        return (new DateTimeOffset(start, zone.GetUtcOffset(start)), new DateTimeOffset(end, zone.GetUtcOffset(end)));
    }
}
