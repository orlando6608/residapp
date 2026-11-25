using ResidApp.Domain.Accounts;
using ResidApp.Domain.Attendance;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

// Asistencia diaria del Centro de Día, fase 5a (ADR 0019; script 0115). El almacén comprueba en cada operación, dentro de su transacción, que el ámbito activo sigue siendo del actor, que el centro
// es un Centro de Día y tiene la función encendida (nunca por la cookie del ámbito) y que el residente está en el alcance de quien actúa. Lo que cada perfil no debe leer (la nota de «otro motivo»
// para Administración; el motivo de una ausencia para todos menos Enfermería y Medicina) lo deja fuera la propia consulta.

/// <summary>Quién actúa: la cuenta, el ámbito y el perfil con el que entró.</summary>
public sealed record AttendanceActor(AccountId AccountId, Guid ProfileScopeId, SystemProfile Profile, CenterId CenterId);

/// <summary>El interruptor del centro. <paramref name="Number"/> es la versión que ve quien cambia (0 si nunca se tocó); <paramref name="DayCenter"/> es false en una Residencia (no se ofrece).</summary>
public sealed record AttendanceCenterState(bool DayCenter, bool Active, int Number);

/// <summary>Lo último anotado de un día. La nota es nula para quien no puede leerla.</summary>
public sealed record AttendanceMark(int Number, AttendanceState State, AttendanceCause? Cause, string? Note);

/// <summary>Una persona en la lista de un día. <paramref name="Expected"/> es verdadero si su pauta incluye ese día o si no tiene pauta todavía.</summary>
public sealed record AttendanceDayEntry(
    ResidentId ResidentId, string ResidentName, Guid? UnitId, string? UnitName, bool HasPattern, bool Expected, AttendanceMark? Mark, bool ScheduledAbsence, AttendanceDayStatus Status);

/// <summary>La pauta vigente de una persona: máscara de días de la semana (lunes = 1 … domingo = 64); 0 si nunca se fijó.</summary>
public sealed record AttendancePatternState(ResidentId ResidentId, string ResidentName, int Number, int DaysMask);

/// <summary>Una ausencia programada vigente o futura (ni anulada ni terminada). El motivo es nulo para quien no puede leerlo.</summary>
public sealed record ScheduledAbsence(Guid Id, ResidentId ResidentId, string ResidentName, DateOnly From, DateOnly To, AbsenceReason? Reason);

/// <summary>Una persona con faltas seguidas. <paramref name="Days"/> son los días esperados seguidos anotados como «no vino» hasta ayer; <paramref name="Capped"/> si hay más de los que se miran. Sin nota ni motivo de ausencia.</summary>
public sealed record FollowUpAbsence(ResidentId ResidentId, string ResidentName, string? UnitName, int Days, bool Capped, DateOnly? LastCame, IReadOnlyList<AttendanceCause> Causes);

/// <summary>Una persona con días recientes sin anotar: no se cuentan como faltas.</summary>
public sealed record FollowUpUnmarked(ResidentId ResidentId, string ResidentName, string? UnitName, int Days);

/// <summary>La lista de seguimiento de Administración: se calcula al abrirla y no se guarda.</summary>
/// <summary>Una persona sin pauta: se la espera todos los días, también el fin de semana, y la lista puede no ser fiable para ella hasta que se le fije.</summary>
public sealed record FollowUpWithoutPattern(ResidentId ResidentId, string ResidentName, string? UnitName);

public sealed record AttendanceFollowUp(IReadOnlyList<FollowUpAbsence> Absences, IReadOnlyList<FollowUpUnmarked> Unmarked, IReadOnlyList<FollowUpWithoutPattern> WithoutPattern);

public interface IAttendanceStore
{
    Task<AttendanceCenterState> ReadCenterAsync(AttendanceActor actor, CancellationToken ct = default);

    /// <summary>Solo Administración. Encender exige las dos confirmaciones. Devuelve el número nuevo; si otra persona cambió antes, conflicto de versión.</summary>
    Task<int> SetCenterAsync(AttendanceActor actor, int expectedNumber, bool enable, bool informed, bool impactAssessment, CancellationToken ct = default);

    /// <summary>Las personas del alcance del actor (activas y no suspendidas) con lo anotado ese día.</summary>
    Task<IReadOnlyList<AttendanceDayEntry>> ListDayAsync(AttendanceActor actor, DateOnly date, CancellationToken ct = default);

    /// <summary>Anota o corrige. Si lo pedido es igual a lo vigente, no hace nada y devuelve su número. Devuelve el número de la versión vigente.</summary>
    Task<int> RecordAsync(
        AttendanceActor actor, ResidentId residentId, DateOnly date, DateOnly todayAtCenter, AttendanceState state, AttendanceCause? cause, string? note, CancellationToken ct = default);

    Task<AttendancePatternState> ReadPatternAsync(AttendanceActor actor, ResidentId residentId, CancellationToken ct = default);

    /// <summary>Administración y Enfermería. Si es igual a la vigente, no hace nada. Devuelve el número vigente.</summary>
    Task<int> SetPatternAsync(AttendanceActor actor, ResidentId residentId, int expectedNumber, int daysMask, CancellationToken ct = default);

    /// <summary>Ausencias vigentes o futuras de una persona (o de todas las del alcance si es nulo), la más próxima primero.</summary>
    Task<IReadOnlyList<ScheduledAbsence>> ListAbsencesAsync(AttendanceActor actor, ResidentId? residentId, DateOnly todayAtCenter, CancellationToken ct = default);

    Task<Guid> RegisterAbsenceAsync(AttendanceActor actor, ResidentId residentId, DateOnly from, DateOnly to, AbsenceReason reason, CancellationToken ct = default);

    Task AnnulAbsenceAsync(AttendanceActor actor, Guid absenceId, CancellationToken ct = default);

    Task ShortenAbsenceAsync(AttendanceActor actor, Guid absenceId, DateOnly newTo, CancellationToken ct = default);

    /// <summary>Solo Administración. Quién lleva faltas seguidas hasta ayer y quién tiene días sin anotar. Registra que se abrió, sin cifras ni nombres.</summary>
    Task<AttendanceFollowUp> ListFollowUpAsync(AttendanceActor actor, DateOnly todayAtCenter, CancellationToken ct = default);
}
