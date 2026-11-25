using Dapper;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Attendance;
using ResidApp.Domain.Platform;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using Day = ResidApp.IntegrationTests.TestSupport.DayCenterFixture.Day;

namespace ResidApp.IntegrationTests;

/// <summary>
/// Los casos de uso de la asistencia del Centro de Día (ADR 0019, script 0115): quién puede hacer cada cosa, el interruptor, la lectura con lo que cada perfil no debe ver, las ausencias
/// programadas, la pauta y la auditoría solo con identificadores.
/// </summary>
public class AsistenciaCasosDeUsoTests
{
    private sealed class FixedIdentity(string subject) : ISessionIdentityProvider
    {
        public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) => Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(subject));
    }

    private sealed record Staff(SeededProfile Administracion, SeededProfile Auxiliar, SeededProfile Enfermeria, SeededProfile Medicina, SeededProfile Direccion);

    private static AttendanceApplicationService Service(SeededProfile profile) => new(
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedIdentity(profile.ExternalSubject), new SqlAttendanceRepository(TestDatabase.ConnectionFactory), TimeProvider.System);

    private static DateOnly Today => CenterTime.TodayAt(DateTimeOffset.UtcNow);

    private static async Task<(Day Center, Staff Staff)> CreateAsync(CenterServiceType type = CenterServiceType.CentroDeDia, bool enable = true)
    {
        var center = await DayCenterFixture.CreateAsync(type);
        async Task<SeededProfile> Add(SystemProfile profile) => await SeedFixture.AddProfileToCenterAsync(profile, CenterId.From(center.CenterId), UnitId.From(center.UnitId));
        var staff = new Staff(await Add(SystemProfile.Administracion), await Add(SystemProfile.Auxiliar), await Add(SystemProfile.Enfermeria), await Add(SystemProfile.Medicina), await Add(SystemProfile.DireccionClinica));
        if (enable)
        {
            var on = await Service(staff.Administracion).SetCenterAsync(new SetAttendanceCenterCommand(staff.Administracion.ProfileScopeId, staff.Administracion.CenterId, 0, true, true, true));
            Assert.True(on.Ok, on.Error?.Message);
        }

        return (center, staff);
    }

    private static RecordAttendanceCommand Mark(SeededProfile who, Day d, AttendanceState state, AttendanceCause? cause = null, string? note = null, DateOnly? date = null) =>
        new(who.ProfileScopeId, who.CenterId, ResidentId.From(d.ResidentId), date ?? Today, state, cause, note);

    private static async Task<AttendanceDayEntry> EntryAsync(SeededProfile who, Day d, DateOnly? date = null)
    {
        var list = await Service(who).ListDayAsync(who.ProfileScopeId, who.CenterId, date ?? Today);
        Assert.True(list.Ok, list.Error?.Message);
        return list.Value!.Single(e => e.ResidentId.Value == d.ResidentId);
    }

    [Fact]
    public async Task El_interruptor_empieza_apagado_solo_lo_cambia_Administracion_y_cada_cambio_exige_la_version_vista()
    {
        var (_, staff) = await CreateAsync(enable: false);
        var admin = Service(staff.Administracion);

        var initial = (await admin.ReadCenterAsync(staff.Administracion.ProfileScopeId, staff.Administracion.CenterId)).Value!;
        Assert.Equal((true, false, 0), (initial.DayCenter, initial.Active, initial.Number));
        Assert.False((await Service(staff.Enfermeria).ReadCenterAsync(staff.Enfermeria.ProfileScopeId, staff.Enfermeria.CenterId)).Value!.Active);
        Assert.Equal(ApplicationFailureCode.AccessDenied,
            (await Service(staff.Enfermeria).SetCenterAsync(new SetAttendanceCenterCommand(staff.Enfermeria.ProfileScopeId, staff.Enfermeria.CenterId, 0, true, true, true))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput,
            (await admin.SetCenterAsync(new SetAttendanceCenterCommand(staff.Administracion.ProfileScopeId, staff.Administracion.CenterId, 0, true, true, false))).Error!.Code);

        var on = await admin.SetCenterAsync(new SetAttendanceCenterCommand(staff.Administracion.ProfileScopeId, staff.Administracion.CenterId, 0, true, true, true));
        var stale = await admin.SetCenterAsync(new SetAttendanceCenterCommand(staff.Administracion.ProfileScopeId, staff.Administracion.CenterId, 0, false, false, false));
        var off = await admin.SetCenterAsync(new SetAttendanceCenterCommand(staff.Administracion.ProfileScopeId, staff.Administracion.CenterId, 1, false, false, false));

        Assert.Equal(1, on.Value);
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        Assert.Equal(2, off.Value);
    }

    [Fact]
    public async Task Una_residencia_no_ofrece_la_asistencia_y_un_centro_con_la_funcion_apagada_rechaza_toda_operacion()
    {
        var (residence, residenceStaff) = await CreateAsync(CenterServiceType.Residencia, enable: false);
        var state = (await Service(residenceStaff.Administracion).ReadCenterAsync(residenceStaff.Administracion.ProfileScopeId, residenceStaff.Administracion.CenterId)).Value!;
        var enabling = await Service(residenceStaff.Administracion).SetCenterAsync(
            new SetAttendanceCenterCommand(residenceStaff.Administracion.ProfileScopeId, residenceStaff.Administracion.CenterId, 0, true, true, true));
        var recording = await Service(residenceStaff.Auxiliar).RecordAsync(Mark(residenceStaff.Auxiliar, residence, AttendanceState.Asistio));

        Assert.False(state.DayCenter);
        Assert.False(enabling.Ok);
        Assert.Contains("no está activada", recording.Error!.Message);

        var (day, dayStaff) = await CreateAsync(enable: false);
        Assert.Contains("no está activada", (await Service(dayStaff.Auxiliar).RecordAsync(Mark(dayStaff.Auxiliar, day, AttendanceState.Asistio))).Error!.Message);
        // Leer sí se puede aunque esté apagada (apagarla no oculta lo anotado); lo que no se puede es anotar.
        Assert.True((await Service(dayStaff.Auxiliar).ListDayAsync(dayStaff.Auxiliar.ProfileScopeId, dayStaff.Auxiliar.CenterId, null)).Ok);
    }

    [Fact]
    public async Task Solo_Auxiliar_Enfermeria_y_Medicina_anotan_y_Direccion_no_lee()
    {
        var (day, staff) = await CreateAsync();

        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(staff.Administracion).RecordAsync(Mark(staff.Administracion, day, AttendanceState.Asistio))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(staff.Direccion).RecordAsync(Mark(staff.Direccion, day, AttendanceState.Asistio))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(staff.Direccion).ListDayAsync(staff.Direccion.ProfileScopeId, staff.Direccion.CenterId, null)).Error!.Code);
        Assert.True((await Service(staff.Auxiliar).RecordAsync(Mark(staff.Auxiliar, day, AttendanceState.Asistio))).Ok);
        Assert.True((await Service(staff.Medicina).RecordAsync(Mark(staff.Medicina, day, AttendanceState.NoAsistio, AttendanceCause.SinAviso))).Ok);
    }

    [Fact]
    public async Task Anotar_dos_veces_lo_mismo_no_duplica_y_corregir_es_una_version_nueva_con_su_auditoria_sin_valores()
    {
        var (day, staff) = await CreateAsync();
        var aux = Service(staff.Auxiliar);

        var first = await aux.RecordAsync(Mark(staff.Auxiliar, day, AttendanceState.Asistio));
        var repeated = await aux.RecordAsync(Mark(staff.Auxiliar, day, AttendanceState.Asistio));
        var corrected = await aux.RecordAsync(Mark(staff.Auxiliar, day, AttendanceState.NoAsistio, AttendanceCause.AvisoFamilia));

        Assert.Equal((1, 1, 2), (first.Value, repeated.Value, corrected.Value));
        var entry = await EntryAsync(staff.Auxiliar, day);
        Assert.Equal((AttendanceDayStatus.NoVino, 2, AttendanceCause.AvisoFamilia), (entry.Status, entry.Mark!.Number, entry.Mark.Cause));

        using var db = await TestDatabase.ConnectionFactory.OpenAsync();
        var actions = (await db.QueryAsync<string>(
            "SELECT accion_codigo FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE cuenta_id = @a AND tipo_recurso = 'ATTENDANCE' ORDER BY ocurrido_en", new { a = staff.Auxiliar.AccountId.Value })).ToList();
        Assert.Equal(["ATTENDANCE_MARK", "ATTENDANCE_CORRECT"], actions);
    }

    [Fact]
    public async Task Una_fecha_futura_se_rechaza_y_la_nota_de_otro_motivo_no_la_ve_Administracion()
    {
        var (day, staff) = await CreateAsync();
        var aux = Service(staff.Auxiliar);

        Assert.Equal(ApplicationFailureCode.InvalidInput, (await aux.RecordAsync(Mark(staff.Auxiliar, day, AttendanceState.Asistio, date: Today.AddDays(1)))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await aux.RecordAsync(Mark(staff.Auxiliar, day, AttendanceState.NoAsistio, AttendanceCause.SinAviso, "no cabe aquí"))).Error!.Code);
        Assert.True((await aux.RecordAsync(Mark(staff.Auxiliar, day, AttendanceState.NoAsistio, AttendanceCause.Otro, "cambio de domicilio"))).Ok);

        Assert.Equal("cambio de domicilio", (await EntryAsync(staff.Auxiliar, day)).Mark!.Note);
        Assert.Equal("cambio de domicilio", (await EntryAsync(staff.Enfermeria, day)).Mark!.Note);
        var admin = await EntryAsync(staff.Administracion, day);
        Assert.Equal(AttendanceCause.Otro, admin.Mark!.Cause);
        Assert.Null(admin.Mark.Note);
    }

    [Fact]
    public async Task Quien_es_de_otro_centro_no_ve_ni_anota_y_un_residente_suspendido_no_sale_en_la_lista()
    {
        var (day, staff) = await CreateAsync();
        var (_, otherStaff) = await CreateAsync();

        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(otherStaff.Auxiliar).RecordAsync(Mark(otherStaff.Auxiliar, day, AttendanceState.Asistio))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied,
            (await Service(otherStaff.Auxiliar).RecordAsync(new RecordAttendanceCommand(staff.Auxiliar.ProfileScopeId, staff.Auxiliar.CenterId, ResidentId.From(day.ResidentId), Today, AttendanceState.Asistio, null, null))).Error!.Code);
        Assert.DoesNotContain(
            (await Service(otherStaff.Auxiliar).ListDayAsync(otherStaff.Auxiliar.ProfileScopeId, otherStaff.Auxiliar.CenterId, null)).Value!, e => e.ResidentId.Value == day.ResidentId);

        using (var db = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await db.ExecuteAsync("""
                INSERT INTO dbo.suspensiones_residente (id, residente_id, centro_id, unidad_id, iniciada_por_cuenta_id, iniciada_en)
                VALUES (NEWID(), @ResidentId, @CenterId, @UnitId, @AccountId, SYSUTCDATETIME())
                """, new { day.ResidentId, day.CenterId, day.UnitId, day.AccountId });
        }

        Assert.DoesNotContain((await Service(staff.Auxiliar).ListDayAsync(staff.Auxiliar.ProfileScopeId, staff.Auxiliar.CenterId, null)).Value!, e => e.ResidentId.Value == day.ResidentId);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(staff.Auxiliar).RecordAsync(Mark(staff.Auxiliar, day, AttendanceState.Asistio))).Error!.Code);
    }

    [Fact]
    public async Task La_pauta_la_fijan_Administracion_y_Enfermeria_con_version_y_marca_los_dias_esperados()
    {
        var (day, staff) = await CreateAsync();
        var resident = ResidentId.From(day.ResidentId);
        var monday = Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7));
        var mondayWednesdayFriday = AttendanceRules.Mask(DayOfWeek.Monday) | AttendanceRules.Mask(DayOfWeek.Wednesday) | AttendanceRules.Mask(DayOfWeek.Friday);
        SetAttendancePatternCommand Pattern(SeededProfile who, int version, int days) => new(who.ProfileScopeId, who.CenterId, resident, version, days);

        // Sin pauta, se espera a todos (no se sabe).
        var before = await EntryAsync(staff.Auxiliar, day);
        Assert.False(before.HasPattern);
        Assert.True(before.Expected);

        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(staff.Auxiliar).SetPatternAsync(Pattern(staff.Auxiliar, 0, 21))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(staff.Medicina).SetPatternAsync(Pattern(staff.Medicina, 0, 21))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await Service(staff.Administracion).SetPatternAsync(Pattern(staff.Administracion, 0, 0))).Error!.Code);
        Assert.Equal(1, (await Service(staff.Administracion).SetPatternAsync(Pattern(staff.Administracion, 0, mondayWednesdayFriday))).Value);
        Assert.Equal(1, (await Service(staff.Enfermeria).SetPatternAsync(Pattern(staff.Enfermeria, 1, mondayWednesdayFriday))).Value);
        Assert.Equal(ApplicationFailureCode.Conflict, (await Service(staff.Enfermeria).SetPatternAsync(Pattern(staff.Enfermeria, 0, 31))).Error!.Code);
        Assert.Equal(2, (await Service(staff.Enfermeria).SetPatternAsync(Pattern(staff.Enfermeria, 1, 31))).Value);
        Assert.Equal(3, (await Service(staff.Enfermeria).SetPatternAsync(Pattern(staff.Enfermeria, 2, mondayWednesdayFriday))).Value);

        // El martes (de la semana pasada) no se espera con L-X-V.
        var tuesday = monday.AddDays(-6);
        var read = (await Service(staff.Auxiliar).ReadPatternAsync(staff.Auxiliar.ProfileScopeId, staff.Auxiliar.CenterId, resident)).Value!;
        Assert.Equal(mondayWednesdayFriday, read.DaysMask);
        Assert.False(AttendanceRules.Expects(read.DaysMask, tuesday));
    }

    [Fact]
    public async Task Una_ausencia_programada_la_registran_Administracion_y_Enfermeria_y_el_motivo_solo_lo_leen_Enfermeria_y_Medicina()
    {
        var (day, staff) = await CreateAsync();
        var resident = ResidentId.From(day.ResidentId);
        RegisterAbsenceCommand Register(SeededProfile who, DateOnly from, DateOnly to) => new(who.ProfileScopeId, who.CenterId, resident, from, to, AbsenceReason.IngresoHospitalario);

        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(staff.Auxiliar).RegisterAbsenceAsync(Register(staff.Auxiliar, Today, Today))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(staff.Medicina).RegisterAbsenceAsync(Register(staff.Medicina, Today, Today))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await Service(staff.Administracion).RegisterAbsenceAsync(Register(staff.Administracion, Today, Today.AddDays(90)))).Error!.Code);
        var registered = await Service(staff.Administracion).RegisterAbsenceAsync(Register(staff.Administracion, Today, Today.AddDays(10)));
        Assert.True(registered.Ok, registered.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, (await Service(staff.Enfermeria).RegisterAbsenceAsync(Register(staff.Enfermeria, Today.AddDays(5), Today.AddDays(6)))).Error!.Code);

        async Task<ScheduledAbsence> Read(SeededProfile who) =>
            (await Service(who).ListAbsencesAsync(who.ProfileScopeId, who.CenterId, resident)).Value!.Single();
        Assert.Null((await Read(staff.Administracion)).Reason);
        Assert.Null((await Read(staff.Auxiliar)).Reason);
        Assert.Equal(AbsenceReason.IngresoHospitalario, (await Read(staff.Enfermeria)).Reason);
        Assert.Equal(AbsenceReason.IngresoHospitalario, (await Read(staff.Medicina)).Reason);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(staff.Direccion).ListAbsencesAsync(staff.Direccion.ProfileScopeId, staff.Direccion.CenterId, resident)).Error!.Code);

        // La lista del día la muestra como ausencia programada; lo anotado manda sobre ella.
        Assert.Equal(AttendanceDayStatus.AusenciaProgramada, (await EntryAsync(staff.Auxiliar, day)).Status);
        Assert.True((await Service(staff.Auxiliar).RecordAsync(Mark(staff.Auxiliar, day, AttendanceState.Asistio))).Ok);
        Assert.Equal(AttendanceDayStatus.Vino, (await EntryAsync(staff.Auxiliar, day)).Status);
    }

    [Fact]
    public async Task Acortar_y_anular_una_ausencia_siguen_sus_reglas_y_dejan_auditoria_sin_valores()
    {
        var (day, staff) = await CreateAsync();
        var resident = ResidentId.From(day.ResidentId);
        var admin = Service(staff.Administracion);
        var id = (await admin.RegisterAbsenceAsync(new RegisterAbsenceCommand(staff.Administracion.ProfileScopeId, staff.Administracion.CenterId, resident, Today, Today.AddDays(20), AbsenceReason.Vacaciones))).Value;
        AbsenceChangeCommand Annul(SeededProfile who) => new(who.ProfileScopeId, who.CenterId, id);
        ShortenAbsenceCommand Shorten(SeededProfile who, DateOnly to) => new(who.ProfileScopeId, who.CenterId, id, to);

        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(staff.Auxiliar).AnnulAbsenceAsync(Annul(staff.Auxiliar))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await admin.ShortenAbsenceAsync(Shorten(staff.Administracion, Today.AddDays(20)))).Error!.Code);
        Assert.True((await admin.ShortenAbsenceAsync(Shorten(staff.Administracion, Today.AddDays(5)))).Ok);
        Assert.Equal(Today.AddDays(5), (await admin.ListAbsencesAsync(staff.Administracion.ProfileScopeId, staff.Administracion.CenterId, resident)).Value!.Single().To);
        Assert.True((await admin.AnnulAbsenceAsync(Annul(staff.Administracion))).Ok);
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await admin.AnnulAbsenceAsync(Annul(staff.Administracion))).Error!.Code);
        Assert.Empty((await admin.ListAbsencesAsync(staff.Administracion.ProfileScopeId, staff.Administracion.CenterId, resident)).Value!);
        Assert.Equal(AttendanceDayStatus.SinAnotar, (await EntryAsync(staff.Auxiliar, day)).Status);

        using var db = await TestDatabase.ConnectionFactory.OpenAsync();
        var actions = (await db.QueryAsync<string>(
            "SELECT accion_codigo FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE cuenta_id = @a AND tipo_recurso = 'ATTENDANCE' ORDER BY ocurrido_en", new { a = staff.Administracion.AccountId.Value })).ToList();
        Assert.Equal(["ATTENDANCE_CENTER_ON", "ABSENCE_REGISTER", "ABSENCE_SHORTEN", "ABSENCE_ANNUL"], actions);
    }

    private static async Task BackdateAsync(Day day, int days)
    {
        using var db = await TestDatabase.ConnectionFactory.OpenAsync();
        await db.ExecuteAsync("UPDATE dbo.residentes SET creado_en = DATEADD(DAY, -@days, creado_en) WHERE id = @id", new { days, id = day.ResidentId });
    }

    private static async Task<AttendanceFollowUp> FollowUpAsync(SeededProfile who)
    {
        var result = await Service(who).ListFollowUpAsync(who.ProfileScopeId, who.CenterId);
        Assert.True(result.Ok, result.Error?.Message);
        return result.Value!;
    }

    [Fact]
    public async Task La_lista_de_seguimiento_es_solo_de_Administracion_cuenta_las_faltas_seguidas_con_su_causa_y_deja_auditoria_sin_nombres()
    {
        var (day, staff) = await CreateAsync();
        await BackdateAsync(day, 10);
        var nurse = Service(staff.Enfermeria);
        foreach (var back in new[] { 3, 2 })
        {
            Assert.True((await nurse.RecordAsync(Mark(staff.Enfermeria, day, AttendanceState.NoAsistio, AttendanceCause.AvisoFamilia, date: Today.AddDays(-back)))).Ok);
        }

        // Con dos días no sale; los demás días de la ventana siguen sin anotar, pero la cadena de faltas no los cuenta.
        Assert.Empty((await FollowUpAsync(staff.Administracion)).Absences);

        Assert.True((await nurse.RecordAsync(Mark(staff.Enfermeria, day, AttendanceState.NoAsistio, AttendanceCause.SinAviso, date: Today.AddDays(-1)))).Ok);
        // Hoy no está cerrado: anotarlo no suma.
        Assert.True((await nurse.RecordAsync(Mark(staff.Enfermeria, day, AttendanceState.NoAsistio, AttendanceCause.Otro, "una nota", Today))).Ok);

        var entry = Assert.Single((await FollowUpAsync(staff.Administracion)).Absences);
        Assert.Equal(day.ResidentId, entry.ResidentId.Value);
        Assert.Equal(3, entry.Days);
        Assert.Equal([AttendanceCause.AvisoFamilia, AttendanceCause.SinAviso], entry.Causes.OrderBy(c => c));
        Assert.Null(entry.LastCame);

        foreach (var other in new[] { staff.Auxiliar, staff.Enfermeria, staff.Medicina, staff.Direccion })
        {
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(other).ListFollowUpAsync(other.ProfileScopeId, other.CenterId)).Error!.Code);
        }

        using var db = await TestDatabase.ConnectionFactory.OpenAsync();
        var opened = (await db.QueryAsync<(string Action, Guid? ResidentId)>(
            "SELECT accion_codigo, residente_id FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE cuenta_id = @a AND accion_codigo = 'ATTENDANCE_FOLLOWUP_OPEN'", new { a = staff.Administracion.AccountId.Value })).ToList();
        Assert.NotEmpty(opened);
        Assert.All(opened, o => Assert.Null(o.ResidentId));
    }

    [Fact]
    public async Task Un_dia_que_vino_o_una_ausencia_programada_sacan_a_la_persona_de_la_lista_y_un_dia_sin_anotar_va_aparte()
    {
        var (day, staff) = await CreateAsync();
        await BackdateAsync(day, 10);
        var nurse = Service(staff.Enfermeria);
        foreach (var back in new[] { 4, 3, 2, 1 })
        {
            Assert.True((await nurse.RecordAsync(Mark(staff.Enfermeria, day, AttendanceState.NoAsistio, AttendanceCause.SinAviso, date: Today.AddDays(-back)))).Ok);
        }

        Assert.Equal(4, Assert.Single((await FollowUpAsync(staff.Administracion)).Absences).Days);

        // Ayer vino: la cuenta termina y la persona sale.
        Assert.True((await nurse.RecordAsync(Mark(staff.Enfermeria, day, AttendanceState.Asistio, date: Today.AddDays(-1)))).Ok);
        Assert.Empty((await FollowUpAsync(staff.Administracion)).Absences);

        // Una persona sin nada anotado ayer sale aparte, con sus días sin anotar, y no como falta.
        var (other, otherStaff) = await CreateAsync();
        await BackdateAsync(other, 5);
        var followUp = await FollowUpAsync(otherStaff.Administracion);
        Assert.Empty(followUp.Absences);
        Assert.Equal(5, Assert.Single(followUp.Unmarked).Days);

        // Con una ausencia programada que cubre esos días, tampoco sale como días sin anotar: la ausencia es lo esperado.
        Assert.True((await Service(otherStaff.Administracion).RegisterAbsenceAsync(new RegisterAbsenceCommand(
            otherStaff.Administracion.ProfileScopeId, otherStaff.Administracion.CenterId, ResidentId.From(other.ResidentId), Today.AddDays(-5), Today.AddDays(3), AbsenceReason.Vacaciones))).Ok);
        followUp = await FollowUpAsync(otherStaff.Administracion);
        Assert.Empty(followUp.Absences);
        Assert.Empty(followUp.Unmarked);
    }

    [Fact]
    public async Task La_lista_de_seguimiento_no_mira_otro_centro_ni_a_una_persona_con_pauta_que_no_toca()
    {
        var (day, staff) = await CreateAsync();
        await BackdateAsync(day, 10);
        var nurse = Service(staff.Enfermeria);
        foreach (var back in new[] { 3, 2, 1 })
        {
            Assert.True((await nurse.RecordAsync(Mark(staff.Enfermeria, day, AttendanceState.NoAsistio, AttendanceCause.SinAviso, date: Today.AddDays(-back)))).Ok);
        }

        var (_, otherStaff) = await CreateAsync();
        Assert.DoesNotContain((await FollowUpAsync(otherStaff.Administracion)).Absences, a => a.ResidentId.Value == day.ResidentId);

        // Con una pauta que no incluye ninguno de esos tres días, ya no suman: quedan los días que sí tocaban, sin anotar.
        var skipped = Enumerable.Range(1, 3).Select(back => Today.AddDays(-back).DayOfWeek).Distinct().ToHashSet();
        var mask = Enum.GetValues<DayOfWeek>().Where(d => !skipped.Contains(d)).Aggregate(0, (all, d) => all | AttendanceRules.Mask(d));
        Assert.True((await Service(staff.Enfermeria).SetPatternAsync(new SetAttendancePatternCommand(staff.Enfermeria.ProfileScopeId, staff.Enfermeria.CenterId, ResidentId.From(day.ResidentId), 0, mask))).Ok);
        var followUp = await FollowUpAsync(staff.Administracion);
        Assert.DoesNotContain(followUp.Absences, a => a.ResidentId.Value == day.ResidentId);
    }

    [Fact]
    public async Task Apagar_la_asistencia_impide_anotar_dias_nuevos_pero_no_oculta_lo_ya_anotado()
    {
        var (day, staff) = await CreateAsync();
        var resident = ResidentId.From(day.ResidentId);
        Assert.True((await Service(staff.Auxiliar).RecordAsync(Mark(staff.Auxiliar, day, AttendanceState.NoAsistio, AttendanceCause.Otro, "una nota"))).Ok);
        Assert.True((await Service(staff.Enfermeria).SetPatternAsync(new SetAttendancePatternCommand(staff.Enfermeria.ProfileScopeId, staff.Enfermeria.CenterId, resident, 0, 21))).Ok);
        Assert.True((await Service(staff.Administracion).RegisterAbsenceAsync(new RegisterAbsenceCommand(
            staff.Administracion.ProfileScopeId, staff.Administracion.CenterId, resident, Today.AddDays(2), Today.AddDays(4), AbsenceReason.Vacaciones))).Ok);

        var off = await Service(staff.Administracion).SetCenterAsync(new SetAttendanceCenterCommand(staff.Administracion.ProfileScopeId, staff.Administracion.CenterId, 1, false, false, false));
        Assert.True(off.Ok, off.Error?.Message);

        // Se sigue leyendo todo, y cada perfil ve lo suyo (la nota, quien puede leerla).
        var entry = await EntryAsync(staff.Auxiliar, day);
        Assert.Equal((AttendanceDayStatus.NoVino, "una nota"), (entry.Status, entry.Mark!.Note));
        Assert.Null((await EntryAsync(staff.Administracion, day)).Mark!.Note);
        Assert.Equal(21, (await Service(staff.Enfermeria).ReadPatternAsync(staff.Enfermeria.ProfileScopeId, staff.Enfermeria.CenterId, resident)).Value!.DaysMask);
        Assert.Single((await Service(staff.Enfermeria).ListAbsencesAsync(staff.Enfermeria.ProfileScopeId, staff.Enfermeria.CenterId, null)).Value!);
        Assert.True((await Service(staff.Administracion).ListFollowUpAsync(staff.Administracion.ProfileScopeId, staff.Administracion.CenterId)).Ok);

        // Pero no se anota ni se cambia nada nuevo.
        Assert.Contains("no está activada", (await Service(staff.Auxiliar).RecordAsync(Mark(staff.Auxiliar, day, AttendanceState.Asistio, date: Today))).Error!.Message);
        Assert.False((await Service(staff.Enfermeria).SetPatternAsync(new SetAttendancePatternCommand(staff.Enfermeria.ProfileScopeId, staff.Enfermeria.CenterId, resident, 1, 127))).Ok);
        Assert.False((await Service(staff.Administracion).RegisterAbsenceAsync(new RegisterAbsenceCommand(
            staff.Administracion.ProfileScopeId, staff.Administracion.CenterId, resident, Today.AddDays(10), Today.AddDays(11), AbsenceReason.Otro))).Ok);
    }

    [Fact]
    public async Task La_lista_de_seguimiento_avisa_de_quien_no_tiene_pauta_y_cuenta_desde_que_existe_la_ficha()
    {
        var (day, staff) = await CreateAsync();
        await BackdateAsync(day, 5);
        using (var db = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            // Ingreso mucho antes de crear la ficha (por ejemplo, importado de otro programa): la base no deja anotar esos días y la lista no los cuenta.
            await db.ExecuteAsync("UPDATE dbo.residentes SET fecha_ingreso = DATEADD(DAY, -30, CAST(SYSUTCDATETIME() AS DATE)) WHERE id = @id", new { id = day.ResidentId });
        }

        var followUp = await FollowUpAsync(staff.Administracion);
        Assert.Equal(day.ResidentId, Assert.Single(followUp.WithoutPattern).ResidentId.Value);
        Assert.Equal(5, Assert.Single(followUp.Unmarked).Days);

        Assert.True((await Service(staff.Enfermeria).SetPatternAsync(new SetAttendancePatternCommand(
            staff.Enfermeria.ProfileScopeId, staff.Enfermeria.CenterId, ResidentId.From(day.ResidentId), 0, AttendanceRules.AllDays))).Ok);
        Assert.Empty((await FollowUpAsync(staff.Administracion)).WithoutPattern);
    }

    [Fact]
    public async Task Un_residente_suspendido_o_de_baja_no_sale_en_la_lista_de_seguimiento_ni_en_las_ausencias()
    {
        var (day, staff) = await CreateAsync();
        await BackdateAsync(day, 5);
        var resident = ResidentId.From(day.ResidentId);
        Assert.True((await Service(staff.Administracion).RegisterAbsenceAsync(new RegisterAbsenceCommand(
            staff.Administracion.ProfileScopeId, staff.Administracion.CenterId, resident, Today.AddDays(2), Today.AddDays(3), AbsenceReason.Vacaciones))).Ok);
        Assert.Single((await FollowUpAsync(staff.Administracion)).Unmarked);

        Guid suspension;
        using (var db = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            suspension = Guid.NewGuid();
            await db.ExecuteAsync("""
                INSERT INTO dbo.suspensiones_residente (id, residente_id, centro_id, unidad_id, iniciada_por_cuenta_id, iniciada_en)
                VALUES (@suspension, @ResidentId, @CenterId, @UnitId, @AccountId, SYSUTCDATETIME())
                """, new { suspension, day.ResidentId, day.CenterId, day.UnitId, day.AccountId });
        }

        Assert.Empty((await FollowUpAsync(staff.Administracion)).Unmarked);
        Assert.Empty((await FollowUpAsync(staff.Administracion)).WithoutPattern);
        Assert.Empty((await Service(staff.Enfermeria).ListAbsencesAsync(staff.Enfermeria.ProfileScopeId, staff.Enfermeria.CenterId, null)).Value!);

        // De baja: otra persona del mismo centro, con la ficha antigua y sin nada anotado.
        var discharged = await DayCenterFixture.AddResidentAsync(day.AccountId, day.CenterId, day.UnitId, "Persona de baja (ficticia)");
        await BackdateAsync(day with { ResidentId = discharged }, 5);
        Assert.Contains((await FollowUpAsync(staff.Administracion)).WithoutPattern, w => w.ResidentId.Value == discharged);
        using (var db = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await db.ExecuteAsync("UPDATE dbo.residentes SET estado = 'INACTIVE', motivo_inactivacion = N'baja de prueba', inactivado_en = SYSUTCDATETIME(), inactivado_por_cuenta_id = @account, inactivado_por_perfil = 'ADMINISTRACION' WHERE id = @id", new { id = discharged, account = day.AccountId });
        }

        var afterDischarge = await FollowUpAsync(staff.Administracion);
        Assert.DoesNotContain(afterDischarge.Unmarked, u => u.ResidentId.Value == discharged);
        Assert.DoesNotContain(afterDischarge.WithoutPattern, w => w.ResidentId.Value == discharged);
        Assert.DoesNotContain((await Service(staff.Enfermeria).ListDayAsync(staff.Enfermeria.ProfileScopeId, staff.Enfermeria.CenterId, null)).Value!, e => e.ResidentId.Value == discharged);
    }

    [Fact]
    public async Task El_Auxiliar_corrige_hasta_7_dias_y_Enfermeria_y_Medicina_mas_atras_por_el_caso_de_uso()
    {
        var (day, staff) = await CreateAsync();
        await BackdateAsync(day, 40);

        Assert.True((await Service(staff.Auxiliar).RecordAsync(Mark(staff.Auxiliar, day, AttendanceState.Asistio, date: Today.AddDays(-7)))).Ok);
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await Service(staff.Auxiliar).RecordAsync(Mark(staff.Auxiliar, day, AttendanceState.Asistio, date: Today.AddDays(-8)))).Error!.Code);
        Assert.True((await Service(staff.Enfermeria).RecordAsync(Mark(staff.Enfermeria, day, AttendanceState.Asistio, date: Today.AddDays(-30)))).Ok);
        Assert.True((await Service(staff.Medicina).RecordAsync(Mark(staff.Medicina, day, AttendanceState.Asistio, date: Today.AddDays(-31)))).Ok);
        // Nunca anterior al alta (la ficha existe desde hace 40 días) ni futura.
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await Service(staff.Enfermeria).RecordAsync(Mark(staff.Enfermeria, day, AttendanceState.Asistio, date: Today.AddDays(-41)))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await Service(staff.Enfermeria).RecordAsync(Mark(staff.Enfermeria, day, AttendanceState.Asistio, date: Today.AddDays(1)))).Error!.Code);
    }

    [Fact]
    public async Task Un_cambio_de_pauta_no_reescribe_el_pasado_de_la_lista_pero_una_fecha_pasada_si_y_queda_a_la_vista()
    {
        var all = AttendanceRules.AllDays;
        var skipped = Enumerable.Range(1, 3).Select(back => Today.AddDays(-back).DayOfWeek).ToHashSet();
        var other = AttendanceRules.Mask(Enum.GetValues<DayOfWeek>().First(d => !skipped.Contains(d)));
        async Task<(Day Day, Staff Staff)> WithThreeAbsencesAsync()
        {
            var (d, s) = await CreateAsync();
            await BackdateAsync(d, 10);
            foreach (var back in new[] { 3, 2, 1 })
            {
                Assert.True((await Service(s.Enfermeria).RecordAsync(Mark(s.Enfermeria, d, AttendanceState.NoAsistio, AttendanceCause.SinAviso, date: Today.AddDays(-back)))).Ok);
            }

            return (d, s);
        }

        SetAttendancePatternCommand Pattern(SeededProfile who, Day d, int version, int days, DateOnly? from = null) => new(who.ProfileScopeId, who.CenterId, ResidentId.From(d.ResidentId), version, days, from);

        // Un cambio con efecto desde hoy no toca lo anterior: los tres días de falta siguen contando con la pauta de antes (todos los días).
        var (day, staff) = await WithThreeAbsencesAsync();
        var nurse = Service(staff.Enfermeria);
        Assert.True((await nurse.SetPatternAsync(Pattern(staff.Enfermeria, day, 0, all))).Ok);
        Assert.True((await nurse.SetPatternAsync(Pattern(staff.Enfermeria, day, 1, other))).Ok);
        Assert.Equal(3, Assert.Single((await FollowUpAsync(staff.Administracion)).Absences).Days);
        // Una fecha anterior a la pauta que está en vigor (la de hoy) se rechaza, y también una a más de 90 días.
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await nurse.SetPatternAsync(Pattern(staff.Enfermeria, day, 2, all, Today.AddDays(-1)))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await nurse.SetPatternAsync(Pattern(staff.Enfermeria, day, 2, all, Today.AddDays(91)))).Error!.Code);

        // Con efecto desde hace tres días (la en vigor es la de «desde siempre»), esos días ya no tocaban y la persona sale de la lista.
        var (retroDay, retroStaff) = await WithThreeAbsencesAsync();
        var retroNurse = Service(retroStaff.Enfermeria);
        Assert.True((await retroNurse.SetPatternAsync(Pattern(retroStaff.Enfermeria, retroDay, 0, all))).Ok);
        var retro = await retroNurse.SetPatternAsync(Pattern(retroStaff.Enfermeria, retroDay, 1, other, Today.AddDays(-3)));
        Assert.True(retro.Ok, retro.Error?.Message);
        Assert.Empty((await FollowUpAsync(retroStaff.Administracion)).Absences);

        // El historial, completo para Administración, deja ver la fecha de efecto y cuándo se anotó (con efecto anterior al registro).
        var history = (await Service(retroStaff.Administracion).ReadPatternAsync(retroStaff.Administracion.ProfileScopeId, retroStaff.Administracion.CenterId, ResidentId.From(retroDay.ResidentId))).Value!;
        Assert.Equal([2, 1], history.Versions.Select(v => v.Number).ToArray());
        Assert.Equal(Today.AddDays(-3), history.Versions[0].From);
        Assert.True(history.Versions[0].From < history.Versions[0].RegisteredOn);
        Assert.Equal(AttendanceRules.FromTheBeginning, history.Versions[1].From);
    }

    [Fact]
    public async Task Una_pauta_futura_puesta_por_error_se_sustituye_con_otra_y_el_Auxiliar_solo_ve_la_en_vigor_y_las_futuras()
    {
        var (day, staff) = await CreateAsync();
        var resident = ResidentId.From(day.ResidentId);
        var nurse = Service(staff.Enfermeria);
        SetAttendancePatternCommand Pattern(int version, int days, DateOnly? from = null, bool replace = false) =>
            new(staff.Enfermeria.ProfileScopeId, staff.Enfermeria.CenterId, resident, version, days, from, replace);
        Assert.True((await nurse.SetPatternAsync(Pattern(0, 21))).Ok);
        // Por error, una pauta que empieza dentro de 80 días; se corrige con otra que empieza dentro de 5 (la versión en vigor sigue siendo la primera).
        Assert.True((await nurse.SetPatternAsync(Pattern(1, 31, Today.AddDays(80)))).Ok);
        // Sin confirmar no se sustituye: ni empezando antes, ni el mismo día, ni guardando sin tocar la fecha (hoy) los días que están en vigor.
        foreach (var attempt in new[] { Pattern(2, 63, Today.AddDays(5)), Pattern(2, 63, Today.AddDays(80)), Pattern(2, 21) })
        {
            Assert.Equal(ApplicationFailureCode.Conflict, (await nurse.SetPatternAsync(attempt)).Error!.Code);
        }

        Assert.Equal(2, (await nurse.ReadPatternAsync(staff.Enfermeria.ProfileScopeId, staff.Enfermeria.CenterId, resident)).Value!.Number);
        Assert.True((await nurse.SetPatternAsync(Pattern(2, 63, Today.AddDays(5), replace: true))).Ok);

        Assert.Equal(21, (await nurse.ReadPatternAsync(staff.Enfermeria.ProfileScopeId, staff.Enfermeria.CenterId, resident)).Value!.DaysMask);
        var auxiliary = (await Service(staff.Auxiliar).ReadPatternAsync(staff.Auxiliar.ProfileScopeId, staff.Auxiliar.CenterId, resident)).Value!;
        Assert.Equal(3, auxiliary.Number);
        Assert.Equal(21, auxiliary.DaysMask);
        // La primera (en vigor) y la futura que sigue en pie: la que quedó sustituida (la de dentro de 80 días) ya no es un próximo cambio.
        Assert.Equal([3, 1], auxiliary.Versions.Select(v => v.Number).ToArray());
        var admin = (await Service(staff.Administracion).ReadPatternAsync(staff.Administracion.ProfileScopeId, staff.Administracion.CenterId, resident)).Value!;
        Assert.All(admin.Versions, v => Assert.True(v.RegisteredBy is SystemProfile.Enfermeria));
        // El historial completo conserva la sustituida.
        Assert.Equal([3, 2, 1], admin.Versions.Select(v => v.Number).ToArray());
    }
}
