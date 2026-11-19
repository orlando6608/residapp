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
        "Id", "Date", "Time", "Type", "Specialty", "Place", "Preparation", "TransportMode", "TransportState", "CompanionMode", "CompanionState", "Cancelled", "Covered",
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
