using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;
using ResidApp.Web.Models;

namespace ResidApp.FunctionalTests;

/// <summary>Textos y fechas del calendario del personal (PR 3): semana de lunes a domingo, nombres de mes y de día fijos en español, iniciales y marcas con palabra.</summary>
public class ExternalAppointmentCalendarDisplayTests
{
    private static readonly DateOnly Today = new(2026, 11, 18);

    [Theory]
    [InlineData("2026-11-16", "2026-11-16")] // lunes
    [InlineData("2026-11-18", "2026-11-16")] // miércoles
    [InlineData("2026-11-22", "2026-11-16")] // domingo
    [InlineData("2026-11-23", "2026-11-23")]
    public void LaSemanaEmpiezaEnLunes(string day, string monday) =>
        Assert.Equal(DateOnly.Parse(monday), ExternalAppointmentCalendarDisplay.MondayOf(DateOnly.Parse(day)));

    [Fact]
    public void LosTitulos_SalenEnEspanolSinDependerDeLaCulturaDelServidor()
    {
        Assert.Equal("Noviembre de 2026", ExternalAppointmentCalendarDisplay.MonthTitle(new DateOnly(2026, 11, 1)));
        Assert.Equal("16 – 22 de noviembre de 2026", ExternalAppointmentCalendarDisplay.WeekTitle(new DateOnly(2026, 11, 16)));
        Assert.Equal("26 de octubre – 1 de noviembre de 2026", ExternalAppointmentCalendarDisplay.WeekTitle(new DateOnly(2026, 10, 26)));
        Assert.Equal("28 de diciembre de 2026 – 3 de enero de 2027", ExternalAppointmentCalendarDisplay.WeekTitle(new DateOnly(2026, 12, 28)));
        Assert.Equal("Mié 18", ExternalAppointmentCalendarDisplay.DayLong(new DateOnly(2026, 11, 18)));
        Assert.Equal("Dom 22", ExternalAppointmentCalendarDisplay.DayLong(new DateOnly(2026, 11, 22)));
    }

    [Theory]
    [InlineData("Antonio Marín", "A. M.")]
    [InlineData("pilar", "P.")]
    [InlineData("  Ana  María   Pérez Gil ", "A. M. P.")]
    public void LasIniciales_SonLasDeLasTresPrimerasPalabras(string name, string expected) =>
        Assert.Equal(expected, ExternalAppointmentCalendarDisplay.Initials(name));

    private static ExternalAppointmentRecord Record(
        ExternalAppointmentStatus status, ExternalArrangementState transport, ExternalArrangementState companion, bool withTime = true, int daysFromToday = 1) => new(
        Guid.NewGuid(), ResidentId.From(Guid.NewGuid()), "Residente Ficticio", null,
        new ExternalAppointment(Today.AddDays(daysFromToday), withTime ? new TimeOnly(10, 30) : null, ExternalAppointmentType.Consulta, null, null,
            transport == ExternalArrangementState.Confirmado ? ExternalTransportMode.Familia : ExternalTransportMode.SinDeterminar, transport,
            companion == ExternalArrangementState.Confirmado ? ExternalCompanionMode.NoNecesita : ExternalCompanionMode.SinDeterminar, companion, null, ExternalFamilyVisibility.Visible),
        status, 1, false);

    [Theory]
    [InlineData(ExternalAppointmentStatus.Programada, ExternalArrangementState.Confirmado, ExternalArrangementState.Confirmado, true, 1, "Cubierta", "success")]
    [InlineData(ExternalAppointmentStatus.Programada, ExternalArrangementState.Confirmado, ExternalArrangementState.Confirmado, true, 0, "Cubierta", "success")]
    [InlineData(ExternalAppointmentStatus.Programada, ExternalArrangementState.Confirmado, ExternalArrangementState.Confirmado, false, 1, "Por confirmar", "warning")]
    [InlineData(ExternalAppointmentStatus.ConfirmadaHospital, ExternalArrangementState.Pendiente, ExternalArrangementState.Confirmado, true, 1, "Falta transporte", "danger")]
    [InlineData(ExternalAppointmentStatus.Programada, ExternalArrangementState.Confirmado, ExternalArrangementState.Pendiente, true, 1, "Falta acompañante", "danger")]
    [InlineData(ExternalAppointmentStatus.Programada, ExternalArrangementState.Pendiente, ExternalArrangementState.Pendiente, false, 1, "Falta transporte y acompañante", "danger")]
    [InlineData(ExternalAppointmentStatus.Realizada, ExternalArrangementState.Pendiente, ExternalArrangementState.Pendiente, true, 1, "Realizada", "secondary")]
    [InlineData(ExternalAppointmentStatus.NoAcudida, ExternalArrangementState.Confirmado, ExternalArrangementState.Confirmado, true, -1, "No se acudió", "secondary")]
    [InlineData(ExternalAppointmentStatus.Cancelada, ExternalArrangementState.Confirmado, ExternalArrangementState.Confirmado, true, 1, "Cancelada", "secondary")]
    // Abierta con el día pasado: «Por cerrar», aunque esté cubierta o le falte algo (decisión de Orlando, 2026-11-18).
    [InlineData(ExternalAppointmentStatus.Programada, ExternalArrangementState.Confirmado, ExternalArrangementState.Confirmado, true, -1, "Por cerrar", "warning")]
    [InlineData(ExternalAppointmentStatus.ConfirmadaHospital, ExternalArrangementState.Pendiente, ExternalArrangementState.Pendiente, true, -5, "Por cerrar", "warning")]
    public void LaMarca_DiceConPalabras_SiEstaCubiertaQueFaltaPorConfirmarOPorCerrar(
        ExternalAppointmentStatus status, ExternalArrangementState transport, ExternalArrangementState companion, bool withTime, int daysFromToday, string text, string css) =>
        Assert.Equal((text, css), ExternalAppointmentCalendarDisplay.Mark(Record(status, transport, companion, withTime, daysFromToday), Today));
}
