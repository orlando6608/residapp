using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

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
}
