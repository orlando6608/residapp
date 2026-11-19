using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;

namespace ResidApp.UnitTests;

/// <summary>
/// Lo que ve la familia de las citas externas (PR 5; DPO, apartado 10.2): el servidor no devuelve a esa vista la nota «al terminar», el motivo de cancelar, el nombre de quien se encarga, el historial,
/// quién la registró ni el nivel de visibilidad elegido. No basta con que la pantalla no los pinte: la prueba fija por reflexión el conjunto de propiedades del modelo, de modo que añadir una nueva
/// obliga a cambiar esta prueba a propósito (y a decidirlo con la persona experta en protección de datos).
/// </summary>
public class ExternalAppointmentFamilyViewTests
{
    private static readonly string[] Allowed =
    [
        "Id", "Date", "Time", "Type", "Specialty", "Place", "Preparation", "TransportMode", "TransportState", "CompanionMode", "CompanionState", "Cancelled", "Covered", "CoveredByFamily", "TransportHolder", "CompanionHolder", "Refused", "CanRefuse", "TransportLapsed", "CompanionLapsed",
    ];

    private static readonly string[] Forbidden = ["Note", "Reason", "Visibility", "History", "Change", "Creat", "Author", "Account", "Profile", "Name", "Version", "Resident", "Unit", "Origin", "Status"];

    [Fact]
    public void ElModeloDeLaFamilia_TieneExactamenteLasPropiedadesPermitidas_YNingunaDeLasProhibidas()
    {
        var names = typeof(ExternalAppointmentForFamily).GetProperties().Select(p => p.Name).ToArray();

        Assert.Equal(Allowed.OrderBy(n => n), names.OrderBy(n => n));
        Assert.All(Forbidden, word => Assert.DoesNotContain(names, n => n.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void LoQueEntregaLaLecturaDeLaFamilia_SoloLleva_ElResidenteHoyYLaListaDeEseModelo()
    {
        var names = typeof(FamilyExternalAppointments).GetProperties().Select(p => p.Name).ToArray();

        Assert.Equal(["Appointments", "Resident", "Today"], names.OrderBy(n => n));
        Assert.Equal(typeof(IReadOnlyList<ExternalAppointmentForFamily>), typeof(FamilyExternalAppointments).GetProperty("Appointments")!.PropertyType);
    }

    [Fact]
    public void ElTipoGeneral_NuncaLlevaLaEspecialidad_YOtraEsCitaFueraDelCentro()
    {
        var specialties = Enum.GetValues<ExternalAppointmentSpecialty>().Select(s => s.ToString()).ToArray();

        foreach (var type in Enum.GetValues<ExternalAppointmentType>())
        {
            var label = ExternalFamilyRules.GeneralTypeLabel(type);
            Assert.EndsWith("fuera del centro", label);
            Assert.All(specialties, specialty => Assert.DoesNotContain(specialty, label, StringComparison.OrdinalIgnoreCase));
        }

        Assert.Equal("Cita fuera del centro", ExternalFamilyRules.GeneralTypeLabel(ExternalAppointmentType.Otra));
        Assert.Equal("Prueba fuera del centro", ExternalFamilyRules.GeneralTypeLabel(ExternalAppointmentType.Prueba));
        Assert.Equal("Consulta fuera del centro", ExternalFamilyRules.GeneralTypeLabel(ExternalAppointmentType.Consulta));
        Assert.Equal("Tratamiento fuera del centro", ExternalFamilyRules.GeneralTypeLabel(ExternalAppointmentType.Tratamiento));
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, false)]
    public void EncenderExigeLasTresConfirmaciones(bool informed, bool will, bool impact, bool expected) =>
        Assert.Equal(expected, new ExternalFamilyConfirmations(informed, will, impact).All);

    [Fact]
    public void LaVentanaDeLaFamilia_EsDeNoventaDias() => Assert.Equal(90, ExternalFamilyRules.WindowDays);
}

public class ExternalAppointmentFamilyActionTests
{
    private static ExternalAppointmentForFamily Appointment(
        ExternalFamilyHolder transport = ExternalFamilyHolder.Nobody, ExternalFamilyHolder companion = ExternalFamilyHolder.Nobody, bool refused = false,
        ExternalTransportMode transportMode = ExternalTransportMode.Familia, ExternalArrangementState transportState = ExternalArrangementState.Pendiente, bool cancelled = false) =>
        new(Guid.NewGuid(), new DateOnly(2030, 1, 1), null, ExternalAppointmentType.Consulta, null, null, ExternalPreparation.None, transportMode, transportState,
            ExternalCompanionMode.Familiar, ExternalArrangementState.Pendiente, cancelled, transport, companion, refused);

    [Fact]
    public void LosBotones_DependenDeQuienLoTiene_YDeSiElPersonalLoConfirmo()
    {
        var free = Appointment();
        Assert.True(free.CanTake(ExternalArrangementScope.Transporte));
        Assert.False(free.CanRelease(ExternalArrangementScope.Transporte));
        Assert.True(free.CanRefuse);

        var mine = Appointment(transport: ExternalFamilyHolder.You);
        Assert.False(mine.CanTake(ExternalArrangementScope.Transporte));
        Assert.True(mine.CanRelease(ExternalArrangementScope.Transporte));
        Assert.False(mine.CanRefuse); // quien se encargó de algo suelta, no dice «No puedo»

        var other = Appointment(transport: ExternalFamilyHolder.Another);
        Assert.False(other.CanTake(ExternalArrangementScope.Transporte));
        Assert.False(other.CanRelease(ExternalArrangementScope.Transporte));
        Assert.True(other.CanTake(ExternalArrangementScope.Acompanante));

        Assert.False(Appointment(refused: true).CanRefuse);
        Assert.False(Appointment(transportState: ExternalArrangementState.Confirmado).CanTake(ExternalArrangementScope.Transporte));
        Assert.False(Appointment(transportMode: ExternalTransportMode.Centro).CanTake(ExternalArrangementScope.Transporte));
        var cancelled = Appointment(cancelled: true);
        Assert.False(cancelled.CanTake(ExternalArrangementScope.Transporte) || cancelled.CanRelease(ExternalArrangementScope.Transporte) || cancelled.CanRefuse);
    }

    [Fact]
    public void ParaLaFamiliaUnEncargoCuentaComoCubierto_ParaElPersonalNo()
    {
        var offered = Appointment(ExternalFamilyHolder.You, ExternalFamilyHolder.Another);
        Assert.True(offered.CoveredByFamily);
        Assert.False(offered.Covered);
        Assert.False(Appointment(ExternalFamilyHolder.You).CoveredByFamily);
    }
}
