using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Attendance;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// La asistencia diaria del Centro de Día (script 0115, ADR 0019). Cada escritura va en una transacción que vuelve a comprobar el ámbito activo, que el centro es un Centro de Día con la función
/// encendida (leído de la base de datos, nunca de la cookie del ámbito) y que el residente está en el alcance de quien actúa; las tablas son de solo añadir, así que corregir es insertar la versión
/// siguiente. La auditoría lleva solo identificadores: nunca el estado, la causa, la nota, el motivo, las fechas ni la pauta. Lo que un perfil no debe leer (la nota para Administración; el motivo
/// de una ausencia para todos menos Enfermería y Medicina) no sale de la consulta.
/// </summary>
public sealed class SqlAttendanceRepository(SqlConnectionFactory connections) : IAttendanceStore
{
    private const string ResourceType = "ATTENDANCE";

    /// <summary>El ámbito activo es de este actor y este centro con el perfil indicado.</summary>
    private const string ActorScopeSql = """
        SELECT 1 FROM dbo.ambitos_perfil scope_profile
         WHERE scope_profile.id = @ProfileScopeId AND scope_profile.centro_id = @CenterId AND scope_profile.cuenta_id = @AccountId
           AND scope_profile.perfil_codigo = @Profile AND scope_profile.estado = 'ACTIVE' AND scope_profile.revocado_en IS NULL
        """;

    /// <summary>El residente {R} está en el alcance de quien actúa: Administración ve todo el centro; los demás, sus unidades y, si tienen residentes asignados, solo esos (el mismo criterio que el resto de
    /// la aplicación: citas externas, ámbito de Auxiliar).</summary>
    private const string ResidentScopeSql = """
        EXISTS (
            SELECT 1 FROM dbo.ambitos_perfil scope_profile
             WHERE scope_profile.id = @ProfileScopeId AND scope_profile.centro_id = @CenterId AND scope_profile.cuenta_id = @AccountId
               AND scope_profile.perfil_codigo = @Profile AND scope_profile.estado = 'ACTIVE' AND scope_profile.revocado_en IS NULL
               AND (scope_profile.perfil_codigo = 'ADMINISTRACION' OR (
                    EXISTS (
                        SELECT 1 FROM dbo.ambitos_perfil_unidad scope_unit
                          JOIN dbo.intervalos_ubicacion_residente scope_location ON scope_location.residente_id = {R} AND scope_location.centro_id = scope_profile.centro_id
                               AND scope_location.unidad_id = scope_unit.unidad_id AND scope_location.vigente_hasta IS NULL
                         WHERE scope_unit.ambito_perfil_id = scope_profile.id AND scope_unit.centro_id = scope_profile.centro_id AND scope_unit.revocado_en IS NULL)
                    AND (EXISTS (
                            SELECT 1 FROM dbo.ambitos_perfil_residente scope_resident
                             WHERE scope_resident.ambito_perfil_id = scope_profile.id AND scope_resident.centro_id = scope_profile.centro_id
                               AND scope_resident.residente_id = {R} AND scope_resident.revocado_en IS NULL)
                         OR NOT EXISTS (
                            SELECT 1 FROM dbo.ambitos_perfil_residente restriction
                             WHERE restriction.ambito_perfil_id = scope_profile.id AND restriction.centro_id = scope_profile.centro_id)))))
        """;

    private const string NotSuspendedSql = """
        NOT EXISTS (SELECT 1 FROM dbo.suspensiones_residente suspension
                     WHERE suspension.residente_id = {R} AND suspension.centro_id = @CenterId AND suspension.finalizada_en IS NULL)
        """;

    /// <summary>El centro es un Centro de Día y tiene la asistencia encendida (la fila vigente del interruptor).</summary>
    private const string FeatureOnSql = """
        SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.centros center WHERE center.id = @CenterId AND center.tipo_de_servicio = N'CENTRO_DE_DIA')
                     AND ISNULL((SELECT TOP (1) CAST(sw.activa AS INT) FROM dbo.asistencia_centro sw WHERE sw.centro_id = @CenterId ORDER BY sw.numero DESC), 0) = 1
                    THEN 1 ELSE 0 END
        """;

    /// <summary>La ausencia {A} (una fila REGISTRADA) cubre el día {D}: no está anulada y su último día vigente (el menor entre su hasta y los de sus acortamientos) no es anterior.</summary>
    private const string AbsenceCoversSql = """
        EXISTS (SELECT 1 FROM dbo.ausencias_programadas absence
                 WHERE absence.centro_id = @CenterId AND absence.residente_id = {R} AND absence.tipo_codigo = N'REGISTRADA' AND absence.desde <= {D}
                   AND NOT EXISTS (SELECT 1 FROM dbo.ausencias_programadas annulled WHERE annulled.ausencia_id = absence.id AND annulled.tipo_codigo = N'ANULADA')
                   AND (SELECT MIN(x.hasta) FROM dbo.ausencias_programadas x WHERE x.ausencia_id = absence.id AND x.tipo_codigo IN (N'REGISTRADA', N'ACORTADA')) >= {D})
        """;

    // ---- El interruptor del centro ----

    public async Task<AttendanceCenterState> ReadCenterAsync(AttendanceActor actor, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await RequireScopeAsync(connection, null, actor, ct);
        var dayCenter = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.centros WHERE id = @CenterId AND tipo_de_servicio = N'CENTRO_DE_DIA') THEN 1 ELSE 0 END", Params(actor), cancellationToken: ct));
        var latest = await LatestSwitchAsync(connection, null, actor, lockRow: false, ct);
        return new AttendanceCenterState(dayCenter, latest?.Active ?? false, latest?.Number ?? 0);
    }

    public async Task<int> SetCenterAsync(AttendanceActor actor, int expectedNumber, bool enable, bool informed, bool impactAssessment, CancellationToken ct = default)
    {
        if (actor.Profile != SystemProfile.Administracion)
        {
            throw new AccessDeniedException();
        }

        if (enable && !(informed && impactAssessment))
        {
            throw new DomainValidationException(AttendanceRules.InvalidCode);
        }

        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await RequireScopeAsync(connection, transaction, actor, ct);
        var latest = await LatestSwitchAsync(connection, transaction, actor, lockRow: true, ct);
        if ((latest?.Number ?? 0) != expectedNumber || (latest?.Active ?? false) == enable)
        {
            throw new DomainValidationException(AttendanceRules.ConflictCode);
        }

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await InsertAsync(connection, transaction, """
            INSERT INTO dbo.asistencia_centro (id, centro_id, numero, activa, confirma_informada, confirma_eipd, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (@Id, @CenterId, @Number, @Enable, @Informed, @Impact, @AccountId, @Profile, @Now);
            """, Params(actor, new { Id = id, Number = expectedNumber + 1, Enable = enable, Informed = enable && informed, Impact = enable && impactAssessment, Now = now.UtcDateTime }), ct);
        await AuditAsync(connection, transaction, actor, null, null, id, enable ? "ATTENDANCE_CENTER_ON" : "ATTENDANCE_CENTER_OFF", now, ct);
        transaction.Commit();
        return expectedNumber + 1;
    }

    // ---- La lista de un día ----

    public async Task<IReadOnlyList<AttendanceDayEntry>> ListDayAsync(AttendanceActor actor, DateOnly date, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await RequireDayCenterAsync(connection, null, actor, ct);
        var rows = await connection.QueryAsync<DayRow>(new CommandDefinition($"""
            SELECT resident.id AS ResidentId, resident.nombre_visible AS ResidentName, location.unidad_id AS UnitId, unit.nombre_visible AS UnitName,
                   pattern.dias_esperados AS PatternMask,
                   mark.numero AS MarkNumber, mark.estado_codigo AS MarkState, mark.causa_codigo AS MarkCause,
                   CASE WHEN @ReadNote = 1 THEN mark.nota END AS MarkNote,
                   CAST(CASE WHEN {AbsenceCoversSql.Replace("{R}", "resident.id").Replace("{D}", "@Date")} THEN 1 ELSE 0 END AS BIT) AS ScheduledAbsence,
                   resident.creado_en AS CreatedAt
              FROM dbo.residentes resident
              LEFT JOIN dbo.intervalos_ubicacion_residente location ON location.residente_id = resident.id AND location.centro_id = resident.centro_id AND location.vigente_hasta IS NULL
              LEFT JOIN dbo.unidades unit ON unit.id = location.unidad_id AND unit.centro_id = resident.centro_id
              OUTER APPLY (SELECT TOP (1) p.dias_esperados FROM dbo.asistencia_pauta p
                            WHERE p.centro_id = resident.centro_id AND p.residente_id = resident.id AND p.vigente_desde <= @Date ORDER BY p.numero DESC) pattern
              OUTER APPLY (SELECT TOP (1) a.numero, a.estado_codigo, a.causa_codigo, a.nota FROM dbo.asistencia_dia a
                            WHERE a.centro_id = resident.centro_id AND a.residente_id = resident.id AND a.fecha = @Date ORDER BY a.numero DESC) mark
             WHERE resident.centro_id = @CenterId AND resident.estado = 'ACTIVE'
               AND {NotSuspendedSql.Replace("{R}", "resident.id")}
               AND {ResidentScopeSql.Replace("{R}", "resident.id")}
             ORDER BY unit.nombre_visible, resident.nombre_visible
            """, Params(actor, new { Date = date.ToDateTime(TimeOnly.MinValue), ReadNote = AttendanceRules.CanReadNote(actor.Profile) }), cancellationToken: ct));

        return rows.Select(row =>
        {
            AttendanceMark? mark = row.MarkNumber is { } number
                ? new AttendanceMark(number, EnumCode.ParseCode<AttendanceState>(row.MarkState!), row.MarkCause is null ? null : EnumCode.ParseCode<AttendanceCause>(row.MarkCause), row.MarkNote)
                : null;
            var expected = row.PatternMask is not { } mask || AttendanceRules.Expects(mask, date);
            return new AttendanceDayEntry(
                ResidentId.From(row.ResidentId), row.ResidentName, row.UnitId, row.UnitName, row.PatternMask is not null, expected, mark, row.ScheduledAbsence,
                // La misma regla que la base: no se anota antes del día, en el centro, en que se creó la ficha.
                AttendanceRules.StatusOf(mark?.State, row.ScheduledAbsence), date < DateOnly.FromDateTime(CenterTime.Local(row.CreatedAt)));
        }).ToList();
    }

    // ---- Anotar y corregir ----

    public async Task<int> RecordAsync(
        AttendanceActor actor, ResidentId residentId, DateOnly date, DateOnly todayAtCenter, AttendanceState state, AttendanceCause? cause, string? note, CancellationToken ct = default)
    {
        if (!AttendanceRules.CanRecord(actor.Profile))
        {
            throw new AccessDeniedException();
        }

        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await RequireFeatureAsync(connection, transaction, actor, ct);
        var unitId = await RequireResidentAsync(connection, transaction, actor, residentId, ct);

        // Dos anotaciones de la misma persona van una detrás de otra por el bloqueo de arriba: de él depende que esta lectura, ya en su propia sentencia, vea la versión anterior. No se reserva un
        // tramo de la tabla (HOLDLOCK): abarcaba a otras personas y acababa en interbloqueos. FORCESEEK deja la lectura en las filas de esta persona.
        var latest = await connection.QuerySingleOrDefaultAsync<MarkRow?>(new CommandDefinition("""
            SELECT TOP (1) numero AS Number, estado_codigo AS State, causa_codigo AS Cause, nota AS Note
              FROM dbo.asistencia_dia WITH (FORCESEEK (UX_asd_numero (centro_id, residente_id, fecha)))
             WHERE centro_id = @CenterId AND residente_id = @ResidentId AND fecha = CAST(@Date AS DATE) ORDER BY numero DESC
            """, Params(actor, new { ResidentId = residentId.Value, Date = date.ToDateTime(TimeOnly.MinValue) }), transaction, cancellationToken: ct));
        if (latest is not null && latest.State == state.ToCode() && latest.Cause == cause?.ToCode() && (latest.Note ?? "") == (note ?? ""))
        {
            return latest.Number;
        }

        var number = (latest?.Number ?? 0) + 1;
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await InsertAsync(connection, transaction, """
            INSERT INTO dbo.asistencia_dia
                (id, centro_id, residente_id, fecha, numero, estado_codigo, causa_codigo, nota, anotado_por_cuenta_id, anotado_por_perfil, anotado_en)
            VALUES (@Id, @CenterId, @ResidentId, @Date, @Number, @State, @Cause, @Note, @AccountId, @Profile, @Now);
            """, Params(actor, new
        {
            Id = id, ResidentId = residentId.Value, Date = date.ToDateTime(TimeOnly.MinValue), Number = number, State = state.ToCode(), Cause = cause?.ToCode(), Note = note, Now = now.UtcDateTime,
        }), ct);
        await AuditAsync(connection, transaction, actor, residentId.Value, unitId, id, number == 1 ? "ATTENDANCE_MARK" : "ATTENDANCE_CORRECT", now, ct);
        transaction.Commit();
        return number;
    }

    // ---- La pauta ----

    public async Task<AttendancePatternState> ReadPatternAsync(AttendanceActor actor, ResidentId residentId, DateOnly todayAtCenter, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await RequireDayCenterAsync(connection, null, actor, ct);
        var name = await ResidentNameAsync(connection, null, actor, residentId, ct);
        var rows = await PatternVersionsAsync(connection, null, actor, residentId, ct);
        var versions = rows.Select(row => new PatternVersion(row.Number, DateOnly.FromDateTime(row.From), row.Days)).ToList();
        var inForce = versions.Where(v => v.From <= todayAtCenter).MaxBy(v => v.Number);
        // El Auxiliar necesita la pauta en vigor y los próximos cambios, no todo el historial (lectura del DPO): un cambio sustituido ya no es un próximo cambio.
        var scheduled = AttendanceRules.ScheduledChanges(versions, todayAtCenter).Select(v => v.Number).ToHashSet();
        var shown = rows
            .Where(row => actor.Profile != SystemProfile.Auxiliar || scheduled.Contains(row.Number) || row.Number == inForce?.Number)
            .OrderByDescending(row => row.Number)
            .Select(row => new PatternVersionEntry(
                row.Number, DateOnly.FromDateTime(row.From), row.Days, DateOnly.FromDateTime(CenterTime.Local(row.RegisteredAt)), EnumCode.ParseCode<SystemProfile>(row.Profile)))
            .ToList();
        return new AttendancePatternState(residentId, name, versions.Count == 0 ? 0 : versions.Max(v => v.Number), inForce?.DaysMask ?? 0, shown);
    }

    public async Task<int> SetPatternAsync(
        AttendanceActor actor, ResidentId residentId, int expectedNumber, int daysMask, DateOnly? effectiveFrom, DateOnly todayAtCenter, bool replaceScheduled = false, CancellationToken ct = default)
    {
        if (!AttendanceRules.CanManageAbsences(actor.Profile))
        {
            throw new AccessDeniedException();
        }

        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await RequireFeatureAsync(connection, transaction, actor, ct);
        var unitId = await RequireResidentAsync(connection, transaction, actor, residentId, ct);
        var rows = await PatternVersionsAsync(connection, transaction, actor, residentId, ct);
        var latest = rows.MaxBy(row => row.Number);
        if ((latest?.Number ?? 0) != expectedNumber)
        {
            throw new DomainValidationException(AttendanceRules.ConflictCode);
        }

        if (latest?.Days == daysMask)
        {
            return latest.Number;
        }

        var inForce = rows.Where(row => DateOnly.FromDateTime(row.From) <= todayAtCenter).MaxBy(row => row.Number);
        var from = AttendanceRules.ValidatePatternDate(effectiveFrom, todayAtCenter, inForce is null ? null : DateOnly.FromDateTime(inForce.From));

        var versions = rows.Select(row => new PatternVersion(row.Number, DateOnly.FromDateTime(row.From), row.Days)).ToList();
        if (!replaceScheduled && AttendanceRules.ScheduledChanges(versions, todayAtCenter).Any(scheduled => from <= scheduled.From))
        {
            throw new DomainValidationException(AttendanceRules.PatternReplacesScheduledCode);
        }

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await InsertAsync(connection, transaction, """
            INSERT INTO dbo.asistencia_pauta (id, centro_id, residente_id, numero, dias_esperados, vigente_desde, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (@Id, @CenterId, @ResidentId, @Number, @Days, CAST(@From AS DATE), @AccountId, @Profile, @Now);
            """, Params(actor, new { Id = id, ResidentId = residentId.Value, Number = expectedNumber + 1, Days = daysMask, From = from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), Now = now.UtcDateTime }), ct);
        await AuditAsync(connection, transaction, actor, residentId.Value, unitId, id, "ATTENDANCE_PATTERN_SET", now, ct);
        transaction.Commit();
        return expectedNumber + 1;
    }

// ---- La lista de seguimiento (se calcula, no se guarda) ----

    public async Task<AttendanceFollowUp> ListFollowUpAsync(AttendanceActor actor, DateOnly todayAtCenter, CancellationToken ct = default)
    {
        if (actor.Profile != SystemProfile.Administracion)
        {
            throw new AccessDeniedException();
        }

        using var connection = await connections.OpenAsync(ct);
        await RequireDayCenterAsync(connection, null, actor, ct);
        var from = todayAtCenter.AddDays(-AttendanceRules.FollowUpWindowDays);
        var parameters = Params(actor, new { From = from.ToDateTime(TimeOnly.MinValue), Today = todayAtCenter.ToDateTime(TimeOnly.MinValue) });
        var residents = (await connection.QueryAsync<FollowUpResidentRow>(new CommandDefinition($"""
            SELECT resident.id AS ResidentId, resident.nombre_visible AS ResidentName, unit.nombre_visible AS UnitName,
                   resident.fecha_ingreso AS AdmissionDate, resident.creado_en AS CreatedAt
              FROM dbo.residentes resident
              LEFT JOIN dbo.intervalos_ubicacion_residente location ON location.residente_id = resident.id AND location.centro_id = resident.centro_id AND location.vigente_hasta IS NULL
              LEFT JOIN dbo.unidades unit ON unit.id = location.unidad_id AND unit.centro_id = resident.centro_id
             WHERE resident.centro_id = @CenterId AND resident.estado = 'ACTIVE'
               AND {NotSuspendedSql.Replace("{R}", "resident.id")}
               AND {ResidentScopeSql.Replace("{R}", "resident.id")}
            """, parameters, cancellationToken: ct))).ToList();
        var versions = (await connection.QueryAsync<PatternRow>(new CommandDefinition("""
            SELECT residente_id AS ResidentId, numero AS Number, dias_esperados AS Days, vigente_desde AS [From], cambiado_en AS RegisteredAt, cambiado_por_perfil AS Profile
              FROM dbo.asistencia_pauta WHERE centro_id = @CenterId
            """, parameters, cancellationToken: ct))).ToLookup(row => row.ResidentId);
        // Solo la última versión de cada día.
        var marks = (await connection.QueryAsync<FollowUpMarkRow>(new CommandDefinition("""
            SELECT residente_id AS ResidentId, fecha AS Day, estado_codigo AS State, causa_codigo AS Cause
              FROM (SELECT a.residente_id, a.fecha, a.estado_codigo, a.causa_codigo,
                           ROW_NUMBER() OVER (PARTITION BY a.residente_id, a.fecha ORDER BY a.numero DESC) AS rank
                      FROM dbo.asistencia_dia a WHERE a.centro_id = @CenterId AND a.fecha >= @From AND a.fecha < @Today) latest
             WHERE rank = 1
            """, parameters, cancellationToken: ct))).ToLookup(row => row.ResidentId);
        var absences = (await connection.QueryAsync<FollowUpAbsenceRow>(new CommandDefinition("""
            SELECT absence.residente_id AS ResidentId, absence.desde AS FromDate,
                   (SELECT MIN(x.hasta) FROM dbo.ausencias_programadas x WHERE x.ausencia_id = absence.id AND x.tipo_codigo IN (N'REGISTRADA', N'ACORTADA')) AS ToDate
              FROM dbo.ausencias_programadas absence
             WHERE absence.centro_id = @CenterId AND absence.tipo_codigo = N'REGISTRADA' AND absence.desde < @Today
               AND NOT EXISTS (SELECT 1 FROM dbo.ausencias_programadas annulled WHERE annulled.ausencia_id = absence.id AND annulled.tipo_codigo = N'ANULADA')
            """, parameters, cancellationToken: ct))).Where(row => DateOnly.FromDateTime(row.ToDate) >= from).ToLookup(row => row.ResidentId);

        var absent = new List<FollowUpAbsence>();
        var unmarked = new List<FollowUpUnmarked>();
        var withoutPattern = new List<FollowUpWithoutPattern>();
        foreach (var resident in residents.OrderBy(r => r.UnitName).ThenBy(r => r.ResidentName))
        {
            var byDay = marks[resident.ResidentId].ToDictionary(m => DateOnly.FromDateTime(m.Day));
            var covered = absences[resident.ResidentId].Select(a => (From: DateOnly.FromDateTime(a.FromDate), To: DateOnly.FromDateTime(a.ToDate))).ToList();
            // Se cuenta desde que existe la ficha en el día del centro, igual que la base (que no deja anotar antes), o desde el ingreso si es posterior.
            var created = DateOnly.FromDateTime(CenterTime.Local(resident.CreatedAt));
            var firstDay = resident.AdmissionDate is { } admission && DateOnly.FromDateTime(admission) > created ? DateOnly.FromDateTime(admission) : created;
            var patterns = versions[resident.ResidentId].Select(v => new PatternVersion(v.Number, DateOnly.FromDateTime(v.From), v.Days)).ToList();
            if (patterns.Count == 0)
            {
                withoutPattern.Add(new FollowUpWithoutPattern(ResidentId.From(resident.ResidentId), resident.ResidentName, resident.UnitName));
            }

            var outcome = AttendanceRules.FollowUp(
                todayAtCenter, firstDay, day => AttendanceRules.PatternFor(patterns, day),
                day => AttendanceRules.StatusOf(
                    byDay.TryGetValue(day, out var m) ? EnumCode.ParseCode<AttendanceState>(m.State) : null, covered.Any(a => a.From <= day && day <= a.To)),
                day => byDay.TryGetValue(day, out var m) && m.Cause is not null ? EnumCode.ParseCode<AttendanceCause>(m.Cause) : null);
            if (outcome.UnmarkedDays > 0)
            {
                unmarked.Add(new FollowUpUnmarked(ResidentId.From(resident.ResidentId), resident.ResidentName, resident.UnitName, outcome.UnmarkedDays));
            }
            else if (outcome.AbsentDays >= AttendanceRules.FollowUpDays)
            {
                absent.Add(new FollowUpAbsence(
                    ResidentId.From(resident.ResidentId), resident.ResidentName, resident.UnitName, outcome.AbsentDays, outcome.Capped, outcome.LastCame, outcome.Causes));
            }
        }

        // Abrir la lista queda registrado sin cifras ni nombres: quién y cuándo (muestra a varias personas a la vez).
        await AuditAsync(connection, null, actor, null, null, actor.CenterId.Value, "ATTENDANCE_FOLLOWUP_OPEN", DateTimeOffset.UtcNow, ct);
        return new AttendanceFollowUp(absent.OrderByDescending(a => a.Days).ThenBy(a => a.ResidentName).ToList(), unmarked, withoutPattern);
    }

    // ---- Las ausencias programadas ----

    public async Task<IReadOnlyList<ScheduledAbsence>> ListAbsencesAsync(AttendanceActor actor, ResidentId? residentId, DateOnly todayAtCenter, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await RequireDayCenterAsync(connection, null, actor, ct);
        var rows = await connection.QueryAsync<AbsenceRow>(new CommandDefinition($"""
            SELECT absence.id AS Id, absence.residente_id AS ResidentId, resident.nombre_visible AS ResidentName, absence.desde AS FromDate,
                   (SELECT MIN(x.hasta) FROM dbo.ausencias_programadas x WHERE x.ausencia_id = absence.id AND x.tipo_codigo IN (N'REGISTRADA', N'ACORTADA')) AS ToDate,
                   CASE WHEN @ReadReason = 1 THEN absence.motivo_codigo END AS Reason
              FROM dbo.ausencias_programadas absence
              JOIN dbo.residentes resident ON resident.id = absence.residente_id AND resident.centro_id = absence.centro_id
             WHERE absence.centro_id = @CenterId AND absence.tipo_codigo = N'REGISTRADA' AND resident.estado = 'ACTIVE'
               AND (@ResidentId IS NULL OR absence.residente_id = @ResidentId)
               AND NOT EXISTS (SELECT 1 FROM dbo.ausencias_programadas annulled WHERE annulled.ausencia_id = absence.id AND annulled.tipo_codigo = N'ANULADA')
               AND (SELECT MIN(x.hasta) FROM dbo.ausencias_programadas x WHERE x.ausencia_id = absence.id AND x.tipo_codigo IN (N'REGISTRADA', N'ACORTADA')) >= @Today
               AND {NotSuspendedSql.Replace("{R}", "resident.id")}
               AND {ResidentScopeSql.Replace("{R}", "resident.id")}
             ORDER BY absence.desde, resident.nombre_visible
            """, Params(actor, new
        {
            ResidentId = residentId?.Value, Today = todayAtCenter.ToDateTime(TimeOnly.MinValue), ReadReason = AttendanceRules.CanReadAbsenceReason(actor.Profile),
        }), cancellationToken: ct));
        return rows.Select(row => new ScheduledAbsence(
            row.Id, ResidentId.From(row.ResidentId), row.ResidentName, DateOnly.FromDateTime(row.FromDate), DateOnly.FromDateTime(row.ToDate),
            row.Reason is null ? null : EnumCode.ParseCode<AbsenceReason>(row.Reason))).ToList();
    }

    public async Task<Guid> RegisterAbsenceAsync(AttendanceActor actor, ResidentId residentId, DateOnly from, DateOnly to, AbsenceReason reason, CancellationToken ct = default)
    {
        if (!AttendanceRules.CanManageAbsences(actor.Profile))
        {
            throw new AccessDeniedException();
        }

        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await RequireFeatureAsync(connection, transaction, actor, ct);
        var unitId = await RequireResidentAsync(connection, transaction, actor, residentId, ct);
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await InsertAsync(connection, transaction, """
            INSERT INTO dbo.ausencias_programadas
                (id, centro_id, residente_id, tipo_codigo, ausencia_id, desde, hasta, motivo_codigo, causa_codigo, registrado_por_cuenta_id, registrado_por_perfil, registrado_en)
            VALUES (@Id, @CenterId, @ResidentId, N'REGISTRADA', @Id, @From, @To, @Reason, NULL, @AccountId, @Profile, @Now);
            """, Params(actor, new
        {
            Id = id, ResidentId = residentId.Value, From = from.ToDateTime(TimeOnly.MinValue), To = to.ToDateTime(TimeOnly.MinValue), Reason = reason.ToCode(), Now = now.UtcDateTime,
        }), ct);
        await AuditAsync(connection, transaction, actor, residentId.Value, unitId, id, "ABSENCE_REGISTER", now, ct);
        transaction.Commit();
        return id;
    }

    public Task AnnulAbsenceAsync(AttendanceActor actor, Guid absenceId, CancellationToken ct = default) =>
        ChangeAbsenceAsync(actor, absenceId, "ANULADA", "ERROR", null, "ABSENCE_ANNUL", ct);

    public Task ShortenAbsenceAsync(AttendanceActor actor, Guid absenceId, DateOnly newTo, CancellationToken ct = default) =>
        ChangeAbsenceAsync(actor, absenceId, "ACORTADA", "REGRESO_ANTES", newTo.ToDateTime(TimeOnly.MinValue), "ABSENCE_SHORTEN", ct);

    private async Task ChangeAbsenceAsync(AttendanceActor actor, Guid absenceId, string type, string cause, DateTime? newTo, string action, CancellationToken ct)
    {
        if (!AttendanceRules.CanManageAbsences(actor.Profile))
        {
            throw new AccessDeniedException();
        }

        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await RequireFeatureAsync(connection, transaction, actor, ct);
        // La ausencia tiene que ser de un residente del alcance de quien actúa: si no, es el mismo «no existe» de siempre.
        var residentId = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT residente_id FROM dbo.ausencias_programadas WHERE id = @AbsenceId AND centro_id = @CenterId AND tipo_codigo = N'REGISTRADA'",
            Params(actor, new { AbsenceId = absenceId }), transaction, cancellationToken: ct))
            ?? throw new AccessDeniedException();
        // Bloquea la fila del residente, como al registrar: dos cambios sobre la misma persona van uno detrás de otro y el segundo ya ve lo que guardó el primero.
        var unitId = await RequireResidentAsync(connection, transaction, actor, ResidentId.From(residentId), ct);

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await InsertAsync(connection, transaction, """
            INSERT INTO dbo.ausencias_programadas
                (id, centro_id, residente_id, tipo_codigo, ausencia_id, desde, hasta, motivo_codigo, causa_codigo, registrado_por_cuenta_id, registrado_por_perfil, registrado_en)
            VALUES (@Id, @CenterId, @ResidentId, @Type, @AbsenceId, NULL, @To, NULL, @Cause, @AccountId, @Profile, @Now);
            """, Params(actor, new { Id = id, ResidentId = residentId, Type = type, AbsenceId = absenceId, To = newTo, Cause = cause, Now = now.UtcDateTime }), ct);
        await AuditAsync(connection, transaction, actor, residentId, unitId, id, action, now, ct);
        transaction.Commit();
    }

    // ---- Comprobaciones y ayudantes ----

    private static DynamicParameters Params(AttendanceActor actor, object? extra = null)
    {
        var parameters = new DynamicParameters(extra ?? new { });
        parameters.Add("AccountId", actor.AccountId.Value);
        parameters.Add("ProfileScopeId", actor.ProfileScopeId);
        parameters.Add("CenterId", actor.CenterId.Value);
        parameters.Add("Profile", actor.Profile.ToCode());
        return parameters;
    }

    private static async Task RequireScopeAsync(SqlConnection connection, SqlTransaction? transaction, AttendanceActor actor, CancellationToken ct)
    {
        if (await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(ActorScopeSql, Params(actor), transaction, cancellationToken: ct)) is null)
        {
            throw new AccessDeniedException();
        }
    }

    /// <summary>El ámbito vigente y el centro con la función encendida; si no, ATTENDANCE_DISABLED (un Centro de Día con la función apagada, o una Residencia).</summary>
    /// <summary>Para leer: el ámbito vigente y un Centro de Día, aunque la función esté apagada. Apagarla impide anotar días nuevos, pero no oculta lo ya anotado (ADR 0019, punto 8; derecho de acceso).</summary>
    private static async Task RequireDayCenterAsync(SqlConnection connection, SqlTransaction? transaction, AttendanceActor actor, CancellationToken ct)
    {
        await RequireScopeAsync(connection, transaction, actor, ct);
        if (!await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.centros center WHERE center.id = @CenterId AND center.tipo_de_servicio = N'CENTRO_DE_DIA') THEN 1 ELSE 0 END", Params(actor), transaction, cancellationToken: ct)))
        {
            throw new DomainValidationException(AttendanceRules.DisabledCode);
        }
    }

    private static async Task RequireFeatureAsync(SqlConnection connection, SqlTransaction? transaction, AttendanceActor actor, CancellationToken ct)
    {
        await RequireScopeAsync(connection, transaction, actor, ct);
        if (!await connection.ExecuteScalarAsync<bool>(new CommandDefinition(FeatureOnSql, Params(actor), transaction, cancellationToken: ct)))
        {
            throw new DomainValidationException(AttendanceRules.DisabledCode);
        }
    }

    /// <summary>El residente está activo, no suspendido y en el alcance de quien actúa. Devuelve su unidad (para la auditoría).</summary>
    private static async Task<Guid?> RequireResidentAsync(SqlConnection connection, SqlTransaction transaction, AttendanceActor actor, ResidentId residentId, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<UnitRow?>(new CommandDefinition($"""
            SELECT (SELECT TOP (1) location.unidad_id FROM dbo.intervalos_ubicacion_residente location
                     WHERE location.residente_id = resident.id AND location.centro_id = resident.centro_id AND location.vigente_hasta IS NULL) AS UnitId
              FROM dbo.residentes resident WITH (UPDLOCK, ROWLOCK)
             WHERE resident.id = @ResidentId AND resident.centro_id = @CenterId AND resident.estado = 'ACTIVE'
               AND {NotSuspendedSql.Replace("{R}", "resident.id")}
               AND {ResidentScopeSql.Replace("{R}", "resident.id")}
            """, Params(actor, new { ResidentId = residentId.Value }), transaction, cancellationToken: ct));
        return row is null ? throw new AccessDeniedException() : row.UnitId;
    }

    private static async Task<string> ResidentNameAsync(SqlConnection connection, SqlTransaction? transaction, AttendanceActor actor, ResidentId residentId, CancellationToken ct) =>
        await connection.ExecuteScalarAsync<string?>(new CommandDefinition($"""
            SELECT resident.nombre_visible FROM dbo.residentes resident
             WHERE resident.id = @ResidentId AND resident.centro_id = @CenterId AND resident.estado = 'ACTIVE'
               AND {ResidentScopeSql.Replace("{R}", "resident.id")}
            """, Params(actor, new { ResidentId = residentId.Value }), transaction, cancellationToken: ct)) ?? throw new AccessDeniedException();

    private static Task<SwitchRow?> LatestSwitchAsync(SqlConnection connection, SqlTransaction? transaction, AttendanceActor actor, bool lockRow, CancellationToken ct) =>
        connection.QuerySingleOrDefaultAsync<SwitchRow?>(new CommandDefinition($"""
            SELECT TOP (1) numero AS Number, activa AS Active FROM dbo.asistencia_centro {(lockRow ? "WITH (UPDLOCK, HOLDLOCK)" : "")} WHERE centro_id = @CenterId ORDER BY numero DESC
            """, Params(actor), transaction, cancellationToken: ct));

    /// <summary>Las versiones de la pauta de una persona. Al cambiarla se llama con la fila del residente ya bloqueada (<see cref="RequireResidentAsync"/>), que es lo que ordena dos cambios sobre la
    /// misma persona; FORCESEEK deja la lectura en sus filas.</summary>
    private static async Task<List<PatternRow>> PatternVersionsAsync(
        SqlConnection connection, SqlTransaction? transaction, AttendanceActor actor, ResidentId residentId, CancellationToken ct) =>
        (await connection.QueryAsync<PatternRow>(new CommandDefinition("""
            SELECT residente_id AS ResidentId, numero AS Number, dias_esperados AS Days, vigente_desde AS [From], cambiado_en AS RegisteredAt, cambiado_por_perfil AS Profile
              FROM dbo.asistencia_pauta WITH (FORCESEEK (UX_asp_numero (centro_id, residente_id)))
             WHERE centro_id = @CenterId AND residente_id = @ResidentId
            """, Params(actor, new { ResidentId = residentId.Value }), transaction, cancellationToken: ct))).ToList();

    /// <summary>Inserta una fila. Una versión repetida (índice único) o una secuencia que la base rechaza es un conflicto de versión; las demás reglas de la base se traducen a su código de aplicación.</summary>
    private static async Task InsertAsync(SqlConnection connection, SqlTransaction transaction, string sql, DynamicParameters parameters, CancellationToken ct)
    {
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: ct));
        }
        catch (SqlException error) when (error.Number is 2601 or 2627 or 51151)
        {
            throw new DomainValidationException(AttendanceRules.ConflictCode);
        }
        catch (SqlException error) when (error.Number is >= 51150 and <= 51157)
        {
            throw new DomainValidationException(error.Number switch
            {
                51150 => AttendanceRules.DisabledCode,
                51153 => AttendanceRules.InvalidCode,
                // La base lanza 51154 por fecha futura o anterior a la ficha; la futura ya la ha rechazado la aplicación antes de llegar aquí.
                51154 => AttendanceRules.BeforeRecordCode,
                51156 => AttendanceRules.AbsenceOverlapCode,
                51157 => AttendanceRules.PatternDateInvalidCode,
                _ => AttendanceRules.AbsenceInvalidCode,
            });
        }
    }

    /// <summary>Auditoría con solo identificadores: el recurso es la fila escrita; el estado, la causa, la nota, el motivo, las fechas y la pauta se leen de ella y no se copian.</summary>
    private static Task AuditAsync(
        SqlConnection connection, SqlTransaction? transaction, AttendanceActor actor, Guid? residentId, Guid? unitId, Guid rowId, string action, DateTimeOffset now, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.eventos_auditoria (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (NEWID(), @AccountId, @Profile, @CenterId, @UnitId, @ResidentId, @ResourceType, @RowId, @Action, @Now);
            """, new
        {
            AccountId = actor.AccountId.Value, Profile = actor.Profile.ToCode(), CenterId = actor.CenterId.Value, UnitId = unitId, ResidentId = residentId,
            ResourceType = ResourceType, RowId = rowId, Action = action, Now = now.UtcDateTime,
        }, transaction, cancellationToken: ct));

    private sealed record SwitchRow(int Number, bool Active);

    private sealed record FollowUpResidentRow(Guid ResidentId, string ResidentName, string? UnitName, DateTime? AdmissionDate, DateTime CreatedAt);

    private sealed record PatternRow(Guid ResidentId, int Number, byte Days, DateTime From, DateTime RegisteredAt, string Profile);

    private sealed record FollowUpMarkRow(Guid ResidentId, DateTime Day, string State, string? Cause);

    private sealed record FollowUpAbsenceRow(Guid ResidentId, DateTime FromDate, DateTime ToDate);

    private sealed record UnitRow(Guid? UnitId);

    private sealed record MarkRow(int Number, string State, string? Cause, string? Note);

    private sealed record DayRow(
        Guid ResidentId, string ResidentName, Guid? UnitId, string? UnitName, byte? PatternMask, int? MarkNumber, string? MarkState, string? MarkCause, string? MarkNote, bool ScheduledAbsence, DateTime CreatedAt);

    private sealed record AbsenceRow(Guid Id, Guid ResidentId, string ResidentName, DateTime FromDate, DateTime ToDate, string? Reason);
}
