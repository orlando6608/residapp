using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;

namespace ResidApp.Web.Models;

/// <summary>Textos de las citas en español (nunca los nombres de los enums en pantalla).</summary>
public static class AppointmentDisplay
{
    public static string Label(AppointmentTeam team) => team switch
    {
        AppointmentTeam.Medico => "Equipo médico",
        AppointmentTeam.Enfermeria => "Equipo de Enfermería",
        _ => team.ToString(),
    };

    public static string Label(AppointmentModality modality) => modality switch
    {
        AppointmentModality.Presencial => "Presencial",
        AppointmentModality.Telefonica => "Telefónica",
        AppointmentModality.Videoconferencia => "Videoconferencia",
        _ => modality.ToString(),
    };

    public static string Label(AppointmentServiceMode mode) => mode switch
    {
        AppointmentServiceMode.Desactivado => "Servicio desactivado",
        AppointmentServiceMode.ReservaDirecta => "Reserva directa",
        AppointmentServiceMode.SolicitudPrevia => "Solicitud previa",
        _ => mode.ToString(),
    };

    public static string Label(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Confirmada => "Confirmada",
        AppointmentStatus.Cancelada => "Cancelada",
        AppointmentStatus.Finalizada => "Finalizada",
        _ => status.ToString(),
    };

    public static string Label(AppointmentChangeAction action) => action switch
    {
        AppointmentChangeAction.Creada => "Reservada",
        AppointmentChangeAction.Reprogramada => "Reprogramada",
        AppointmentChangeAction.Cancelada => "Cancelada",
        AppointmentChangeAction.Finalizada => "Finalizada",
        _ => action.ToString(),
    };

    public static string Day(int isoDayOfWeek) => isoDayOfWeek switch
    {
        1 => "Lunes",
        2 => "Martes",
        3 => "Miércoles",
        4 => "Jueves",
        5 => "Viernes",
        6 => "Sábado",
        7 => "Domingo",
        _ => isoDayOfWeek.ToString(),
    };

    /// <summary>Un instante en la hora del servidor, como «lunes 12/10/2026 10:30».</summary>
    public static string When(DateTimeOffset at)
    {
        var local = TimeZoneInfo.ConvertTime(at, TimeZoneInfo.Local);
        return $"{Day(local.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)local.DayOfWeek).ToLowerInvariant()} {local:dd/MM/yyyy HH:mm}";
    }

    /// <summary>El día de un instante en la hora del servidor, como «lunes 12/10/2026».</summary>
    public static string DayHeading(DateTimeOffset at)
    {
        var local = TimeZoneInfo.ConvertTime(at, TimeZoneInfo.Local);
        return $"{Day(local.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)local.DayOfWeek).ToLowerInvariant()} {local:dd/MM/yyyy}";
    }

    public static string Time(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, TimeZoneInfo.Local).ToString("HH:mm");

    /// <summary>La clave de una modalidad habilitada en el formulario: «MEDICO:PRESENCIAL».</summary>
    public static string Key(AppointmentTeam team, AppointmentModality modality) => $"{team.ToCode()}:{modality.ToCode()}";
}

/// <summary>La configuración general del servicio de citas del centro (ADM-21). Version es la que tenía al abrir la pantalla; Modalidades, las
/// casillas marcadas («EQUIPO:MODALIDAD»).</summary>
public sealed class CitasServicioFormModel
{
    public int Version { get; set; }

    [Required(ErrorMessage = "Elige el modo del servicio.")]
    [Display(Name = "Modo del servicio")]
    public AppointmentServiceMode? Modo { get; set; }

    [Required(ErrorMessage = "Indica la antelación mínima en horas.")]
    [Range(0, AppointmentServiceSettings.MaxNoticeHours, ErrorMessage = "La antelación va de 0 a {2} horas.")]
    [Display(Name = "Antelación mínima (horas)")]
    public int? AntelacionHoras { get; set; }

    [Required(ErrorMessage = "Indica el horizonte máximo en días.")]
    [Range(1, AppointmentServiceSettings.MaxHorizonDays, ErrorMessage = "El horizonte va de 1 a {2} días.")]
    [Display(Name = "Horizonte máximo (días)")]
    public int? HorizonteDias { get; set; }

    public List<string> Modalidades { get; set; } = [];

    public static CitasServicioFormModel From(AppointmentServiceView view) => new()
    {
        Version = view.Version, Modo = view.Settings.Mode, AntelacionHoras = view.Settings.MinimumNoticeHours, HorizonteDias = view.Settings.MaximumHorizonDays,
        Modalidades = view.Modalities.Select(m => AppointmentDisplay.Key(m.Team, m.Modality)).ToList(),
    };

    /// <summary>Las casillas marcadas como modalidades; lo que no sea una clave conocida se ignora.</summary>
    public IReadOnlyCollection<AppointmentModalitySetting> ParsedModalities() => Modalidades
        .Select(key => key.Split(':'))
        .Where(parts => parts.Length == 2 && EnumCode.TryParseCode<AppointmentTeam>(parts[0], out _) && EnumCode.TryParseCode<AppointmentModality>(parts[1], out _))
        .Select(parts => new AppointmentModalitySetting(EnumCode.ParseCode<AppointmentTeam>(parts[0]), EnumCode.ParseCode<AppointmentModality>(parts[1])))
        .Distinct()
        .ToList();
}

/// <summary>Una franja recurrente de un equipo (ADM-22).</summary>
public sealed class CitasFranjaFormModel
{
    [Required(ErrorMessage = "Elige el equipo.")]
    [Display(Name = "Equipo")]
    public AppointmentTeam? Equipo { get; set; }

    [Required(ErrorMessage = "Elige el día de la semana.")]
    [Range(1, 7, ErrorMessage = "Elige el día de la semana.")]
    [Display(Name = "Día de la semana")]
    public int? DiaSemana { get; set; }

    [Required(ErrorMessage = "Indica la hora de inicio.")]
    [Display(Name = "Desde")]
    public TimeOnly? Inicio { get; set; }

    [Required(ErrorMessage = "Indica la hora de fin.")]
    [Display(Name = "Hasta")]
    public TimeOnly? Fin { get; set; }

    [Required(ErrorMessage = "Indica la duración de cada cita en minutos.")]
    [Range(AppointmentWindow.MinDurationMinutes, AppointmentWindow.MaxDurationMinutes, ErrorMessage = "La duración va de {1} a {2} minutos.")]
    [Display(Name = "Duración de cada cita (minutos)")]
    public int? DuracionMinutos { get; set; }
}

/// <summary>Un bloqueo puntual (ADM-22): las horas se escriben en la hora local del centro.</summary>
public sealed class CitasBloqueoFormModel
{
    [Required(ErrorMessage = "Elige el equipo.")]
    [Display(Name = "Equipo")]
    public AppointmentTeam? Equipo { get; set; }

    [Display(Name = "Modalidad")]
    public AppointmentModality? Modalidad { get; set; }

    [Required(ErrorMessage = "Indica cuándo empieza el bloqueo.")]
    [Display(Name = "Desde")]
    public DateTime? Desde { get; set; }

    [Required(ErrorMessage = "Indica cuándo termina el bloqueo.")]
    [Display(Name = "Hasta")]
    public DateTime? Hasta { get; set; }

    [Required(ErrorMessage = "Escribe el motivo del bloqueo.")]
    [StringLength(AppointmentBlock.MaxReasonLength)]
    [Display(Name = "Motivo")]
    public string? Motivo { get; set; }
}

public sealed record CitasViewModel(
    AppointmentServiceView? View, CitasServicioFormModel Servicio, CitasFranjaFormModel Franja, CitasBloqueoFormModel Bloqueo);

/// <summary>La semana de la agenda: del lunes (incluido) al lunes siguiente (excluido), en la hora local del servidor.</summary>
public sealed record AgendaCitasViewModel(
    DateOnly Monday, IReadOnlyList<AdministrationAppointment> Appointments, DateTimeOffset Now);

public sealed class CancelarCitaFormModel
{
    [Required]
    public Guid CitaId { get; set; }

    [Required(ErrorMessage = "Escribe el motivo de la cancelación.")]
    [StringLength(AppointmentReason.MaxLength)]
    [Display(Name = "Motivo")]
    public string? Motivo { get; set; }
}

public sealed class ReprogramarCitaFormModel
{
    [Required]
    public Guid CitaId { get; set; }

    [Required(ErrorMessage = "Elige el nuevo hueco.")]
    [Display(Name = "Nuevo hueco")]
    public DateTimeOffset? NuevoInicio { get; set; }

    [Required(ErrorMessage = "Escribe el motivo del cambio.")]
    [StringLength(AppointmentReason.MaxLength)]
    [Display(Name = "Motivo")]
    public string? Motivo { get; set; }
}

public sealed record CancelarCitaViewModel(AdministrationAppointment Appointment, CancelarCitaFormModel Form);

public sealed record ReprogramarCitaViewModel(
    AdministrationAppointment Appointment, IReadOnlyList<AppointmentSlot> Slots, ReprogramarCitaFormModel Form);

/// <summary>Las pantallas de citas de la familia.</summary>
public sealed record PortalFamiliarCitasViewModel(FamilyAppointmentsHome Home, IReadOnlyList<FamilyAppointmentRequest>? Requests = null);

/// <summary>Elegir equipo y modalidad y ver los huecos libres. Slots es null mientras no se hayan elegido.</summary>
public sealed record PortalFamiliarNuevaCitaViewModel(
    FamilyPortalResident Resident, FamilyAppointmentsHome Home, AppointmentTeam? Team, AppointmentModality? Modality, IReadOnlyList<AppointmentSlot>? Slots,
    FamilyAppointment? Rescheduling = null, string? Warning = null);

/// <summary>Revisar y confirmar una reserva o un cambio de hora. OperationId hace idempotente la doble pulsación.</summary>
public sealed record PortalFamiliarConfirmarCitaViewModel(
    FamilyPortalResident Resident, AppointmentTeam Team, AppointmentModality Modality, AppointmentSlot Slot, Guid OperationId, FamilyAppointment? Rescheduling);

public sealed record PortalFamiliarCitaViewModel(FamilyPortalResident Resident, FamilyAppointment Appointment);
