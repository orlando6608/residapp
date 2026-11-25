using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Attendance;

// Asistencia diaria del Centro de Día, fase 5a (ADR 0019; script 0115). Las reglas de la forma de los datos están también en la base de datos; aquí viven las que dependen del perfil y de la fecha.

/// <summary>Si la persona vino o no vino ese día. Nada más (sin hora de llegada, sin «tarde»): CJ, 2.1.</summary>
public enum AttendanceState
{
    [Code("ASISTIO")] [Display(Name = "Vino")] Asistio,
    [Code("NO_ASISTIO")] [Display(Name = "No vino")] NoAsistio,
}

/// <summary>Por qué no vino: lista cerrada (CJ, 2.2).</summary>
public enum AttendanceCause
{
    [Code("AVISO_FAMILIA")] [Display(Name = "Avisó la familia")] AvisoFamilia,
    [Code("SIN_AVISO")] [Display(Name = "No avisó")] SinAviso,
    [Code("OTRO")] [Display(Name = "Otro motivo")] Otro,
}

/// <summary>El motivo de una ausencia programada: lista cerrada y sin texto libre. Solo lo leen Enfermería y Medicina (ADR 0019, punto 4).</summary>
public enum AbsenceReason
{
    [Code("INGRESO_HOSPITALARIO")] [Display(Name = "Ingreso hospitalario")] IngresoHospitalario,
    [Code("VACACIONES")] [Display(Name = "Vacaciones")] Vacaciones,
    [Code("OTRO")] [Display(Name = "Otro motivo")] Otro,
}

/// <summary>Lo que se sabe de una persona un día concreto. «Sin anotar» no es «no vino»: es que nadie lo ha anotado.</summary>
public enum AttendanceDayStatus
{
    SinAnotar,
    Vino,
    NoVino,
    AusenciaProgramada,
}

public static class AttendanceRules
{
    public const string InvalidCode = "ATTENDANCE_INVALID";
    public const string DisabledCode = "ATTENDANCE_DISABLED";
    public const string ConflictCode = "ATTENDANCE_REVISION_CONFLICT";
    public const string DateNotAllowedCode = "ATTENDANCE_DATE_NOT_ALLOWED";
    public const string AbsenceInvalidCode = "ATTENDANCE_ABSENCE_INVALID";
    public const string AbsenceOverlapCode = "ATTENDANCE_ABSENCE_OVERLAP";

    /// <summary>Tope de la nota de «otro motivo»: propuesto, sin validar por CJ (ADR 0019). Igual que en la base de datos.</summary>
    public const int MaxNoteLength = 80;

    /// <summary>Hasta cuántos días atrás anotan o corrigen Auxiliar, Enfermería y Medicina; pasado el tope, solo Enfermería o Medicina. Propuesto, sin validar por CJ (ADR 0019).</summary>
    public const int CorrectionDays = 7;

    /// <summary>Duración máxima de una ausencia programada: propuesta, sin validar por CJ (ADR 0019). Igual que en la base de datos.</summary>
    public const int MaxAbsenceDays = 90;

    /// <summary>Días seguidos sin venir desde los que una persona sale en la lista de seguimiento: propuesto, sin validar por CJ (ADR 0019).</summary>
    public const int FollowUpDays = 3;

    /// <summary>Quién anota y corrige la asistencia. Administración no (decisión de Orlando, 2026-11-24).</summary>
    public static bool CanRecord(SystemProfile profile) => profile is SystemProfile.Auxiliar or SystemProfile.Enfermeria or SystemProfile.Medicina;

    /// <summary>Quién lee la asistencia: quien anota y Administración (para su lista de seguimiento). Dirección no.</summary>
    public static bool CanRead(SystemProfile profile) => CanRecord(profile) || profile == SystemProfile.Administracion;

    /// <summary>Quién registra, anula y acorta ausencias programadas, y quién fija la pauta.</summary>
    public static bool CanManageAbsences(SystemProfile profile) => profile is SystemProfile.Administracion or SystemProfile.Enfermeria;

    /// <summary>Quién lee el motivo de una ausencia programada (puede ser un ingreso hospitalario: dato de salud). Administración elige el motivo al registrar, pero no lo vuelve a ver.</summary>
    public static bool CanReadAbsenceReason(SystemProfile profile) => profile is SystemProfile.Enfermeria or SystemProfile.Medicina;

    /// <summary>Quién lee la nota de «otro motivo»: Administración no (ve «Otro motivo»).</summary>
    public static bool CanReadNote(SystemProfile profile) => CanRecord(profile);

    /// <summary>
    /// La forma de lo que se anota: ASISTIO sin causa; NO_ASISTIO con causa; la nota solo con «otro motivo», de una línea y de 80 caracteres como máximo. Devuelve la nota limpia (o nula).
    /// </summary>
    public static string? Validate(AttendanceState state, AttendanceCause? cause, string? note)
    {
        if (!Enum.IsDefined(state) || (cause is { } c && !Enum.IsDefined(c)))
        {
            throw new DomainValidationException(InvalidCode);
        }

        if ((state == AttendanceState.Asistio) != (cause is null))
        {
            throw new DomainValidationException(InvalidCode);
        }

        var clean = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (clean is null)
        {
            return null;
        }

        if (cause != AttendanceCause.Otro || clean.Length > MaxNoteLength || clean.Contains('\n') || clean.Contains('\r'))
        {
            throw new DomainValidationException(InvalidCode);
        }

        return clean;
    }

    /// <summary>
    /// La fecha que se puede anotar: ni futura ni de otro centro (la fecha es la del centro) y, para Auxiliar, hasta <see cref="CorrectionDays"/> días atrás; Enfermería y Medicina, sin ese tope.
    /// </summary>
    public static void ValidateDate(SystemProfile profile, DateOnly date, DateOnly todayAtCenter)
    {
        if (date > todayAtCenter)
        {
            throw new DomainValidationException(DateNotAllowedCode);
        }

        if (profile == SystemProfile.Auxiliar && date < todayAtCenter.AddDays(-CorrectionDays))
        {
            throw new DomainValidationException(DateNotAllowedCode);
        }
    }

    /// <summary>Una ausencia programada: de <paramref name="from"/> a <paramref name="to"/> (ese día incluido), de 90 días como máximo.</summary>
    public static void ValidateAbsence(DateOnly from, DateOnly to, AbsenceReason reason)
    {
        if (to < from || to.DayNumber - from.DayNumber + 1 > MaxAbsenceDays || !Enum.IsDefined(reason))
        {
            throw new DomainValidationException(AbsenceInvalidCode);
        }
    }

    // ---- La pauta: qué días de la semana se espera a la persona (lunes = 1 … domingo = 64) ----

    public const int AllDays = 127;

    public static int Mask(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => 1,
        DayOfWeek.Tuesday => 2,
        DayOfWeek.Wednesday => 4,
        DayOfWeek.Thursday => 8,
        DayOfWeek.Friday => 16,
        DayOfWeek.Saturday => 32,
        _ => 64,
    };

    /// <summary>La máscara de una pauta (de 1 a 127: al menos un día). Si no, ATTENDANCE_INVALID.</summary>
    public static int ValidatePattern(int daysMask) =>
        daysMask is >= 1 and <= AllDays ? daysMask : throw new DomainValidationException(InvalidCode);

    public static bool Expects(int daysMask, DateOnly date) => (daysMask & Mask(date.DayOfWeek)) != 0;

    /// <summary>Lo que se sabe de un día, de más a menos fuerte: lo anotado manda sobre una ausencia programada (la realidad gana a lo previsto).</summary>
    public static AttendanceDayStatus StatusOf(AttendanceState? recorded, bool scheduledAbsence) => recorded switch
    {
        AttendanceState.Asistio => AttendanceDayStatus.Vino,
        AttendanceState.NoAsistio => AttendanceDayStatus.NoVino,
        _ => scheduledAbsence ? AttendanceDayStatus.AusenciaProgramada : AttendanceDayStatus.SinAnotar,
    };
}
