using ResidApp.Domain.Attendance;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>Las reglas de la asistencia del Centro de Día que dependen del perfil y de la fecha (ADR 0019). Los valores del tope (80, 7, 90, 3) son propuestos y sin validar por CJ.</summary>
public class AttendanceRulesTests
{
    [Fact]
    public void Vino_no_lleva_causa_y_no_vino_la_exige()
    {
        Assert.Null(AttendanceRules.Validate(AttendanceState.Asistio, null, null));
        Assert.Throws<DomainValidationException>(() => AttendanceRules.Validate(AttendanceState.Asistio, AttendanceCause.SinAviso, null));
        Assert.Throws<DomainValidationException>(() => AttendanceRules.Validate(AttendanceState.NoAsistio, null, null));
        Assert.Null(AttendanceRules.Validate(AttendanceState.NoAsistio, AttendanceCause.AvisoFamilia, null));
    }

    [Fact]
    public void La_nota_solo_acompana_a_otro_motivo_de_una_linea_y_80_caracteres_y_se_limpia()
    {
        Assert.Equal("cambio de domicilio", AttendanceRules.Validate(AttendanceState.NoAsistio, AttendanceCause.Otro, "  cambio de domicilio  "));
        Assert.Null(AttendanceRules.Validate(AttendanceState.NoAsistio, AttendanceCause.Otro, "   "));
        Assert.Equal(new string('x', 80), AttendanceRules.Validate(AttendanceState.NoAsistio, AttendanceCause.Otro, new string('x', 80)));
        Assert.Throws<DomainValidationException>(() => AttendanceRules.Validate(AttendanceState.NoAsistio, AttendanceCause.Otro, new string('x', 81)));
        Assert.Throws<DomainValidationException>(() => AttendanceRules.Validate(AttendanceState.NoAsistio, AttendanceCause.Otro, "dos\nlíneas"));
        Assert.Throws<DomainValidationException>(() => AttendanceRules.Validate(AttendanceState.NoAsistio, AttendanceCause.SinAviso, "una nota"));
    }

    [Fact]
    public void Auxiliar_anota_hasta_7_dias_atras_y_Enfermeria_y_Medicina_sin_tope_pero_nunca_en_el_futuro()
    {
        var today = new DateOnly(2026, 11, 25);

        AttendanceRules.ValidateDate(SystemProfile.Auxiliar, today, today);
        AttendanceRules.ValidateDate(SystemProfile.Auxiliar, today.AddDays(-7), today);
        Assert.Throws<DomainValidationException>(() => AttendanceRules.ValidateDate(SystemProfile.Auxiliar, today.AddDays(-8), today));
        AttendanceRules.ValidateDate(SystemProfile.Enfermeria, today.AddDays(-60), today);
        AttendanceRules.ValidateDate(SystemProfile.Medicina, today.AddDays(-60), today);
        foreach (var profile in new[] { SystemProfile.Auxiliar, SystemProfile.Enfermeria, SystemProfile.Medicina })
        {
            Assert.Throws<DomainValidationException>(() => AttendanceRules.ValidateDate(profile, today.AddDays(1), today));
        }
    }

    [Fact]
    public void Quien_anota_quien_lee_y_quien_ve_el_motivo_de_una_ausencia()
    {
        Assert.True(AttendanceRules.CanRecord(SystemProfile.Auxiliar));
        Assert.True(AttendanceRules.CanRecord(SystemProfile.Enfermeria));
        Assert.True(AttendanceRules.CanRecord(SystemProfile.Medicina));
        Assert.False(AttendanceRules.CanRecord(SystemProfile.Administracion));
        Assert.False(AttendanceRules.CanRecord(SystemProfile.DireccionClinica));
        Assert.True(AttendanceRules.CanRead(SystemProfile.Administracion));
        Assert.False(AttendanceRules.CanRead(SystemProfile.DireccionClinica));
        Assert.False(AttendanceRules.CanRead(SystemProfile.Familiar));
        Assert.True(AttendanceRules.CanManageAbsences(SystemProfile.Administracion));
        Assert.True(AttendanceRules.CanManageAbsences(SystemProfile.Enfermeria));
        Assert.False(AttendanceRules.CanManageAbsences(SystemProfile.Auxiliar));
        Assert.True(AttendanceRules.CanReadAbsenceReason(SystemProfile.Enfermeria));
        Assert.True(AttendanceRules.CanReadAbsenceReason(SystemProfile.Medicina));
        Assert.False(AttendanceRules.CanReadAbsenceReason(SystemProfile.Administracion));
        Assert.False(AttendanceRules.CanReadAbsenceReason(SystemProfile.Auxiliar));
        Assert.False(AttendanceRules.CanReadNote(SystemProfile.Administracion));
        Assert.True(AttendanceRules.CanReadNote(SystemProfile.Auxiliar));
    }

    [Fact]
    public void Una_ausencia_dura_de_1_a_90_dias_con_el_ultimo_dia_incluido()
    {
        var from = new DateOnly(2026, 11, 25);

        AttendanceRules.ValidateAbsence(from, from, AbsenceReason.Vacaciones);
        AttendanceRules.ValidateAbsence(from, from.AddDays(89), AbsenceReason.IngresoHospitalario);
        Assert.Throws<DomainValidationException>(() => AttendanceRules.ValidateAbsence(from, from.AddDays(90), AbsenceReason.Vacaciones));
        Assert.Throws<DomainValidationException>(() => AttendanceRules.ValidateAbsence(from, from.AddDays(-1), AbsenceReason.Vacaciones));
    }

    [Fact]
    public void La_pauta_es_una_mascara_de_dias_de_la_semana_de_1_a_127()
    {
        var monday = new DateOnly(2026, 11, 23);
        var mondayWednesdayFriday = AttendanceRules.Mask(DayOfWeek.Monday) | AttendanceRules.Mask(DayOfWeek.Wednesday) | AttendanceRules.Mask(DayOfWeek.Friday);

        Assert.Equal(21, mondayWednesdayFriday);
        Assert.True(AttendanceRules.Expects(mondayWednesdayFriday, monday));
        Assert.False(AttendanceRules.Expects(mondayWednesdayFriday, monday.AddDays(1)));
        Assert.True(AttendanceRules.Expects(mondayWednesdayFriday, monday.AddDays(2)));
        Assert.Equal(127, Enum.GetValues<DayOfWeek>().Sum(AttendanceRules.Mask));
        Assert.Equal(21, AttendanceRules.ValidatePattern(21));
        Assert.Throws<DomainValidationException>(() => AttendanceRules.ValidatePattern(0));
        Assert.Throws<DomainValidationException>(() => AttendanceRules.ValidatePattern(128));
    }

    [Fact]
    public void Lo_anotado_manda_sobre_una_ausencia_programada_y_sin_anotar_no_es_no_vino()
    {
        Assert.Equal(AttendanceDayStatus.Vino, AttendanceRules.StatusOf(AttendanceState.Asistio, scheduledAbsence: true));
        Assert.Equal(AttendanceDayStatus.NoVino, AttendanceRules.StatusOf(AttendanceState.NoAsistio, scheduledAbsence: true));
        Assert.Equal(AttendanceDayStatus.AusenciaProgramada, AttendanceRules.StatusOf(null, scheduledAbsence: true));
        Assert.Equal(AttendanceDayStatus.SinAnotar, AttendanceRules.StatusOf(null, scheduledAbsence: false));
    }

    private static readonly DateOnly Thursday = new(2026, 11, 26);

    private static FollowUpOutcome Follow(DateOnly today, int? pattern, Dictionary<DateOnly, AttendanceState> marks, params (DateOnly From, DateOnly To)[] absences) =>
        AttendanceRules.FollowUp(
            today, new DateOnly(2026, 1, 1), pattern,
            day => AttendanceRules.StatusOf(marks.TryGetValue(day, out var state) ? state : null, absences.Any(a => a.From <= day && day <= a.To)),
            day => marks.ContainsKey(day) ? AttendanceCause.SinAviso : null);

    [Fact]
    public void La_lista_cuenta_dias_esperados_seguidos_anotados_como_no_vino_hasta_ayer()
    {
        var marks = new Dictionary<DateOnly, AttendanceState>
        {
            [Thursday.AddDays(-1)] = AttendanceState.NoAsistio, [Thursday.AddDays(-2)] = AttendanceState.NoAsistio, [Thursday.AddDays(-3)] = AttendanceState.NoAsistio,
            [Thursday] = AttendanceState.NoAsistio, // hoy no está cerrado: no cuenta
        };

        var outcome = Follow(Thursday, null, marks);

        Assert.Equal(3, outcome.AbsentDays);
        Assert.Equal([AttendanceCause.SinAviso], outcome.Causes);
        Assert.Null(outcome.LastCame);
        Assert.Equal(0, outcome.UnmarkedDays);
    }

    [Fact]
    public void Los_dias_que_no_tocan_segun_la_pauta_ni_suman_ni_rompen()
    {
        var friday = new DateOnly(2026, 11, 27);
        var mondayWednesdayFriday = AttendanceRules.Mask(DayOfWeek.Monday) | AttendanceRules.Mask(DayOfWeek.Wednesday) | AttendanceRules.Mask(DayOfWeek.Friday);
        var marks = new Dictionary<DateOnly, AttendanceState>
        {
            [new DateOnly(2026, 11, 25)] = AttendanceState.NoAsistio, [new DateOnly(2026, 11, 23)] = AttendanceState.NoAsistio, [new DateOnly(2026, 11, 20)] = AttendanceState.NoAsistio,
        };

        // El jueves, el martes y el fin de semana no tocan y no están anotados: no rompen la cadena.
        Assert.Equal(3, Follow(friday, mondayWednesdayFriday, marks).AbsentDays);
        // Sin pauta se espera todos los días: el jueves sin anotar rompe la cadena y se cuenta aparte.
        var withoutPattern = Follow(friday, null, marks);
        Assert.Equal(0, withoutPattern.AbsentDays);
        Assert.Equal(1, withoutPattern.UnmarkedDays);
    }

    [Fact]
    public void Un_dia_que_vino_o_una_ausencia_programada_terminan_la_cuenta_y_un_dia_sin_anotar_no_vale_como_falta()
    {
        var yesterday = Thursday.AddDays(-1);
        var came = Follow(Thursday, null, new() { [yesterday] = AttendanceState.NoAsistio, [yesterday.AddDays(-1)] = AttendanceState.Asistio, [yesterday.AddDays(-2)] = AttendanceState.NoAsistio });
        Assert.Equal(1, came.AbsentDays);
        Assert.Equal(yesterday.AddDays(-1), came.LastCame);

        // Ayer cubierto por una ausencia programada: nadie lo sigue.
        var absent = Follow(Thursday, null, new() { [yesterday.AddDays(-1)] = AttendanceState.NoAsistio, [yesterday.AddDays(-2)] = AttendanceState.NoAsistio }, (yesterday.AddDays(-5), yesterday));
        Assert.Equal(0, absent.AbsentDays);
        Assert.Equal(0, absent.UnmarkedDays);

        // Ayer sin anotar: no hay faltas que seguir, hay días que anotar (aunque antes hubiera faltas).
        var unmarked = Follow(Thursday, null, new() { [yesterday.AddDays(-1)] = AttendanceState.NoAsistio, [yesterday.AddDays(-2)] = AttendanceState.NoAsistio, [yesterday.AddDays(-3)] = AttendanceState.NoAsistio });
        Assert.Equal(0, unmarked.AbsentDays);
        Assert.Equal(1, unmarked.UnmarkedDays);
    }

    [Fact]
    public void La_lista_no_mira_antes_del_alta_y_avisa_si_hay_mas_dias_de_los_que_mira()
    {
        // Alta hace dos días y nada anotado: solo dos días sin anotar, no sesenta.
        var recent = AttendanceRules.FollowUp(Thursday, Thursday.AddDays(-2), null, _ => AttendanceDayStatus.SinAnotar, _ => null);
        Assert.Equal(2, recent.UnmarkedDays);

        var longRun = AttendanceRules.FollowUp(Thursday, new DateOnly(2026, 1, 1), null, _ => AttendanceDayStatus.NoVino, _ => AttendanceCause.Otro);
        Assert.Equal(AttendanceRules.FollowUpWindowDays, longRun.AbsentDays);
        Assert.True(longRun.Capped);
    }
}
