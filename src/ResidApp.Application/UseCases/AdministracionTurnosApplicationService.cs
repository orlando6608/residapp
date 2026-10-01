using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Scheduling;
using ResidApp.Domain.Structure;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>ADM-14 (0027): alta de un turno del catálogo del centro. OperacionId nace con el formulario y es el id del turno.</summary>
public sealed record CreateShiftCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid OperacionId, string? Nombre, TimeOnly? Inicio, TimeOnly? Fin);

public sealed record RenameShiftCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid TurnoId, string? Nombre);

/// <summary>ADM-14: Activo es el estado que se quiere (false para inactivar, true para reactivar).</summary>
public sealed record ChangeShiftStatusCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid TurnoId, bool Activo);

/// <summary>ADM-14: alta de un equipo en una unidad del ámbito. OperacionId es el id del equipo.</summary>
public sealed record CreateTeamCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid OperacionId, UnitId UnidadId, string? Nombre);

public sealed record RenameTeamCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid EquipoId, string? Nombre);

public sealed record ChangeTeamStatusCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid EquipoId, bool Activo);

/// <summary>ADM-14: añadir (true) o dar de baja (false) a una cuenta de un equipo.</summary>
public sealed record ChangeTeamMemberCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid EquipoId, AccountId CuentaId, bool Anadir);

/// <summary>ADM-15/17 (0027): planificar un equipo en un turno para varias fechas. LoteId nace con el formulario y es el token contra el
/// doble envío. Justificacion se da cuando el servidor avisó de solapamientos y Administración decide seguir.</summary>
public sealed record PlanShiftCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid LoteId, Guid EquipoId, Guid TurnoId, IReadOnlyList<DateOnly> Fechas, string? Justificacion = null);

public sealed record RetireScheduleCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid PlanificacionId);

/// <summary>ADM-16: la serie de un lote (las fechas guardadas en un mismo envío).</summary>
public sealed record FindScheduleSeriesQuery(Guid AmbitoPerfilId, CenterId CentroId, Guid LoteId);

/// <summary>ADM-16: retirar las fechas activas de una serie desde una fecha (las pasadas no se tocan).</summary>
public sealed record RetireScheduleSeriesCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid LoteId, DateOnly Desde);

/// <summary>ADM-14: la planificación entre dos fechas (ambas incluidas), de todas las unidades del ámbito o de una.</summary>
public sealed record ListScheduleQuery(Guid AmbitoPerfilId, CenterId CentroId, DateOnly Desde, DateOnly Hasta, UnitId? UnidadId = null);

/// <summary>Administración, turnos, equipos y planificación (ADM-14/15/17, script 0027). Cada caso de uso exige un ámbito activo de
/// Administración y el repositorio lo repite dentro de la transacción. Planificar no concede acceso a nada.</summary>
public sealed class AdministracionTurnosApplicationService(
    AdministrationAccessResolver access, ISchedulingDirectory scheduling, ISchedulingRepository schedulingWriter,
    ISchedulePlanDirectory schedulePlan, ISchedulePlanRepository schedulePlanWriter)
{
    public Task<ApplicationResult<IReadOnlyList<ShiftInfo>>> ListShiftsAsync(AdministracionQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await scheduling.ListShiftsAsync(grant, ct);
        });

    public Task<ApplicationResult<IReadOnlyList<TeamInfo>>> ListTeamsAsync(AdministracionQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await scheduling.ListTeamsAsync(grant, ct);
        });

    public Task<ApplicationResult<IReadOnlyList<EligibleTeamMember>>> ListEligibleTeamMembersAsync(
        AdministracionQuery query, Guid teamId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await scheduling.ListEligibleMembersAsync(grant, teamId, ct);
        });

    public Task<ApplicationResult<Guid>> CreateShiftAsync(CreateShiftCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            var data = Shift.Validate(command.Nombre, command.Inicio, command.Fin);
            return await schedulingWriter.CreateShiftAsync(grant, command.OperacionId, data, ct);
        });

    public Task<ApplicationResult<bool>> RenameShiftAsync(RenameShiftCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await schedulingWriter.RenameShiftAsync(grant, command.TurnoId, Shift.ValidateName(command.Nombre), ct);
            return true;
        });

    public Task<ApplicationResult<bool>> ChangeShiftStatusAsync(ChangeShiftStatusCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await schedulingWriter.ChangeShiftStatusAsync(grant, command.TurnoId, command.Activo, ct);
            return true;
        });

    public Task<ApplicationResult<Guid>> CreateTeamAsync(CreateTeamCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            return await schedulingWriter.CreateTeamAsync(grant, command.OperacionId, command.UnidadId, Team.ValidateName(command.Nombre), ct);
        });

    public Task<ApplicationResult<bool>> RenameTeamAsync(RenameTeamCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await schedulingWriter.RenameTeamAsync(grant, command.EquipoId, Team.ValidateName(command.Nombre), ct);
            return true;
        });

    public Task<ApplicationResult<bool>> ChangeTeamStatusAsync(ChangeTeamStatusCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await schedulingWriter.ChangeTeamStatusAsync(grant, command.EquipoId, command.Activo, ct);
            return true;
        });

    public Task<ApplicationResult<bool>> ChangeTeamMemberAsync(ChangeTeamMemberCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await (command.Anadir
                ? schedulingWriter.AddTeamMemberAsync(grant, command.EquipoId, command.CuentaId, ct)
                : schedulingWriter.RemoveTeamMemberAsync(grant, command.EquipoId, command.CuentaId, ct));
            return true;
        });

    public Task<ApplicationResult<IReadOnlyList<ScheduleEntry>>> ListScheduleAsync(ListScheduleQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            if (query.Desde > query.Hasta || query.Hasta.DayNumber - query.Desde.DayNumber + 1 > SchedulePlan.MaxListDays)
            {
                throw new DomainValidationException(SchedulePlan.InvalidCode);
            }

            var (grant, _) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await schedulePlan.ListScheduleAsync(grant, query.Desde, query.Hasta, query.UnidadId, ct);
        });

    /// <summary>ADM-17: lo que pasaría al planificar, sin escribir nada.</summary>
    public Task<ApplicationResult<SchedulePreview>> PreviewScheduleAsync(PlanShiftCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            var dates = SchedulePlan.ValidateDates(command.Fechas, DateOnly.FromDateTime(DateTime.Today));
            return await schedulePlanWriter.PreviewAsync(grant, command.EquipoId, command.TurnoId, dates, ct);
        });

    /// <summary>ADM-15: planifica. Con solapamientos y sin justificación no guarda nada (SCHEDULE_CONFLICTS, un conflicto).</summary>
    public Task<ApplicationResult<PlanOutcome>> PlanShiftAsync(PlanShiftCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            var dates = SchedulePlan.ValidateDates(command.Fechas, DateOnly.FromDateTime(DateTime.Today));
            var justification = SchedulePlan.ValidateJustification(command.Justificacion);
            return await schedulePlanWriter.PlanAsync(grant, command.LoteId, command.EquipoId, command.TurnoId, dates, justification, ct);
        });

    public Task<ApplicationResult<bool>> RetireScheduleAsync(RetireScheduleCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await schedulePlanWriter.RetireAsync(grant, command.PlanificacionId, DateOnly.FromDateTime(DateTime.Today), ct);
            return true;
        });

    public Task<ApplicationResult<ScheduleSeries>> FindScheduleSeriesAsync(FindScheduleSeriesQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await schedulePlan.FindSeriesAsync(grant, query.LoteId, ct);
        });

    /// <summary>ADM-16: devuelve cuántas fechas retiró.</summary>
    public Task<ApplicationResult<int>> RetireScheduleSeriesAsync(RetireScheduleSeriesCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            return await schedulePlanWriter.RetireSeriesAsync(grant, command.LoteId, command.Desde, DateOnly.FromDateTime(DateTime.Today), ct);
        });
}
