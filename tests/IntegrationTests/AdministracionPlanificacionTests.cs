using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Scheduling;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;

namespace ResidApp.IntegrationTests;

/// <summary>Administración, turnos y equipos (historia 4, ADM-15/17, script 0027), fase 2: planificación puntual de un equipo en un
/// turno para varias fechas, con aviso de solapamientos que Administración decide. Cada prueba crea su propio centro.</summary>
public class AdministracionPlanificacionTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private sealed record World(
        SeededProfile Admin, AdministracionTurnosApplicationService Service, SeededProfile Nurse1, SeededProfile Nurse2,
        Guid TeamA, Guid TeamB, Guid TeamC, Guid Morning, Guid Afternoon, Guid Night, Guid Dawn);

    /// <summary>Equipo A (enfermera 1 y 2), B (enfermera 2) y C (nadie); turnos de mañana 07–15, tarde 14–22, noche 22–06 y madrugada 05–13.</summary>
    private static async Task<World> CreateWorldAsync()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildTurnos(admin.ExternalSubject);
        var n1 = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var n2 = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        async Task<Guid> Shift(string name, string start, string end) => (await service.CreateShiftAsync(new CreateShiftCommand(
            admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), name, TimeOnly.Parse(start), TimeOnly.Parse(end)))).Value;
        async Task<Guid> Team(string name, params SeededProfile[] members)
        {
            var id = (await service.CreateTeamAsync(new CreateTeamCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), admin.UnitId, name))).Value;
            foreach (var member in members)
            {
                Assert.True((await service.ChangeTeamMemberAsync(new ChangeTeamMemberCommand(admin.ProfileScopeId, admin.CenterId, id, member.AccountId, true))).Ok);
            }

            return id;
        }

        return new World(admin, service, n1, n2, await Team("Equipo A", n1, n2), await Team("Equipo B", n2), await Team("Equipo C"),
            await Shift("Mañana", "07:00", "15:00"), await Shift("Tarde", "14:00", "22:00"), await Shift("Noche", "22:00", "06:00"),
            await Shift("Madrugada", "05:00", "13:00"));
    }

    private static PlanShiftCommand Plan(World w, Guid team, Guid shift, string? justification = null, Guid? batch = null, params DateOnly[] dates) =>
        new(w.Admin.ProfileScopeId, w.Admin.CenterId, batch ?? Guid.NewGuid(), team, shift, dates, justification);

    private static async Task<IReadOnlyList<ScheduleEntry>> ListAsync(World w, DateOnly? from = null, DateOnly? to = null) =>
        (await w.Service.ListScheduleAsync(new ListScheduleQuery(
            w.Admin.ProfileScopeId, w.Admin.CenterId, from ?? Today, to ?? Today.AddDays(40)))).Value!;

    private static async Task<int> CountAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    [Fact]
    public async Task Planifica_VariasFechasDeGolpe_ConAuditoria_YElReenvioNoDuplica()
    {
        var w = await CreateWorldAsync();
        var batch = Guid.NewGuid();
        var dates = new[] { Today.AddDays(3), Today.AddDays(2), Today.AddDays(4) };

        var first = await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Morning, batch: batch, dates: dates));
        var again = await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Morning, batch: batch, dates: dates));
        var reused = await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Afternoon, batch: batch, dates: dates));

        Assert.True(first.Ok, first.Error?.Message);
        Assert.Equal(dates.Order(), first.Value!.Created);
        Assert.Empty(first.Value.Skipped);
        Assert.True(again.Ok, again.Error?.Message);
        Assert.Equal(dates.Order(), again.Value!.Created);
        Assert.Equal(ApplicationFailureCode.Conflict, reused.Error!.Code);
        var entries = await ListAsync(w);
        Assert.Equal(3, entries.Count);
        Assert.All(entries, e => Assert.Equal("Equipo A", e.TeamName));
        Assert.All(entries, e => Assert.Equal(2, e.TeamMembers));
        Assert.All(entries, e => Assert.Null(e.Justification));
        Assert.Equal(dates.Order(), entries.Select(e => e.Date));
        Assert.Equal(3, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE accion_codigo = 'SCHEDULE_CREATE' AND centro_id = @c", new { c = w.Admin.CenterId.Value }));
    }

    [Fact]
    public async Task LasFechasYaPlanificadas_SeOmiten_YSiSonTodas_NoHayNadaQueGuardar()
    {
        var w = await CreateWorldAsync();
        var d1 = Today.AddDays(2);
        var d2 = Today.AddDays(3);
        Assert.True((await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Morning, dates: [d1]))).Ok);

        var preview = await w.Service.PreviewScheduleAsync(Plan(w, w.TeamA, w.Morning, dates: [d1, d2]));
        var partial = await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Morning, dates: [d1, d2]));
        var none = await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Morning, dates: [d1, d2]));

        Assert.Equal([d2], preview.Value!.ToCreate);
        Assert.Equal([d1], preview.Value.AlreadyPlanned);
        Assert.True(partial.Ok, partial.Error?.Message);
        Assert.Equal([d2], partial.Value!.Created);
        Assert.Equal([d1], partial.Value.Skipped);
        Assert.Equal(ApplicationFailureCode.InvalidInput, none.Error!.Code);
        Assert.Equal(2, (await ListAsync(w)).Count);
    }

    [Fact]
    public async Task ElMismoEquipoEnTurnosQueSeSolapan_AvisaYSoloGuardaConJustificacion_AnotadaEnLasFechasAfectadas()
    {
        var w = await CreateWorldAsync();
        var d1 = Today.AddDays(5);
        var d2 = Today.AddDays(6);
        Assert.True((await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Morning, dates: [d1]))).Ok);

        var preview = await w.Service.PreviewScheduleAsync(Plan(w, w.TeamA, w.Afternoon, dates: [d1, d2]));
        var without = await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Afternoon, dates: [d1, d2]));
        var blank = await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Afternoon, "   ", dates: [d1, d2]));
        var tooLong = await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Afternoon, new string('x', 501), dates: [d1, d2]));
        var beforeConfirming = await ListAsync(w);
        var with = await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Afternoon, "  Refuerzo puntual por baja  ", dates: [d1, d2]));

        var conflict = Assert.Single(preview.Value!.Conflicts);
        Assert.Equal(ScheduleConflictKind.SameTeam, conflict.Kind);
        Assert.Equal(d1, conflict.Date);
        Assert.Equal("Mañana", conflict.OtherShiftName);
        Assert.Equal("Tarde", conflict.ShiftName);
        Assert.Equal(ApplicationFailureCode.Conflict, without.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, blank.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, tooLong.Error!.Code);
        Assert.Single(beforeConfirming);
        Assert.True(with.Ok, with.Error?.Message);
        var afternoon = (await ListAsync(w)).Where(e => e.ShiftId == w.Afternoon).ToDictionary(e => e.Date);
        Assert.Equal("Refuerzo puntual por baja", afternoon[d1].Justification);
        Assert.Null(afternoon[d2].Justification);
    }

    [Fact]
    public async Task UnaPersonaEnDosEquipos_ConTurnosQueSeSolapan_Avisa_PeroNoSiNoCompartenMiembros()
    {
        var w = await CreateWorldAsync();
        var d = Today.AddDays(7);
        Assert.True((await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Morning, dates: [d]))).Ok);

        var shared = await w.Service.PreviewScheduleAsync(Plan(w, w.TeamB, w.Afternoon, dates: [d]));
        var noMembers = await w.Service.PreviewScheduleAsync(Plan(w, w.TeamC, w.Afternoon, dates: [d]));
        var contiguous = await w.Service.PreviewScheduleAsync(Plan(w, w.TeamB, w.Night, dates: [d]));

        var conflict = Assert.Single(shared.Value!.Conflicts);
        Assert.Equal(ScheduleConflictKind.SamePerson, conflict.Kind);
        Assert.Equal(w.Nurse2.ExternalSubject, conflict.PersonName);
        Assert.Equal("Equipo B", conflict.TeamName);
        Assert.Equal("Equipo A", conflict.OtherTeamName);
        Assert.Empty(noMembers.Value!.Conflicts);
        Assert.Empty(contiguous.Value!.Conflicts);
    }

    [Fact]
    public async Task ElTurnoDeNoche_PisaLaMadrugadaDelDiaSiguiente_PeroNoLaMananaQueEmpiezaCuandoTermina()
    {
        var w = await CreateWorldAsync();
        var d = Today.AddDays(8);
        Assert.True((await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Night, dates: [d]))).Ok);

        var dawn = await w.Service.PreviewScheduleAsync(Plan(w, w.TeamB, w.Dawn, dates: [d.AddDays(1)]));
        var morning = await w.Service.PreviewScheduleAsync(Plan(w, w.TeamB, w.Morning, dates: [d.AddDays(1)]));
        var sameDayMorning = await w.Service.PreviewScheduleAsync(Plan(w, w.TeamB, w.Morning, dates: [d]));

        var conflict = Assert.Single(dawn.Value!.Conflicts);
        Assert.Equal(d.AddDays(1), conflict.Date);
        Assert.Equal(ScheduleConflictKind.SamePerson, conflict.Kind);
        Assert.Empty(morning.Value!.Conflicts);
        Assert.Empty(sameDayMorning.Value!.Conflicts);
    }

    [Fact]
    public async Task LosConflictos_SoloCuentanLaPlanificacionDeUnidadesDelAmbito()
    {
        var w = await CreateWorldAsync();
        var d = Today.AddDays(9);
        var foreignUnit = await AddUnitAsync(w.Admin.CenterId);
        var foreignTeam = Guid.NewGuid();
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.equipos (id, centro_id, unidad_id, nombre_visible, estado, creado_en, creado_por_cuenta_id)
                VALUES (@team, @center, @unit, N'Equipo ajeno', 'ACTIVE', SYSUTCDATETIME(), @actor);
                INSERT INTO dbo.equipos_miembros (id, centro_id, equipo_id, cuenta_id, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @center, @team, @nurse, SYSUTCDATETIME(), @actor);
                INSERT INTO dbo.planificacion_turnos (id, centro_id, unidad_id, equipo_id, turno_id, fecha, lote_id, creado_en, creado_por_cuenta_id)
                VALUES (NEWID(), @center, @unit, @team, @shift, @date, NEWID(), SYSUTCDATETIME(), @actor);
                """, new
            {
                team = foreignTeam, center = w.Admin.CenterId.Value, unit = foreignUnit.Value, actor = w.Admin.AccountId.Value,
                nurse = w.Nurse2.AccountId.Value, shift = w.Morning, date = d.ToDateTime(TimeOnly.MinValue),
            });
        }

        var preview = await w.Service.PreviewScheduleAsync(Plan(w, w.TeamB, w.Afternoon, dates: [d]));

        Assert.Empty(preview.Value!.Conflicts);
        Assert.Empty(await ListAsync(w));
    }

    [Fact]
    public async Task Retirar_LiberaLaFecha_ConAuditoria_YNoVale_ParaLoPasadoNiDosVeces()
    {
        var w = await CreateWorldAsync();
        var d = Today.AddDays(4);
        Assert.True((await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Morning, dates: [d]))).Ok);
        var entry = Assert.Single(await ListAsync(w));
        var past = Guid.NewGuid();
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.planificacion_turnos (id, centro_id, unidad_id, equipo_id, turno_id, fecha, lote_id, creado_en, creado_por_cuenta_id)
                VALUES (@id, @center, @unit, @team, @shift, @date, NEWID(), SYSUTCDATETIME(), @actor)
                """, new
            {
                id = past, center = w.Admin.CenterId.Value, unit = w.Admin.UnitId.Value, team = w.TeamB, shift = w.Morning,
                date = Today.AddDays(-3).ToDateTime(TimeOnly.MinValue), actor = w.Admin.AccountId.Value,
            });
        }

        RetireScheduleCommand Retire(Guid id) => new(w.Admin.ProfileScopeId, w.Admin.CenterId, id);
        var retired = await w.Service.RetireScheduleAsync(Retire(entry.ScheduleId));
        var twice = await w.Service.RetireScheduleAsync(Retire(entry.ScheduleId));
        var retiredPast = await w.Service.RetireScheduleAsync(Retire(past));
        var replanned = await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Morning, dates: [d]));

        Assert.True(retired.Ok, retired.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, twice.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, retiredPast.Error!.Code);
        Assert.True(replanned.Ok, replanned.Error?.Message);
        Assert.Single(await ListAsync(w));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE recurso_id = @id AND accion_codigo = 'SCHEDULE_RETIRE'", new { id = entry.ScheduleId }));
    }

    [Fact]
    public async Task FechasInvalidas_EquipoOTurnoInactivo_YPerfilesAjenos_SeRechazan()
    {
        var w = await CreateWorldAsync();
        var other = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var d = Today.AddDays(2);
        var tooMany = Enumerable.Range(1, 368).Select(i => Today.AddDays(i)).ToArray();
        Assert.True((await w.Service.ChangeTeamStatusAsync(new ChangeTeamStatusCommand(w.Admin.ProfileScopeId, w.Admin.CenterId, w.TeamC, false))).Ok);
        Assert.True((await w.Service.ChangeShiftStatusAsync(new ChangeShiftStatusCommand(w.Admin.ProfileScopeId, w.Admin.CenterId, w.Dawn, false))).Ok);

        var results = new[]
        {
            await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Morning, dates: [])),
            await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Morning, dates: [Today.AddDays(-1)])),
            await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Morning, dates: [d, d])),
            await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Morning, dates: [Today.AddDays(400)])),
            await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Morning, dates: tooMany)),
            await w.Service.PlanShiftAsync(Plan(w, w.TeamC, w.Morning, dates: [d])),
            await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Dawn, dates: [d])),
        };
        var denied = new[]
        {
            (await BuildTurnos(other.ExternalSubject).PlanShiftAsync(new PlanShiftCommand(other.ProfileScopeId, other.CenterId, Guid.NewGuid(), w.TeamA, w.Morning, [d]))).Error,
            (await BuildTurnos(w.Nurse1.ExternalSubject).PlanShiftAsync(new PlanShiftCommand(w.Nurse1.ProfileScopeId, w.Nurse1.CenterId, Guid.NewGuid(), w.TeamA, w.Morning, [d]))).Error,
            (await BuildTurnos(w.Nurse1.ExternalSubject).ListScheduleAsync(new ListScheduleQuery(w.Nurse1.ProfileScopeId, w.Nurse1.CenterId, Today, Today.AddDays(5)))).Error,
        };

        Assert.All(results, r => Assert.Equal(ApplicationFailureCode.InvalidInput, r.Error!.Code));
        Assert.All(denied, error => Assert.Equal(ApplicationFailureCode.AccessDenied, error!.Code));
        Assert.Empty(await ListAsync(w));
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await w.Service.ListScheduleAsync(
            new ListScheduleQuery(w.Admin.ProfileScopeId, w.Admin.CenterId, Today, Today.AddDays(90)))).Error!.Code);
    }

    [Fact]
    public async Task BaseDeDatos_LaPlanificacionSoloSeRetira_NoSeEditaNiSeBorra_YNoDuplicaUnaFechaVigente()
    {
        var w = await CreateWorldAsync();
        var d = Today.AddDays(3);
        Assert.True((await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Morning, dates: [d]))).Ok);
        var entry = Assert.Single(await ListAsync(w));
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var parameters = new { id = entry.ScheduleId, center = w.Admin.CenterId.Value, unit = w.Admin.UnitId.Value, team = w.TeamA, shift = w.Morning, date = d.ToDateTime(TimeOnly.MinValue), actor = w.Admin.AccountId.Value };

        var move = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.planificacion_turnos SET fecha = DATEADD(DAY, 1, fecha) WHERE id = @id", parameters));
        var delete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.planificacion_turnos WHERE id = @id", parameters));
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("""
            INSERT INTO dbo.planificacion_turnos (id, centro_id, unidad_id, equipo_id, turno_id, fecha, lote_id, creado_en, creado_por_cuenta_id)
            VALUES (NEWID(), @center, @unit, @team, @shift, @date, NEWID(), SYSUTCDATETIME(), @actor)
            """, parameters));
        var blankJustification = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("""
            INSERT INTO dbo.planificacion_turnos (id, centro_id, unidad_id, equipo_id, turno_id, fecha, lote_id, creado_en, creado_por_cuenta_id, conflicto_justificacion)
            VALUES (NEWID(), @center, @unit, @team, @shift, DATEADD(DAY, 9, @date), NEWID(), SYSUTCDATETIME(), @actor, N'  ')
            """, parameters));

        Assert.Contains("SCHEDULE_RETIRE_ONLY", move.Message);
        Assert.Contains("SCHEDULE_DELETE_FORBIDDEN", delete.Message);
        Assert.Contains("UX_sch_active", duplicate.Message);
        Assert.Contains("CK_sch_justification", blankJustification.Message);
    }

    [Fact]
    public async Task Serie_DeUnAnio_SeGuardaEnUnLote_YEnseñaSusFechas()
    {
        var w = await CreateWorldAsync();
        var batch = Guid.NewGuid();
        var dates = Enumerable.Range(0, SchedulePlan.MaxDates).Select(i => Today.AddDays(i)).ToArray();

        var planned = await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Morning, batch: batch, dates: dates));
        var series = await w.Service.FindScheduleSeriesAsync(new FindScheduleSeriesQuery(w.Admin.ProfileScopeId, w.Admin.CenterId, batch));
        var listed = await ListAsync(w, Today, Today.AddDays(SchedulePlan.MaxListDays - 1));

        Assert.True(planned.Ok, planned.Error?.Message);
        Assert.Equal(367, planned.Value!.Created.Count);
        Assert.True(series.Ok, series.Error?.Message);
        Assert.Equal(367, series.Value!.ActiveDates.Count);
        Assert.Equal(0, series.Value.RetiredDates);
        Assert.Equal(w.TeamA, series.Value.TeamId);
        Assert.Equal(w.Morning, series.Value.ShiftId);
        Assert.All(listed, e => Assert.Equal(batch, e.BatchId));
        Assert.Equal(SchedulePlan.MaxListDays, listed.Count);
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await w.Service.ListScheduleAsync(new ListScheduleQuery(
            w.Admin.ProfileScopeId, w.Admin.CenterId, Today, Today.AddDays(SchedulePlan.MaxListDays)))).Error!.Code);
    }

    [Fact]
    public async Task RetirarSerie_RetiraDesdeLaFechaConUnSoloEventoDeAuditoria_YNoTocaLoAnterior()
    {
        var w = await CreateWorldAsync();
        var batch = Guid.NewGuid();
        var dates = Enumerable.Range(1, 20).Select(i => Today.AddDays(i)).ToArray();
        Assert.True((await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Morning, batch: batch, dates: dates))).Ok);
        var other = Guid.NewGuid();
        Assert.True((await w.Service.PlanShiftAsync(Plan(w, w.TeamB, w.Night, batch: other, dates: [Today.AddDays(15)]))).Ok);
        var retireFrom = Today.AddDays(11);

        var retired = await w.Service.RetireScheduleSeriesAsync(new RetireScheduleSeriesCommand(w.Admin.ProfileScopeId, w.Admin.CenterId, batch, retireFrom));
        var again = await w.Service.RetireScheduleSeriesAsync(new RetireScheduleSeriesCommand(w.Admin.ProfileScopeId, w.Admin.CenterId, batch, retireFrom));
        var series = (await w.Service.FindScheduleSeriesAsync(new FindScheduleSeriesQuery(w.Admin.ProfileScopeId, w.Admin.CenterId, batch))).Value!;

        Assert.True(retired.Ok, retired.Error?.Message);
        Assert.Equal(10, retired.Value);
        Assert.Equal(ApplicationFailureCode.Conflict, again.Error!.Code);
        Assert.Equal(10, series.ActiveDates.Count);
        Assert.Equal(10, series.RetiredDates);
        Assert.Equal(dates.Take(10), series.ActiveDates.Select(d => d.Date));
        Assert.Single((await ListAsync(w)).Where(e => e.BatchId == other));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE recurso_id = @batch AND accion_codigo = 'SCHEDULE_SERIES_RETIRE' AND unidad_id = @unit",
            new { batch, unit = w.Admin.UnitId.Value }));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE accion_codigo = 'SCHEDULE_RETIRE' AND centro_id = @center", new { center = w.Admin.CenterId.Value }));
    }

    [Fact]
    public async Task RetirarSerie_DesdeUnaFechaPasada_SoloRetiraDeHoyEnAdelante_YSinFechasActivasEsUnConflicto()
    {
        var w = await CreateWorldAsync();
        var batch = Guid.NewGuid();
        Assert.True((await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Morning, batch: batch, dates: [Today, Today.AddDays(1), Today.AddDays(2)]))).Ok);

        var fromPast = await w.Service.RetireScheduleSeriesAsync(new RetireScheduleSeriesCommand(
            w.Admin.ProfileScopeId, w.Admin.CenterId, batch, Today.AddDays(-30)));
        var empty = await w.Service.RetireScheduleSeriesAsync(new RetireScheduleSeriesCommand(w.Admin.ProfileScopeId, w.Admin.CenterId, batch, Today.AddDays(-30)));

        Assert.Equal(3, fromPast.Value);
        Assert.Equal(ApplicationFailureCode.Conflict, empty.Error!.Code);
    }

    [Fact]
    public async Task Serie_ConEnfermeriaOtroAmbitoOLoteAjeno_ReturnsAccessDenied()
    {
        var w = await CreateWorldAsync();
        var other = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var batch = Guid.NewGuid();
        Assert.True((await w.Service.PlanShiftAsync(Plan(w, w.TeamA, w.Morning, batch: batch, dates: [Today.AddDays(2)]))).Ok);

        var find = new[]
        {
            (await BuildTurnos(w.Nurse1.ExternalSubject).FindScheduleSeriesAsync(new FindScheduleSeriesQuery(w.Nurse1.ProfileScopeId, w.Nurse1.CenterId, batch))).Error,
            (await BuildTurnos(other.ExternalSubject).FindScheduleSeriesAsync(new FindScheduleSeriesQuery(other.ProfileScopeId, other.CenterId, batch))).Error,
            (await w.Service.FindScheduleSeriesAsync(new FindScheduleSeriesQuery(w.Admin.ProfileScopeId, w.Admin.CenterId, Guid.NewGuid()))).Error,
        };
        var retire = new[]
        {
            (await BuildTurnos(w.Nurse1.ExternalSubject).RetireScheduleSeriesAsync(new RetireScheduleSeriesCommand(w.Nurse1.ProfileScopeId, w.Nurse1.CenterId, batch, Today))).Error,
            (await BuildTurnos(other.ExternalSubject).RetireScheduleSeriesAsync(new RetireScheduleSeriesCommand(other.ProfileScopeId, other.CenterId, batch, Today))).Error,
            (await BuildTurnos(other.ExternalSubject).RetireScheduleSeriesAsync(new RetireScheduleSeriesCommand(w.Admin.ProfileScopeId, w.Admin.CenterId, batch, Today))).Error,
        };

        Assert.All(find.Concat(retire), e => Assert.Equal(ApplicationFailureCode.AccessDenied, e!.Code));
        Assert.Single(await ListAsync(w));
    }
}
