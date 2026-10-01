using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Ports;
using ResidApp.Domain.Scheduling;

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
