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
