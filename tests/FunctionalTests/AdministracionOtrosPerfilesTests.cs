using System.Net;

namespace ResidApp.FunctionalTests;

/// <summary>Las pantallas de Administración no se abren a otros perfiles: se les lleva a su inicio. Administración sigue entrando.</summary>
public class AdministracionOtrosPerfilesTests(ResidentsFlowTests.WebAppFactory factory) : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    // Con un único ámbito, abrir el selector lo deja elegido.
    private async Task<HttpClient> EntrarAsync(string subject)
    {
        var client = await SesionTests.LoginAsync(factory, subject);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/ProfileScope/Select")).StatusCode);
        return client;
    }

    [Theory]
    [InlineData("ENFERMERIA", "/Administracion")]
    [InlineData("ENFERMERIA", "/Administracion/Residentes")]
    [InlineData("MEDICINA", "/Administracion")]
    public async Task OtroPerfil_QueAbreAdministracion_VuelveAlInicio(string perfil, string ruta)
    {
        var (subject, _) = await SesionTests.SeedAccountAsync(perfil);
        var client = await EntrarAsync(subject);

        var response = await client.GetAsync(ruta);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Administracion_SigueAbriendoSuInicio()
    {
        var (subject, _) = await SesionTests.SeedAccountAsync("ADMINISTRACION");
        var client = await EntrarAsync(subject);

        var response = await client.GetAsync("/Administracion");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
