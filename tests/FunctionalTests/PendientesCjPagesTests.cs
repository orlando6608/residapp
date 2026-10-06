using System.Net.Http;

namespace ResidApp.FunctionalTests;

/// <summary>Los documentos de docs/pendientes-cj se publican con la aplicación en /pendientes-cj, para que CJ los
/// responda desde la web.</summary>
public class PendientesCjPagesTests : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    private readonly ResidentsFlowTests.WebAppFactory _factory;

    public PendientesCjPagesTests(ResidentsFlowTests.WebAppFactory factory) => _factory = factory;

    [Theory]
    [InlineData("index")]
    [InlineData("rangos-referencia-constantes")]
    [InlineData("decisiones-direccion-basal-derivacion")]
    [InlineData("traslado-y-baja-residente")]
    [InlineData("administracion-ambito-familiares-cargos")]
    [InlineData("continuidad-supervision-comunicacion")]
    [InlineData("guia-de-pruebas-cj")]
    public async Task Document_IsServedAsHtml(string name)
    {
        var response = await _factory.CreateClient().GetAsync($"/pendientes-cj/{name}.html");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Footer_LinksToTheIndex()
    {
        var html = await _factory.CreateClient().GetStringAsync("/DevAuth/Login");

        Assert.Contains("href=\"/pendientes-cj/index.html\"", html);
    }
}
