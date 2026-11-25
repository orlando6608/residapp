using ResidApp.Application.Ports;
using ResidApp.Domain.Attendance;
using ResidApp.Shared;

namespace ResidApp.Web.Models;

/// <summary>La lista de un día. <paramref name="Error"/> es el motivo por el que no hay lista (función apagada, sin acceso).</summary>
public sealed record AsistenciaDiaViewModel(DateOnly Day, DateOnly Today, IReadOnlyList<AttendanceDayEntry> Entries, SystemProfile Profile, string? Error, bool Active = true)
{
    public bool CanRecord => Active && AttendanceRules.CanRecord(Profile);
    public bool CanManage => AttendanceRules.CanManageAbsences(Profile);
    public bool CanSeeNote => AttendanceRules.CanReadNote(Profile);
}

public sealed record AsistenciaPautaViewModel(AttendancePatternState Pattern, bool CanManage, DateOnly Today, SystemProfile Profile)
{
    /// <summary>El Auxiliar ve la pauta en vigor y los próximos cambios, no todo el historial.</summary>
    public bool CanSeeHistory => Profile != SystemProfile.Auxiliar;
}

/// <summary>Las ausencias programadas y, para quien las registra, las personas entre las que elegir.</summary>
public sealed record AsistenciaAusenciasViewModel(
    IReadOnlyList<ScheduledAbsence> Absences, IReadOnlyList<AttendanceDayEntry> Residents, bool CanManage, bool CanSeeReason, DateOnly Today, Guid? ResidentFilter);

public sealed record AsistenciaSeguimientoViewModel(AttendanceFollowUp Data, DateOnly Today);

public sealed record AsistenciaAjustesViewModel(AttendanceCenterState State, string? Error);

/// <summary>Los textos en español de lo que la asistencia muestra: un solo sitio para todas las vistas.</summary>
public static class AsistenciaDisplay
{
    public static string Status(AttendanceDayStatus status) => status switch
    {
        AttendanceDayStatus.Vino => "Vino",
        AttendanceDayStatus.NoVino => "No vino",
        AttendanceDayStatus.AusenciaProgramada => "Ausencia programada",
        _ => "Sin anotar",
    };

    /// <summary>El color va siempre junto al texto; ninguno depende solo del color (WCAG).</summary>
    public static string StatusBadge(AttendanceDayStatus status) => status switch
    {
        AttendanceDayStatus.Vino => "text-bg-success",
        AttendanceDayStatus.NoVino => "text-bg-danger",
        AttendanceDayStatus.AusenciaProgramada => "text-bg-info",
        _ => "text-bg-secondary",
    };

    private static readonly DayOfWeek[] Week =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    /// <summary>De lunes a domingo, como se lee una semana en España.</summary>
    public static IReadOnlyList<DayOfWeek> WeekDays => Week;

    public static string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Lunes",
        DayOfWeek.Tuesday => "Martes",
        DayOfWeek.Wednesday => "Miércoles",
        DayOfWeek.Thursday => "Jueves",
        DayOfWeek.Friday => "Viernes",
        DayOfWeek.Saturday => "Sábado",
        _ => "Domingo",
    };

    /// <summary>«lunes, miércoles y viernes» a partir de la máscara; vacío si no hay pauta.</summary>
    public static string PatternText(int mask)
    {
        var names = Week.Where(d => (mask & AttendanceRules.Mask(d)) != 0).Select(d => DayName(d).ToLowerInvariant()).ToList();
        return names.Count switch
        {
            0 => string.Empty,
            1 => names[0],
            _ => string.Join(", ", names.Take(names.Count - 1)) + " y " + names[^1],
        };
    }

    /// <summary>«3 de marzo» (con el año si no es el de hoy): para el historial de la pauta.</summary>
    public static string LongDate(DateOnly day, DateOnly today) =>
        day.ToString(day.Year == today.Year ? "d 'de' MMMM" : "d 'de' MMMM 'de' yyyy", System.Globalization.CultureInfo.GetCultureInfo("es-ES"));

    public static string Date(DateOnly day) => day.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.GetCultureInfo("es-ES"));
}
