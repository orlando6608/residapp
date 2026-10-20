using Microsoft.AspNetCore.Mvc.Testing;

namespace ResidApp.FunctionalTests;

/// <summary>El service worker guarda /Home/Offline al instalarse, con las cookies de quien esté entonces en la app, y después la ve cualquiera que use el
/// dispositivo sin red (directrices-pwa-movil.md, §3). La página no puede depender de la sesión ni mostrar identidad, perfil ni centro.</summary>
public class PaginaSinConexionTests : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    private readonly ResidentsFlowTests.WebAppFactory _factory;

    public PaginaSinConexionTests(ResidentsFlowTests.WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Offline_ConSesionAbierta_NoMuestraIdentidadNiEnlacesDePanel()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", "residapp_dev_subject=dev-cualquiera");

        var html = await client.GetStringAsync("/Home/Offline");

        Assert.Contains("Sin conexión", html);
        Assert.DoesNotContain("dev-cualquiera", html);
        Assert.DoesNotContain("Identidad", html);
        Assert.DoesNotContain("Mi panel", html);
        Assert.DoesNotContain("Cambiar de identidad", html);
    }
}
