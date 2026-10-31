using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using ResidApp.Application.Email;

namespace ResidApp.FunctionalTests;

/// <summary>El flujo de trabajo ilustrado (wwwroot/flujo-de-trabajo.html) es una página estática y pública, como el Manual, y el pie
/// de página la enlaza junto al Manual y a los documentos de CJ. No va detrás de la clave de CJ.</summary>
public class FlujoDeTrabajoPageTests : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    private readonly ResidentsFlowTests.WebAppFactory _factory;

    public FlujoDeTrabajoPageTests(ResidentsFlowTests.WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Footer_LinksToTheWorkflowPage()
    {
        var html = await _factory.CreateClient().GetStringAsync("/Acceso/Entrar");

        Assert.Contains("href=\"/flujo-de-trabajo.html\">Flujo de trabajo</a>", html);
    }

    [Fact]
    public async Task WorkflowPage_IsServedWithoutTheCjKey_WithBrandAndLinkBackToTheApp()
    {
        var response = await _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .GetAsync("/flujo-de-trabajo.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<img src=\"/images/logo.svg\"", html);
        Assert.Contains("Plataforma Asistencial Geriátrica", html);
        Assert.Contains("<a href=\"/\">← Volver a ResidApp</a>", html);
    }

    /// <summary>El Manual cuenta los dos avisos por correo de las citas y cita el asunto real del aviso de cambio (si cambia la plantilla, esta prueba avisa): no vuelve a decir que
    /// la aplicación no manda ningún correo al reprogramar o cancelar.</summary>
    [Fact]
    public async Task Manual_CuentaElAvisoDeCambioDeCita_ConElAsuntoRealDeLaPlantilla()
    {
        var html = await _factory.CreateClient().GetStringAsync("/Home/Manual");
        var flow = await _factory.CreateClient().GetStringAsync("/flujo-de-trabajo.html");

        Assert.Contains(ChangeNoticeTemplate.Render("es", "https://dev.example.test").Subject, html);
        Assert.DoesNotContain("no le manda ningún correo", html);
        Assert.DoesNotContain("Todavía no hay avisos ni recordatorios", flow);
        Assert.DoesNotContain("la aplicación no envía correos", html);
    }
}
