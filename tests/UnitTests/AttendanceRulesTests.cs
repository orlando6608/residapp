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
}
