using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Ports;
using ResidApp.Domain.Scheduling;
using ResidApp.Shared;

namespace ResidApp.Web.Models;

/// <summary>ADM-14 (0027): alta de un turno del catálogo. OperacionId es el id del turno, así que reenviar no lo duplica. Si la hora
/// de fin es igual o anterior a la de inicio, el turno cruza la medianoche.</summary>
public sealed class NewShiftFormModel
{
    public Guid OperacionId { get; set; }

    [Required(ErrorMessage = "Escribe el nombre del turno.")]
    [StringLength(Shift.MaxNameLength, ErrorMessage = "El nombre no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre")]
    public string? Nombre { get; set; }

    [Required(ErrorMessage = "Indica la hora de inicio.")]
    [Display(Name = "Hora de inicio")]
    public TimeOnly? Inicio { get; set; }

    [Required(ErrorMessage = "Indica la hora de fin.")]
    [Display(Name = "Hora de fin")]
    public TimeOnly? Fin { get; set; }
}

public sealed record NewShiftViewModel(NewShiftFormModel Form);

public sealed class RenameShiftFormModel
{
    public Guid TurnoId { get; set; }

    [Required(ErrorMessage = "Escribe el nombre del turno.")]
    [StringLength(Shift.MaxNameLength, ErrorMessage = "El nombre no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre")]
    public string? Nombre { get; set; }
}

public sealed record RenameShiftViewModel(ShiftInfo Shift, RenameShiftFormModel Form);

/// <summary>ADM-14: alta de un equipo en una de las unidades del ámbito.</summary>
public sealed class NewTeamFormModel
{
    public Guid OperacionId { get; set; }

    [Required(ErrorMessage = "Elige la unidad.")]
    [Display(Name = "Unidad")]
    public Guid? UnidadId { get; set; }

    [Required(ErrorMessage = "Escribe el nombre del equipo.")]
    [StringLength(Team.MaxNameLength, ErrorMessage = "El nombre no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre")]
    public string? Nombre { get; set; }
}

public sealed record NewTeamViewModel(NewTeamFormModel Form, IReadOnlyList<ScopeUnit> Units);

public sealed class RenameTeamFormModel
{
    public Guid EquipoId { get; set; }

    [Required(ErrorMessage = "Escribe el nombre del equipo.")]
    [StringLength(Team.MaxNameLength, ErrorMessage = "El nombre no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre")]
    public string? Nombre { get; set; }
}

public sealed record RenameTeamViewModel(TeamInfo Team, RenameTeamFormModel Form);

/// <summary>ADM-14: Eligible son las cuentas que se pueden añadir ahora al equipo.</summary>
public sealed record TeamMembersViewModel(TeamInfo Team, IReadOnlyList<EligibleTeamMember> Eligible);

public static class SchedulingDisplay
{
    /// <summary>«08:00 – 15:00», o «22:00 – 06:00 (día siguiente)» si cruza la medianoche.</summary>
    public static string Hours(ShiftInfo shift) =>
        $"{shift.Start:HH\\:mm} – {shift.End:HH\\:mm}" + (shift.CrossesMidnight ? " (termina al día siguiente)" : "");

    public static string Profiles(IReadOnlyList<ResidApp.Shared.SystemProfile> profiles) =>
        string.Join(", ", profiles.Select(SystemProfileDisplay.Label));
}

/// <summary>ADM-15/17: planificar un equipo en un turno para las fechas de un rango, en los días de la semana elegidos. OperacionId es
/// el token contra el doble envío. Justificacion y ConflictosVistos solo se usan cuando el servidor avisó de solapamientos:
/// ConflictosVistos es la huella de los que se enseñaron, para no confirmar unos distintos de los que se vieron.</summary>
public sealed class PlanFormModel
{
    public Guid OperacionId { get; set; }

    [Required(ErrorMessage = "Elige el equipo.")]
    [Display(Name = "Equipo")]
    public Guid? EquipoId { get; set; }

    [Required(ErrorMessage = "Elige el turno.")]
    [Display(Name = "Turno")]
    public Guid? TurnoId { get; set; }

    [Required(ErrorMessage = "Indica la primera fecha.")]
    [Display(Name = "Desde")]
    public DateOnly? Desde { get; set; }

    [Required(ErrorMessage = "Indica la última fecha.")]
    [Display(Name = "Hasta")]
    public DateOnly? Hasta { get; set; }

    [Display(Name = "Días de la semana")]
    public List<DayOfWeek> Dias { get; set; } = [];

    [StringLength(SchedulePlan.MaxJustificationLength, ErrorMessage = "La justificación no puede pasar de {1} caracteres.")]
    [Display(Name = "Justificación")]
    public string? Justificacion { get; set; }

    public string? ConflictosVistos { get; set; }

    /// <summary>Las fechas del rango que caen en los días elegidos (nunca más de un año, para no recorrer rangos absurdos).</summary>
    public IReadOnlyList<DateOnly> Dates()
    {
        if (Desde is not { } from || Hasta is not { } to || to < from)
        {
            return [];
        }

        var last = to > from.AddDays(SchedulePlan.MaxHorizonDays) ? from.AddDays(SchedulePlan.MaxHorizonDays) : to;
        var dates = new List<DateOnly>();
        for (var day = from; day <= last; day = day.AddDays(1))
        {
            if (Dias.Contains(day.DayOfWeek))
            {
                dates.Add(day);
            }
        }

        return dates;
    }
}

/// <summary>ADM-15/17: Preview es null hasta que se calcula; con Conflicts, la pantalla pide la justificación.</summary>
public sealed record PlanViewModel(
    PlanFormModel Form, IReadOnlyList<TeamInfo> Teams, IReadOnlyList<ShiftInfo> Shifts, SchedulePreview? Preview);

/// <summary>ADM-14: la planificación de dos semanas desde From, de todas las unidades del ámbito o de una.</summary>
public sealed record ScheduleViewModel(
    DateOnly From, DateOnly To, UnitId? Unit, IReadOnlyList<ScheduleEntry> Entries, IReadOnlyList<ScopeUnit> Units);

public static class ScheduleConflictDisplay
{
    /// <summary>Huella de los solapamientos enseñados.</summary>
    public static string Fingerprint(IEnumerable<ScheduleConflict> conflicts)
    {
        var text = string.Join("\n", conflicts.Select(c =>
            $"{c.Kind}|{c.Date:yyyy-MM-dd}|{c.TeamName}|{c.ShiftName}|{c.OtherTeamName}|{c.OtherShiftName}|{c.PersonName}"));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    }

    public static string Describe(ScheduleConflict conflict) => conflict.Kind == ScheduleConflictKind.SameTeam
        ? $"«{conflict.TeamName}» ya está planificado en «{conflict.OtherShiftName}», que se solapa con «{conflict.ShiftName}»."
        : $"{conflict.PersonName} está en «{conflict.TeamName}» y en «{conflict.OtherTeamName}», y sus turnos se solapan: «{conflict.ShiftName}» y «{conflict.OtherShiftName}».";

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

    public static readonly DayOfWeek[] WeekOrder =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];
}
