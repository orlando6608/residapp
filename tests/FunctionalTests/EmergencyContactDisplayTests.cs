using ResidApp.Application.Ports;
using ResidApp.Web.Models;

namespace ResidApp.FunctionalTests;

/// <summary>ADM-08/DER-06 (0022): cómo se precarga y se enlaza el contacto urgente en la derivación y la ficha.</summary>
public class EmergencyContactDisplayTests
{
    [Fact]
    public void AQuienSeLlama_EsNombreYRelacion() =>
        Assert.Equal("Lucía Pérez (Hija)", EmergencyContactDisplay.WhoIsCalled(new EmergencyContactSummary("Lucía Pérez", "Hija", "600 111 222")));

    [Theory]
    [InlineData("600 111 222", "tel:600111222")]
    [InlineData("+34 600-11.22 (33)", "tel:+34600112233")]
    public void EnlaceTel_SoloLlevaMasYDigitos(string phone, string href) =>
        Assert.Equal(href, EmergencyContactDisplay.TelHref(phone));
}
