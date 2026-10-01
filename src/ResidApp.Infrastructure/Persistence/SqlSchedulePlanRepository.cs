using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Scheduling;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// ADM-15/17 (0027): planificación de un equipo en un turno para una o varias fechas. Planificar no concede acceso a nada.
/// Las escrituras van en una transacción que repite el ámbito de Administración y bloquea la fila del centro (como el resto de cambios
/// de turnos y equipos), y recalculan los conflictos aunque la pantalla ya los haya enseñado:
///   - mismo equipo en turnos que se solapan, y una persona en dos equipos con turnos que se solapan (SchedulePlan.FindConflicts);
///   - solo se comparan planificaciones de unidades concedidas al ámbito;
///   - con conflictos y sin justificación no se guarda nada (SCHEDULE_CONFLICTS); con justificación, esta queda en las filas cuya fecha
///     tiene un conflicto.
/// Las fechas que ya tenían ese equipo y ese turno se omiten. El lote (LotId) es el token contra el doble envío.
/// </summary>
public sealed class SqlSchedulePlanRepository(SqlConnectionFactory connections) : ISchedulePlanRepository
{
    public async Task<SchedulePreview> PreviewAsync(
        AccountAdministrationAccess access, Guid teamId, Guid shiftId, IReadOnlyList<DateOnly> dates, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var computed = await ComputeAsync(connection, null, access, teamId, shiftId, dates, ct);
        return new SchedulePreview(computed.ToCreate, computed.AlreadyPlanned, computed.Conflicts);
    }

    public async Task<PlanOutcome> PlanAsync(
        AccountAdministrationAccess access, Guid batchId, Guid teamId, Guid shiftId, IReadOnlyList<DateOnly> dates, string? justification,
        CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "SELECT id FROM dbo.centros WITH (UPDLOCK, ROWLOCK) WHERE id = @CenterId",
            new { CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));

        var previous = (await connection.QueryAsync<BatchRow>(new CommandDefinition(
            "SELECT equipo_id AS TeamId, turno_id AS ShiftId, fecha AS Date FROM dbo.planificacion_turnos WHERE lote_id = @BatchId AND centro_id = @CenterId",
            new { BatchId = batchId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct))).ToList();
        if (previous.Count > 0)
        {
            return previous.All(r => r.TeamId == teamId && r.ShiftId == shiftId)
                ? new PlanOutcome(previous.Select(r => DateOnly.FromDateTime(r.Date)).Order().ToList(), [])
                : throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
        }

        var computed = await ComputeAsync(connection, transaction, access, teamId, shiftId, dates, ct);
        if (computed.ToCreate.Count == 0)
        {
            throw new DomainValidationException(SchedulePlan.InvalidCode);
        }

        if (computed.Conflicts.Count > 0 && justification is null)
        {
            throw new DomainValidationException(SchedulePlan.ConflictsCode);
        }

        var conflictDates = computed.Conflicts.Select(c => c.Date).ToHashSet();
        var occurredAt = DateTimeOffset.UtcNow;
        foreach (var date in computed.ToCreate)
        {
            var id = Guid.NewGuid();
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.planificacion_turnos
                    (id, centro_id, unidad_id, equipo_id, turno_id, fecha, lote_id, creado_en, creado_por_cuenta_id, conflicto_justificacion)
                VALUES (@Id, @CenterId, @UnitId, @TeamId, @ShiftId, @Date, @BatchId, @OccurredAt, @ActorId, @Justification)
                """, new
            {
                Id = id, CenterId = access.CenterId.Value, UnitId = computed.UnitId, TeamId = teamId, ShiftId = shiftId,
                Date = date.ToDateTime(TimeOnly.MinValue), BatchId = batchId, OccurredAt = occurredAt, ActorId = access.AccountId.Value,
                Justification = conflictDates.Contains(date) ? justification : null,
            }, transaction, cancellationToken: ct));
            await SqlProfessionalAccountRepository.AuditAsync(
                connection, transaction, access, "SCHEDULE", id, "SCHEDULE_CREATE", occurredAt, computed.UnitId, ct: ct);
        }

        transaction.Commit();
        return new PlanOutcome(computed.ToCreate, computed.AlreadyPlanned);
    }

    public async Task RetireAsync(AccountAdministrationAccess access, Guid scheduleId, DateOnly today, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "SELECT id FROM dbo.centros WITH (UPDLOCK, ROWLOCK) WHERE id = @CenterId",
            new { CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        var row = await connection.QuerySingleOrDefaultAsync<RetireRow>(new CommandDefinition($"""
            SELECT sch.unidad_id AS UnitId, sch.fecha AS Date, CAST(CASE WHEN sch.retirado_en IS NULL THEN 0 ELSE 1 END AS BIT) AS Retired
              FROM dbo.planificacion_turnos sch WITH (UPDLOCK, ROWLOCK)
             WHERE sch.id = @Id AND sch.centro_id = @CenterId AND {SqlSchedulingDirectory.AdministratorUnitGrant("sch.unidad_id")}
            """, new { Id = scheduleId, CenterId = access.CenterId.Value, access.ProfileScopeId }, transaction, cancellationToken: ct))
            ?? throw new AccessDeniedException();
        if (row.Retired)
        {
            throw new DomainValidationException("SCHEDULING_CHANGE_CONFLICT");
        }

        if (DateOnly.FromDateTime(row.Date) < today)
        {
            throw new DomainValidationException(SchedulePlan.InvalidCode);
        }

        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.planificacion_turnos SET retirado_en = @OccurredAt, retirado_por_cuenta_id = @ActorId
             WHERE id = @Id AND centro_id = @CenterId AND retirado_en IS NULL
            """, new { Id = scheduleId, CenterId = access.CenterId.Value, OccurredAt = occurredAt, ActorId = access.AccountId.Value },
            transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "SCHEDULE", scheduleId, "SCHEDULE_RETIRE", occurredAt, row.UnitId, ct: ct);
        transaction.Commit();
    }

    /// <summary>El equipo y el turno tienen que existir, estar activos y el equipo ser de una unidad del ámbito. Calcula qué fechas se
    /// guardarían, cuáles ya estaban y los conflictos con lo planificado en el entorno de esas fechas (un día antes y después, por los
    /// turnos que cruzan la medianoche).</summary>
    private static async Task<Computed> ComputeAsync(
        SqlConnection connection, SqlTransaction? transaction, AccountAdministrationAccess access, Guid teamId, Guid shiftId,
        IReadOnlyList<DateOnly> dates, CancellationToken ct)
    {
        var team = await connection.QuerySingleOrDefaultAsync<TeamRow>(new CommandDefinition($"""
            SELECT team.unidad_id AS UnitId, team.nombre_visible AS Name, team.estado AS StatusCode
              FROM dbo.equipos team
             WHERE team.id = @TeamId AND team.centro_id = @CenterId AND {SqlSchedulingDirectory.AdministratorUnitGrant("team.unidad_id")}
            """, new { TeamId = teamId, CenterId = access.CenterId.Value, access.ProfileScopeId }, transaction, cancellationToken: ct))
            ?? throw new AccessDeniedException();
        var shift = await connection.QuerySingleOrDefaultAsync<ShiftRow>(new CommandDefinition("""
            SELECT nombre_visible AS Name, hora_inicio AS Start, hora_fin AS [End], estado AS StatusCode
              FROM dbo.turnos_catalogo WHERE id = @ShiftId AND centro_id = @CenterId
            """, new { ShiftId = shiftId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct))
            ?? throw new AccessDeniedException();
        if (team.StatusCode != "ACTIVE" || shift.StatusCode != "ACTIVE")
        {
            throw new DomainValidationException(SchedulePlan.InvalidCode);
        }

        var existingRows = (await connection.QueryAsync<ExistingRow>(new CommandDefinition($"""
            SELECT sch.equipo_id AS TeamId, team.nombre_visible AS TeamName, sch.turno_id AS ShiftId, sh.nombre_visible AS ShiftName,
                   sh.hora_inicio AS Start, sh.hora_fin AS [End], sch.fecha AS Date
              FROM dbo.planificacion_turnos sch
              JOIN dbo.equipos team ON team.id = sch.equipo_id
              JOIN dbo.turnos_catalogo sh ON sh.id = sch.turno_id
             WHERE sch.centro_id = @CenterId AND sch.retirado_en IS NULL AND sch.fecha >= @From AND sch.fecha <= @To
               AND {SqlSchedulingDirectory.AdministratorUnitGrant("sch.unidad_id")}
               AND (sch.equipo_id = @TeamId OR EXISTS (
                        SELECT 1 FROM dbo.equipos_miembros mine
                          JOIN dbo.equipos_miembros theirs ON theirs.cuenta_id = mine.cuenta_id AND theirs.revocado_en IS NULL
                         WHERE mine.equipo_id = @TeamId AND mine.revocado_en IS NULL AND theirs.equipo_id = sch.equipo_id))
            """, new
        {
            CenterId = access.CenterId.Value, access.ProfileScopeId, TeamId = teamId,
            From = dates.Min().AddDays(-1).ToDateTime(TimeOnly.MinValue), To = dates.Max().AddDays(1).ToDateTime(TimeOnly.MinValue),
        }, transaction, cancellationToken: ct))).ToList();
        var existing = existingRows.Select(r => new PlannedShift(
            r.TeamId, r.TeamName, r.ShiftId, r.ShiftName, TimeOnly.FromTimeSpan(r.Start), TimeOnly.FromTimeSpan(r.End), DateOnly.FromDateTime(r.Date))).ToList();

        var already = dates.Where(d => existing.Any(e => e.TeamId == teamId && e.ShiftId == shiftId && e.Date == d)).ToList();
        var toCreate = dates.Except(already).ToList();
        var start = TimeOnly.FromTimeSpan(shift.Start);
        var end = TimeOnly.FromTimeSpan(shift.End);
        var proposed = toCreate.Select(d => new PlannedShift(teamId, team.Name, shiftId, shift.Name, start, end, d)).ToList();

        var teamIds = existing.Select(e => e.TeamId).Append(teamId).Distinct().ToList();
        var memberRows = await connection.QueryAsync<MemberRow>(new CommandDefinition("""
            SELECT member.equipo_id AS TeamId, member.cuenta_id AS AccountId, COALESCE(account.nombre_visible, account.sujeto_externo) AS Name
              FROM dbo.equipos_miembros member
              JOIN dbo.cuentas account ON account.id = member.cuenta_id
             WHERE member.revocado_en IS NULL AND member.equipo_id IN @TeamIds
            """, new { TeamIds = teamIds }, transaction, cancellationToken: ct));
        var members = memberRows.GroupBy(m => m.TeamId).ToDictionary(
            g => g.Key, g => (IReadOnlyList<(Guid AccountId, string Name)>)g.Select(m => (m.AccountId, m.Name)).ToList());
        return new Computed(team.UnitId, toCreate, already, SchedulePlan.FindConflicts(proposed, existing, members));
    }

    private sealed record Computed(Guid UnitId, IReadOnlyList<DateOnly> ToCreate, IReadOnlyList<DateOnly> AlreadyPlanned, IReadOnlyList<ScheduleConflict> Conflicts);

    private sealed record TeamRow(Guid UnitId, string Name, string StatusCode);

    private sealed record ShiftRow(string Name, TimeSpan Start, TimeSpan End, string StatusCode);

    private sealed record ExistingRow(Guid TeamId, string TeamName, Guid ShiftId, string ShiftName, TimeSpan Start, TimeSpan End, DateTime Date);

    private sealed record MemberRow(Guid TeamId, Guid AccountId, string Name);

    private sealed record BatchRow(Guid TeamId, Guid ShiftId, DateTime Date);

    private sealed record RetireRow(Guid UnitId, DateTime Date, bool Retired);
}
