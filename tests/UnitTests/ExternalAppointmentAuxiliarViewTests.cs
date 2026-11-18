using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;

namespace ResidApp.UnitTests;

/// <summary>
/// La lectura de citas externas del Auxiliar (PR 3; DPO, apartados 2 y 6.4): el servidor no devuelve a esa vista el tipo, la especialidad, el lugar, la nota «al terminar» ni el nombre de ningún
/// familiar. No basta con que la pantalla no los pinte: la prueba fija por reflexión el conjunto de propiedades del modelo, de modo que añadir una nueva obliga a cambiar esta prueba a propósito.
/// </summary>
public class ExternalAppointmentAuxiliarViewTests
{
    private static readonly string[] Allowed =
        ["Id", "ResidentId", "ResidentName", "Date", "Time", "Preparation", "TransportMode", "CompanionMode"];

    private static readonly string[] Forbidden = ["Type", "Specialty", "Place", "Note", "Family", "Companion Name", "Visibility", "Status", "Reason"];

    [Fact]
    public void ElModeloDelAuxiliar_TieneExactamenteLasPropiedadesPermitidas_YNingunaDeLasProhibidas()
    {
        var names = typeof(ExternalAppointmentForAuxiliar).GetProperties().Select(p => p.Name).ToArray();

        Assert.Equal(Allowed.OrderBy(n => n), names.OrderBy(n => n));
        Assert.All(Forbidden, word => Assert.DoesNotContain(names, n => n.Contains(word.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void LoQueEntregaElServicioAlAuxiliar_SoloLleva_HoyYLaListaDeEseModelo()
    {
        var names = typeof(AuxiliarExternalAppointments).GetProperties().Select(p => p.Name).ToArray();

        Assert.Equal(["Appointments", "Today"], names.OrderBy(n => n));
        Assert.Equal(typeof(IReadOnlyList<ExternalAppointmentForAuxiliar>), typeof(AuxiliarExternalAppointments).GetProperty("Appointments")!.PropertyType);
    }

    [Fact]
    public void LaPreparacion_SoloLlevaCasillasYUnaLineaCorta_SinNadaClinico()
    {
        var names = typeof(ResidApp.Domain.Appointments.ExternalPreparation).GetProperties().Select(p => p.Name).Where(n => n != "None" && n != "EqualityContract").ToArray();

        Assert.Equal(["Fasting", "HealthCard", "LastReport", "Medication", "Other", "Text", "Wheelchair"], names.OrderBy(n => n));
    }
}
