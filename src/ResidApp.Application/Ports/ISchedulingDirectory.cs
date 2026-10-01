using ResidApp.Domain.Scheduling;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>ADM-14: un turno del catálogo del centro.</summary>
public sealed record ShiftInfo(Guid ShiftId, string Name, TimeOnly Start, TimeOnly End, bool Active)
{
    public bool CrossesMidnight => Shift.CrossesMidnight(Start, End);
}

/// <summary>ADM-14: un miembro vigente de un equipo. Eligible dice si todavía tiene un perfil de Auxiliar, Enfermería o Medicina
/// con la unidad del equipo concedida (si se le retiró después, sigue como miembro hasta que se le dé de baja).</summary>
public sealed record TeamMemberInfo(AccountId AccountId, string Name, IReadOnlyList<SystemProfile> Profiles, bool Eligible, DateTimeOffset Since);

/// <summary>ADM-14: un equipo de una unidad del ámbito, con sus miembros vigentes.</summary>
public sealed record TeamInfo(Guid TeamId, UnitId UnitId, string UnitName, string Name, bool Active, IReadOnlyList<TeamMemberInfo> Members);

/// <summary>ADM-14: una cuenta que se puede añadir a un equipo.</summary>
public sealed record EligibleTeamMember(AccountId AccountId, string Name, IReadOnlyList<SystemProfile> Profiles);

/// <summary>ADM-14: lectura del catálogo de turnos del centro y de los equipos de las unidades del ámbito de Administración. Solo
/// lee; los equipos de unidades fuera del ámbito no se ven.</summary>
public interface ISchedulingDirectory
{
    Task<IReadOnlyList<ShiftInfo>> ListShiftsAsync(AccountAdministrationAccess access, CancellationToken ct = default);

    Task<IReadOnlyList<TeamInfo>> ListTeamsAsync(AccountAdministrationAccess access, CancellationToken ct = default);

    /// <summary>Las cuentas con un perfil vigente de Auxiliar, Enfermería o Medicina con la unidad del equipo concedida, activas y
    /// que todavía no son miembros vigentes. Vacío si el equipo no es de una unidad del ámbito.</summary>
    Task<IReadOnlyList<EligibleTeamMember>> ListEligibleMembersAsync(AccountAdministrationAccess access, Guid teamId, CancellationToken ct = default);
}

/// <summary>
/// ADM-14 (0027): escrituras sobre turnos, equipos y miembros. Cada una va en una transacción que comprueba de nuevo el ámbito de
/// Administración, bloquea la fila del centro y deja su evento en dbo.eventos_auditoria, sin datos. Los turnos son del centro; los
/// equipos, de una unidad concedida al ámbito. Nada se borra y las horas de un turno no cambian.
/// </summary>
public interface ISchedulingRepository
{
    /// <summary>Crea el turno (con OperationId como id). Reenviar la misma operación devuelve el turno ya creado.</summary>
    Task<Guid> CreateShiftAsync(AccountAdministrationAccess access, Guid operationId, ShiftData data, CancellationToken ct = default);

    Task RenameShiftAsync(AccountAdministrationAccess access, Guid shiftId, string name, CancellationToken ct = default);

    Task ChangeShiftStatusAsync(AccountAdministrationAccess access, Guid shiftId, bool active, CancellationToken ct = default);

    /// <summary>Crea el equipo (con OperationId como id) en una unidad activa del ámbito.</summary>
    Task<Guid> CreateTeamAsync(AccountAdministrationAccess access, Guid operationId, UnitId unitId, string name, CancellationToken ct = default);

    Task RenameTeamAsync(AccountAdministrationAccess access, Guid teamId, string name, CancellationToken ct = default);

    Task ChangeTeamStatusAsync(AccountAdministrationAccess access, Guid teamId, bool active, CancellationToken ct = default);

    Task AddTeamMemberAsync(AccountAdministrationAccess access, Guid teamId, AccountId accountId, CancellationToken ct = default);

    Task RemoveTeamMemberAsync(AccountAdministrationAccess access, Guid teamId, AccountId accountId, CancellationToken ct = default);
}

/// <summary>ADM-14/15: un turno planificado de un equipo en una fecha, de una unidad del ámbito. Justification es la decisión de
/// Administración cuando se avisó de un solapamiento (null si no lo hubo). BatchId es la serie: las fechas guardadas en un mismo envío.</summary>
public sealed record ScheduleEntry(
    Guid ScheduleId, Guid BatchId, UnitId UnitId, string UnitName, Guid TeamId, string TeamName, int TeamMembers, Guid ShiftId, string ShiftName,
    TimeOnly Start, TimeOnly End, DateOnly Date, string? Justification)
{
    public bool CrossesMidnight => ResidApp.Domain.Scheduling.Shift.CrossesMidnight(Start, End);
}

/// <summary>ADM-17: lo que pasaría al planificar: ToCreate son las fechas que se guardarían, AlreadyPlanned las que ya tenían ese equipo
/// y ese turno (se omiten), y Conflicts los solapamientos que Administración tiene que decidir. No escribe nada.</summary>
public sealed record SchedulePreview(
    IReadOnlyList<DateOnly> ToCreate, IReadOnlyList<DateOnly> AlreadyPlanned, IReadOnlyList<ResidApp.Domain.Scheduling.ScheduleConflict> Conflicts);

/// <summary>ADM-15: las fechas guardadas y las omitidas por estar ya planificadas.</summary>
public sealed record PlanOutcome(IReadOnlyList<DateOnly> Created, IReadOnlyList<DateOnly> Skipped);

/// <summary>ADM-16: una fecha activa de una serie.</summary>
public sealed record ScheduleSeriesDate(Guid ScheduleId, DateOnly Date, string? Justification);

/// <summary>ADM-16: una serie (las fechas guardadas en un mismo envío): su equipo, su turno, sus fechas activas y cuántas se han retirado.</summary>
public sealed record ScheduleSeries(
    Guid BatchId, UnitId UnitId, string UnitName, Guid TeamId, string TeamName, Guid ShiftId, string ShiftName, TimeOnly Start, TimeOnly End,
    IReadOnlyList<ScheduleSeriesDate> ActiveDates, int RetiredDates)
{
    public bool CrossesMidnight => ResidApp.Domain.Scheduling.Shift.CrossesMidnight(Start, End);
}

/// <summary>ADM-14/15: lectura de la planificación de las unidades del ámbito entre dos fechas (ambas incluidas).</summary>
public interface ISchedulePlanDirectory
{
    Task<IReadOnlyList<ScheduleEntry>> ListScheduleAsync(
        AccountAdministrationAccess access, DateOnly from, DateOnly to, UnitId? unitId, CancellationToken ct = default);

    /// <summary>ADM-16: la serie de un lote, solo si es de una unidad del ámbito (si no, acceso denegado).</summary>
    Task<ScheduleSeries> FindSeriesAsync(AccountAdministrationAccess access, Guid batchId, CancellationToken ct = default);
}

/// <summary>
/// ADM-15/17 (0027): escrituras sobre la planificación. Planificar no concede acceso a nada. El cálculo de conflictos es del servidor y
/// se repite dentro de la transacción de escritura: con conflictos, solo se guarda con una justificación.
/// </summary>
public interface ISchedulePlanRepository
{
    /// <summary>Calcula lo que pasaría, sin escribir.</summary>
    Task<SchedulePreview> PreviewAsync(
        AccountAdministrationAccess access, Guid teamId, Guid shiftId, IReadOnlyList<DateOnly> dates, CancellationToken ct = default);

    /// <summary>Planifica el equipo en el turno para las fechas (con LotId como token contra el doble envío). Con solapamientos y sin
    /// justificación lanza SCHEDULE_CONFLICTS.</summary>
    Task<PlanOutcome> PlanAsync(
        AccountAdministrationAccess access, Guid batchId, Guid teamId, Guid shiftId, IReadOnlyList<DateOnly> dates, string? justification,
        CancellationToken ct = default);

    /// <summary>Retira una fecha planificada (hoy o futura). Lo retirado no se reactiva ni se borra.</summary>
    Task RetireAsync(AccountAdministrationAccess access, Guid scheduleId, DateOnly today, CancellationToken ct = default);

    /// <summary>ADM-16: retira las fechas activas de la serie desde max(from, today). Un solo evento de auditoría. Sin fechas activas desde
    /// ahí lanza SCHEDULING_CHANGE_CONFLICT. Devuelve cuántas retiró.</summary>
    Task<int> RetireSeriesAsync(AccountAdministrationAccess access, Guid batchId, DateOnly from, DateOnly today, CancellationToken ct = default);
}
