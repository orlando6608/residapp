using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Appointments;

/// <summary>Con quién es la cita (CIT-04): el equipo médico o el de Enfermería, nunca un profesional concreto.</summary>
public enum AppointmentTeam
{
    [Code("MEDICO")] [Display(Name = "Equipo médico")] Medico,
    [Code("ENFERMERIA")] [Display(Name = "Equipo de Enfermería")] Enfermeria,
}

/// <summary>Cómo es la cita (CIT-04).</summary>
public enum AppointmentModality
{
    [Code("PRESENCIAL")] [Display(Name = "Presencial")] Presencial,
    [Code("TELEFONICA")] [Display(Name = "Telefónica")] Telefonica,
    [Code("VIDEOCONFERENCIA")] [Display(Name = "Videoconferencia")] Videoconferencia,
}

/// <summary>El servicio de citas del centro (CIT-01): desactivado o con una única modalidad vigente. La solicitud previa existe en el modelo, pero
/// todavía no se puede elegir.</summary>
public enum AppointmentServiceMode
{
    [Code("DESACTIVADO")] [Display(Name = "Desactivado")] Desactivado,
    [Code("RESERVA_DIRECTA")] [Display(Name = "Reserva directa")] ReservaDirecta,
    [Code("SOLICITUD_PREVIA")] [Display(Name = "Solicitud previa")] SolicitudPrevia,
}

public enum AppointmentStatus
{
    [Code("CONFIRMADA")] [Display(Name = "Confirmada")] Confirmada,
    [Code("CANCELADA")] [Display(Name = "Cancelada")] Cancelada,
    [Code("FINALIZADA")] [Display(Name = "Finalizada")] Finalizada,
}

/// <summary>Qué le pasó a una cita (CIT-C03).</summary>
public enum AppointmentChangeAction
{
    [Code("CREADA")] [Display(Name = "Reservada")] Creada,
    [Code("REPROGRAMADA")] [Display(Name = "Reprogramada")] Reprogramada,
    [Code("CANCELADA")] [Display(Name = "Cancelada")] Cancelada,
    [Code("FINALIZADA")] [Display(Name = "Finalizada")] Finalizada,
}

/// <summary>
/// La configuración general del servicio de citas de un centro (CIT-01, CIT-C01; suposición a confirmar con CJ: 24 horas de antelación y 30 días de
/// horizonte de partida). Sin configuración el servicio está desactivado. La solicitud previa todavía no se admite.
/// </summary>
public sealed record AppointmentServiceSettings(AppointmentServiceMode Mode, int MinimumNoticeHours, int MaximumHorizonDays)
{
    public const int MaxNoticeHours = 720;
    public const int MaxHorizonDays = 180;
    public const string InvalidCode = "APPOINTMENT_SERVICE_INVALID";

    public static AppointmentServiceSettings Default { get; } = new(AppointmentServiceMode.Desactivado, 24, 30);

    public bool BookingEnabled => Mode == AppointmentServiceMode.ReservaDirecta;

    /// <summary>La configuración con sus límites; si falta un valor, se sale de rango o se pide la solicitud previa (todavía sin flujo), APPOINTMENT_SERVICE_INVALID.</summary>
    public static AppointmentServiceSettings Validate(AppointmentServiceMode? mode, int? minimumNoticeHours, int? maximumHorizonDays) =>
        mode is AppointmentServiceMode.Desactivado or AppointmentServiceMode.ReservaDirecta
        && minimumNoticeHours is >= 0 and <= MaxNoticeHours
        && maximumHorizonDays is >= 1 and <= MaxHorizonDays
            ? new AppointmentServiceSettings(mode.Value, minimumNoticeHours.Value, maximumHorizonDays.Value)
            : throw new DomainValidationException(InvalidCode);
}

/// <summary>Una franja recurrente de un equipo (CIT-C01): los días de la semana (1 lunes … 7 domingo, como ISO 8601), de hora a hora locales, con
/// citas de una duración fija. Dos franjas del mismo equipo y día no se solapan.</summary>
public sealed record AppointmentWindow(Guid Id, AppointmentTeam Team, int IsoDayOfWeek, TimeOnly Start, TimeOnly End, int DurationMinutes)
{
    public const int MinDurationMinutes = 5;
    public const int MaxDurationMinutes = 240;
    public const string InvalidCode = "APPOINTMENT_WINDOW_INVALID";
    public const string OverlapCode = "APPOINTMENT_WINDOW_OVERLAP";

    /// <summary>Validada: día de 1 a 7, horas sin segundos con el fin después del inicio, duración de 5 a 240 minutos y que quepa al menos una cita.</summary>
    public static AppointmentWindow Validate(Guid id, AppointmentTeam? team, int? isoDayOfWeek, TimeOnly? start, TimeOnly? end, int? durationMinutes)
    {
        if (team is null || isoDayOfWeek is not (>= 1 and <= 7) || start is null || end is null
            || durationMinutes is not (>= MinDurationMinutes and <= MaxDurationMinutes))
        {
            throw new DomainValidationException(InvalidCode);
        }

        var from = new TimeOnly(start.Value.Hour, start.Value.Minute);
        var to = new TimeOnly(end.Value.Hour, end.Value.Minute);
        if (to <= from || (to - from).TotalMinutes < durationMinutes.Value)
        {
            throw new DomainValidationException(InvalidCode);
        }

        return new AppointmentWindow(id, team.Value, isoDayOfWeek.Value, from, to, durationMinutes.Value);
    }

    public bool Overlaps(AppointmentWindow other) =>
        Team == other.Team && IsoDayOfWeek == other.IsoDayOfWeek && Start < other.End && other.Start < End;
}

/// <summary>Un bloqueo puntual de un equipo, de todas sus modalidades o de una sola, entre dos instantes (CIT-C01).</summary>
public sealed record AppointmentBlock(Guid Id, AppointmentTeam Team, AppointmentModality? Modality, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Reason)
{
    public const int MaxReasonLength = 200;
    public const string InvalidCode = "APPOINTMENT_BLOCK_INVALID";

    public static AppointmentBlock Validate(
        Guid id, AppointmentTeam? team, AppointmentModality? modality, DateTimeOffset? startsAt, DateTimeOffset? endsAt, string? reason) =>
        team is not null && startsAt is not null && endsAt is not null && endsAt > startsAt
        && reason?.Trim() is { Length: > 0 and <= MaxReasonLength } trimmed
            ? new AppointmentBlock(id, team.Value, modality, startsAt.Value, endsAt.Value, trimmed)
            : throw new DomainValidationException(InvalidCode);
}

/// <summary>Un hueco: de cuándo a cuándo, en UTC.</summary>
public sealed record AppointmentSlot(DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>
/// Los huecos que la familia puede reservar (CIT-D01). No se guardan: salen de las franjas del equipo, entre la antelación mínima y el horizonte
/// máximo, sin los bloqueados (del equipo y de la modalidad, o de todas) y sin los que se solapan con una cita no cancelada del equipo. Las horas de
/// las franjas son locales; los cambios de hora no producen huecos en una hora que no existe.
/// </summary>
public static class AppointmentSlots
{
    public static IReadOnlyList<AppointmentSlot> Generate(
        IEnumerable<AppointmentWindow> windows, IEnumerable<AppointmentBlock> blocks, IEnumerable<AppointmentSlot> taken,
        AppointmentTeam team, AppointmentModality modality, AppointmentServiceSettings settings, DateTimeOffset now, TimeZoneInfo zone)
    {
        var earliest = now.AddHours(settings.MinimumNoticeHours);
        var latest = now.AddDays(settings.MaximumHorizonDays);
        var teamWindows = windows.Where(w => w.Team == team).ToList();
        var teamBlocks = blocks.Where(b => b.Team == team && (b.Modality is null || b.Modality == modality)).ToList();
        var occupied = taken.ToList();
        var slots = new List<AppointmentSlot>();
        var firstDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(earliest, zone).DateTime);
        var lastDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(latest, zone).DateTime);
        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            var isoDay = day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek;
            foreach (var window in teamWindows.Where(w => w.IsoDayOfWeek == isoDay))
            {
                for (var time = window.Start; time.AddMinutes(window.DurationMinutes) <= window.End && time.AddMinutes(window.DurationMinutes) > time;
                     time = time.AddMinutes(window.DurationMinutes))
                {
                    var local = day.ToDateTime(time);
                    if (zone.IsInvalidTime(local))
                    {
                        continue;
                    }

                    var start = new DateTimeOffset(local, zone.GetUtcOffset(local));
                    var slot = new AppointmentSlot(start, start.AddMinutes(window.DurationMinutes));
                    if (start >= earliest && start <= latest
                        && !teamBlocks.Any(b => Overlaps(slot, b.StartsAt, b.EndsAt))
                        && !occupied.Any(o => Overlaps(slot, o.StartsAt, o.EndsAt)))
                    {
                        slots.Add(slot);
                    }
                }
            }
        }

        return slots.OrderBy(s => s.StartsAt).ToList();
    }

    public static bool Overlaps(AppointmentSlot slot, DateTimeOffset startsAt, DateTimeOffset endsAt) => slot.StartsAt < endsAt && startsAt < slot.EndsAt;
}

/// <summary>Reglas de cuándo puede cambiar una cita la familia (suposición a confirmar con CJ): reprogramar o cancelar hasta la antelación mínima del
/// centro; después, por teléfono. Administración no tiene este límite.</summary>
public static class AppointmentRules
{
    public static bool CanFamilyChange(DateTimeOffset startsAt, DateTimeOffset now, AppointmentServiceSettings settings) =>
        startsAt - now >= TimeSpan.FromHours(settings.MinimumNoticeHours);
}

/// <summary>El motivo que Administración da al cancelar o reprogramar una cita (ADM-24): obligatorio, de hasta 500 caracteres. Es interno: la
/// familia solo ve que el centro la cambió.</summary>
public static class AppointmentReason
{
    public const int MaxLength = 500;
    public const string InvalidCode = "APPOINTMENT_REASON_INVALID";

    public static string Normalize(string? reason) =>
        reason?.Trim() is { Length: > 0 and <= MaxLength } trimmed ? trimmed : throw new DomainValidationException(InvalidCode);
}

/// <summary>Lo que la familia pide al ver huecos o reservar (CIT-04): un equipo y una modalidad, y para reservar también el hueco y la operación que
/// hace idempotente la doble pulsación. Nunca un profesional.</summary>
public sealed record AppointmentSlotRequest(AppointmentTeam Team, AppointmentModality Modality, DateTimeOffset? StartsAt)
{
    public const string InvalidCode = "APPOINTMENT_REQUEST_INVALID";

    public static AppointmentSlotRequest Validate(AppointmentTeam? team, AppointmentModality? modality) =>
        team is not null && modality is not null ? new AppointmentSlotRequest(team.Value, modality.Value, null) : throw new DomainValidationException(InvalidCode);

    public static AppointmentSlotRequest ValidateBooking(AppointmentTeam? team, AppointmentModality? modality, DateTimeOffset? startsAt, Guid operationId) =>
        team is not null && modality is not null && startsAt is not null && operationId != Guid.Empty
            ? new AppointmentSlotRequest(team.Value, modality.Value, startsAt)
            : throw new DomainValidationException(InvalidCode);
}
