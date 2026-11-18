using ResidApp.Domain.Appointments;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>Reglas de dominio de las citas externas de los residentes (script 0101; docs/producto/citas-externas-diseno.md): coherencia de modos y estados, longitudes y saltos de
/// línea, cubierta o no, hora por confirmar, día no pasado y estados finales.</summary>
public class ExternalAppointmentsTests
{
    private static readonly DateOnly Today = new(2026, 11, 17);

    private static ExternalAppointment Make(
        DateOnly? date = null, TimeOnly? time = null, string? place = "Hospital Ficticio Norte",
        ExternalTransportMode transport = ExternalTransportMode.SinDeterminar, ExternalArrangementState transportState = ExternalArrangementState.Pendiente,
        ExternalCompanionMode companion = ExternalCompanionMode.SinDeterminar, ExternalArrangementState companionState = ExternalArrangementState.Pendiente,
        ExternalPreparation? preparation = null) =>
        new(date ?? Today.AddDays(3), time, ExternalAppointmentType.Consulta, ExternalAppointmentSpecialty.Cardiologia, place, transport, transportState, companion, companionState,
            preparation, ExternalFamilyVisibility.Visible);

    [Fact]
    public void UnaCitaNueva_SinNadaResuelto_FaltaAlgoYSinHoraSaleComoPorConfirmar()
    {
        var appointment = Make();

        Assert.False(appointment.Covered);
        Assert.True(appointment.TimeToConfirm);
        Assert.Equal(ExternalPreparation.None, appointment.Preparation);
    }

    [Theory]
    [InlineData(ExternalTransportMode.Familia, ExternalCompanionMode.PersonalCentro, true)]
    [InlineData(ExternalTransportMode.Ambulancia, ExternalCompanionMode.NoNecesita, true)]
    [InlineData(ExternalTransportMode.Centro, ExternalCompanionMode.LoLlevaTransporte, true)]
    public void Cubierta_SiTransporteYAcompananteEstanConfirmados(ExternalTransportMode transport, ExternalCompanionMode companion, bool covered) =>
        Assert.Equal(covered, Make(time: new TimeOnly(9, 0), transport: transport, transportState: ExternalArrangementState.Confirmado, companion: companion,
            companionState: ExternalArrangementState.Confirmado).Covered);

    [Fact]
    public void NoCubierta_SiUnoDeLosDosSigueEnPendiente()
    {
        Assert.False(Make(transport: ExternalTransportMode.Familia, transportState: ExternalArrangementState.Confirmado,
            companion: ExternalCompanionMode.Familiar, companionState: ExternalArrangementState.Pendiente).Covered);
        Assert.False(Make(transport: ExternalTransportMode.Familia, transportState: ExternalArrangementState.Pendiente,
            companion: ExternalCompanionMode.NoNecesita, companionState: ExternalArrangementState.Confirmado).Covered);
    }

    [Fact]
    public void SinDeterminar_SoloAdmitePendiente_YNoNecesitaSoloAdmiteConfirmado()
    {
        Assert.Equal(ExternalAppointment.InvalidCode, Assert.Throws<DomainValidationException>(() =>
            Make(transport: ExternalTransportMode.SinDeterminar, transportState: ExternalArrangementState.Confirmado)).Message);
        Assert.Throws<DomainValidationException>(() =>
            Make(companion: ExternalCompanionMode.SinDeterminar, companionState: ExternalArrangementState.Confirmado));
        Assert.Throws<DomainValidationException>(() =>
            Make(companion: ExternalCompanionMode.NoNecesita, companionState: ExternalArrangementState.Pendiente));
        Assert.Equal(ExternalArrangementState.Confirmado,
            Make(companion: ExternalCompanionMode.NoNecesita, companionState: ExternalArrangementState.Confirmado).CompanionState);
    }

    [Fact]
    public void ElLugar_EsUnaLineaDeComoMucho80Caracteres_RecortadaYVaciaEsNula()
    {
        Assert.Equal("Hospital Ficticio Norte", Make(place: "  Hospital Ficticio Norte  ").Place);
        Assert.Null(Make(place: "   ").Place);
        Assert.Null(Make(place: null).Place);
        Assert.Equal(80, Make(place: new string('x', 80)).Place!.Length);
        Assert.Throws<DomainValidationException>(() => Make(place: new string('x', 81)));
        Assert.Throws<DomainValidationException>(() => Make(place: "Hospital\nOncología"));
        Assert.Throws<DomainValidationException>(() => Make(place: "Hospital\rOncología"));
        Assert.Throws<DomainValidationException>(() => Make(place: "Hospital\tOncología"));
        Assert.Throws<DomainValidationException>(() => Make(place: "Hospital" + (char)0x2028 + "Oncología"));
    }

    [Fact]
    public void QuePreparar_LlevaCasillasYUnaLineaDe100Caracteres()
    {
        var prep = new ExternalPreparation(true, false, true, false, true, false, "  Llevar gafas  ");

        Assert.Equal("Llevar gafas", Make(preparation: prep).Preparation.Text);
        Assert.True(Make(preparation: prep).Preparation.Fasting);
        Assert.Null(Make(preparation: prep with { Text = " " }).Preparation.Text);
        Assert.Equal(100, Make(preparation: prep with { Text = new string('x', 100) }).Preparation.Text!.Length);
        Assert.Throws<DomainValidationException>(() => Make(preparation: prep with { Text = new string('x', 101) }));
        Assert.Throws<DomainValidationException>(() => Make(preparation: prep with { Text = "a\nb" }));
    }

    [Fact]
    public void LaHora_SeGuardaSinSegundos()
    {
        Assert.Equal(new TimeOnly(9, 30), Make(time: new TimeOnly(9, 30, 45)).Time);
        Assert.False(Make(time: new TimeOnly(0, 30)).TimeToConfirm);
    }

    [Fact]
    public void ValoresFueraDeLasListasCerradas_SeRechazan()
    {
        var date = Today.AddDays(1);
        Assert.Throws<DomainValidationException>(() => new ExternalAppointment(date, null, (ExternalAppointmentType)99, null, null, ExternalTransportMode.Familia,
            ExternalArrangementState.Confirmado, ExternalCompanionMode.Familiar, ExternalArrangementState.Pendiente, null, ExternalFamilyVisibility.Visible));
        Assert.Throws<DomainValidationException>(() => new ExternalAppointment(date, null, ExternalAppointmentType.Prueba, (ExternalAppointmentSpecialty)99, null,
            ExternalTransportMode.Familia, ExternalArrangementState.Confirmado, ExternalCompanionMode.Familiar, ExternalArrangementState.Pendiente, null, ExternalFamilyVisibility.Visible));
        Assert.Throws<DomainValidationException>(() => new ExternalAppointment(date, null, ExternalAppointmentType.Prueba, null, null, ExternalTransportMode.Familia,
            ExternalArrangementState.Confirmado, ExternalCompanionMode.Familiar, ExternalArrangementState.Pendiente, null, (ExternalFamilyVisibility)99));
    }

    [Fact]
    public void ElDia_NoPuedeSerAnteriorAHoy_PeroHoyVale()
    {
        Assert.Equal(Today, Make(date: Today).RequireNotInPast(Today).Date);
        Assert.Throws<DomainValidationException>(() => Make(date: Today.AddDays(-1)).RequireNotInPast(Today));
    }

    [Fact]
    public void ReprogramadaNoEsUnEstado_SonCinco()
    {
        Assert.Equal(["PROGRAMADA", "CONFIRMADA_HOSPITAL", "REALIZADA", "NO_ACUDIDA", "CANCELADA"], Enum.GetValues<ExternalAppointmentStatus>().Select(s => s.ToCode()));
    }

    [Theory]
    [InlineData(ExternalAppointmentStatus.Programada, ExternalAppointmentStatus.ConfirmadaHospital, true)]
    [InlineData(ExternalAppointmentStatus.Programada, ExternalAppointmentStatus.Realizada, true)]
    [InlineData(ExternalAppointmentStatus.Programada, ExternalAppointmentStatus.NoAcudida, true)]
    [InlineData(ExternalAppointmentStatus.ConfirmadaHospital, ExternalAppointmentStatus.Realizada, true)]
    [InlineData(ExternalAppointmentStatus.Programada, ExternalAppointmentStatus.Cancelada, true)]
    [InlineData(ExternalAppointmentStatus.ConfirmadaHospital, ExternalAppointmentStatus.Programada, false)]
    [InlineData(ExternalAppointmentStatus.Programada, ExternalAppointmentStatus.Programada, false)]
    [InlineData(ExternalAppointmentStatus.Realizada, ExternalAppointmentStatus.Programada, false)]
    [InlineData(ExternalAppointmentStatus.NoAcudida, ExternalAppointmentStatus.Realizada, false)]
    [InlineData(ExternalAppointmentStatus.Cancelada, ExternalAppointmentStatus.Programada, false)]
    public void Transiciones_LaAbiertaPasaAOtroEstado_LaFinalNoCambia_YConfirmadaNoVuelveAProgramada(ExternalAppointmentStatus from, ExternalAppointmentStatus to, bool allowed) =>
        Assert.Equal(allowed, ExternalAppointmentRules.CanTransition(from, to, Today, Today));

    [Theory]
    [InlineData(ExternalAppointmentStatus.Realizada, -1, true)]
    [InlineData(ExternalAppointmentStatus.Realizada, 0, true)]
    [InlineData(ExternalAppointmentStatus.Realizada, 1, false)]
    [InlineData(ExternalAppointmentStatus.NoAcudida, 0, true)]
    [InlineData(ExternalAppointmentStatus.NoAcudida, 1, false)]
    [InlineData(ExternalAppointmentStatus.ConfirmadaHospital, 5, true)]
    [InlineData(ExternalAppointmentStatus.Cancelada, 5, true)]
    public void RealizadaYNoAcudida_SoloDesdeElDiaDeLaCita_ElDeHoyCuenta(ExternalAppointmentStatus to, int daysFromToday, bool allowed) =>
        Assert.Equal(allowed, ExternalAppointmentRules.CanTransition(ExternalAppointmentStatus.Programada, to, Today.AddDays(daysFromToday), Today));

    [Fact]
    public void ElMotivo_EsDeLaListaCerradaMasUnaLineaDe100()
    {
        Assert.Equal((ExternalAppointmentReasonCode.CambioDelHospital, "Nueva fecha"),
            ExternalAppointmentRules.Reason(ExternalAppointmentReasonCode.CambioDelHospital, " Nueva fecha "));
        Assert.Null(ExternalAppointmentRules.Reason(ExternalAppointmentReasonCode.Otro, "  ").Line);
        Assert.Throws<DomainValidationException>(() => ExternalAppointmentRules.Reason(null, null));
        Assert.Throws<DomainValidationException>(() => ExternalAppointmentRules.Reason((ExternalAppointmentReasonCode)99, null));
        Assert.Throws<DomainValidationException>(() => ExternalAppointmentRules.Reason(ExternalAppointmentReasonCode.Otro, new string('x', 101)));
        Assert.Throws<DomainValidationException>(() => ExternalAppointmentRules.Reason(ExternalAppointmentReasonCode.Otro, "a\nb"));
    }

    [Fact]
    public void LaLista_DeEspecialidades_TieneCodigosUnicosQueCaben_YElTextoDeLaBajaEsElFijo()
    {
        var codes = Enum.GetValues<ExternalAppointmentSpecialty>().Select(s => s.ToCode()).ToList();

        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, code => Assert.True(code.Length <= 24));
        Assert.Equal("Cancelada por el sistema al dar de baja al residente.", ExternalAppointmentRules.DischargeCancelReason);
    }
}
