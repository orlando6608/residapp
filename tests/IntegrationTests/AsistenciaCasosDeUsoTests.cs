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
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(staff.Enfermeria).ReadCenterAsync(staff.Enfermeria.ProfileScopeId, staff.Enfermeria.CenterId)).Error!.Code);
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
        Assert.False((await Service(dayStaff.Auxiliar).ListDayAsync(dayStaff.Auxiliar.ProfileScopeId, dayStaff.Auxiliar.CenterId, null)).Ok);
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
}
