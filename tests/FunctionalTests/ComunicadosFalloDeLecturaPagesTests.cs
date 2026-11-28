using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ResidApp.Application.Ports;

namespace ResidApp.FunctionalTests;

/// <summary>«Comunicados a la familia» de Dirección Clínica y de Administración cuando la lectura falla: la lista no puede decir que no haya comunicados, ni la corrección o
/// la retirada que el comunicado no las admite, y no se pierde lo escrito.</summary>
public class ComunicadosFalloDeLecturaPagesTests(ResidentsFlowTests.WebAppFactory factory) : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    private const string Failure = "Ahora mismo no se pueden mostrar los comunicados a la familia.";
    private const string Empty = "No hay comunicados a la familia en los últimos";

    [Theory]
    [InlineData("DIRECCION_CLINICA", "/Direccion/Comunicados", true)]
    [InlineData("DIRECCION_CLINICA", "/Direccion/Comunicados", false)]
    [InlineData("ADMINISTRACION", "/Administracion/Comunicados", true)]
    [InlineData("ADMINISTRACION", "/Administracion/Comunicados", false)]
    public async Task SoloDiceQueNoHayComunicados_SiLosHaPodidoLeer(string profile, string url, bool fails)
    {
        WebApplicationFactory<Program> app = fails
            ? factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddScoped<IFamilyCommunicationDirectory, FailingCommunications>()))
            : factory;
        var (subject, _) = await SesionTests.SeedAccountAsync(profile);
        var client = await SesionTests.LoginAsync(app, subject);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/ProfileScope/Select")).StatusCode);

        var page = WebUtility.HtmlDecode(await client.GetStringAsync(url));

        Assert.Equal(fails, page.Contains(Failure, StringComparison.Ordinal));
        Assert.Equal(!fails, page.Contains(Empty, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("DIRECCION_CLINICA", "/Direccion", "PublicarCorreccion")]
    [InlineData("DIRECCION_CLINICA", "/Direccion", "RetirarComunicado")]
    [InlineData("ADMINISTRACION", "/Administracion", "PublicarCorreccion")]
    [InlineData("ADMINISTRACION", "/Administracion", "RetirarComunicado")]
    public async Task AlAbrir_SiFallaLaLectura_NoDiceQueElComunicadoNoLoAdmite(string profile, string area, string action)
    {
        var client = await FailingClientAsync(profile);

        var response = await client.GetAsync($"{area}/{action}?comunicadoId={Guid.NewGuid()}");
        Assert.Equal($"{area}/Comunicados", response.Headers.Location?.OriginalString);
        var page = WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location));

        Assert.Contains("Ahora mismo no se puede leer ese comunicado. Vuelve a intentarlo.", page);
        Assert.DoesNotContain("Ese comunicado no", page);
    }

    [Theory]
    [InlineData("DIRECCION_CLINICA", "/Direccion", "PublicarCorreccion", "Form.Texto")]
    [InlineData("DIRECCION_CLINICA", "/Direccion", "RetirarComunicado", "Form.Motivo")]
    [InlineData("ADMINISTRACION", "/Administracion", "PublicarCorreccion", "Form.Texto")]
    [InlineData("ADMINISTRACION", "/Administracion", "RetirarComunicado", "Form.Motivo")]
    public async Task AlEnviar_SiFallaLaLectura_LoDiceYConservaLoEscrito(string profile, string area, string action, string field)
    {
        var client = await FailingClientAsync(profile);
        var token = Regex.Match(await client.GetStringAsync($"{area}/Comunicados"), "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var id = Guid.NewGuid().ToString();

        var response = await client.PostAsync($"{area}/{action}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["Form.ComunicadoId"] = id, ["Form.Tipo"] = "Ordinaria", [field] = "Lo que ya estaba escrito",
        }));
        var page = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ahora mismo no se puede leer el comunicado, así que no se ha cambiado nada.", page);
        Assert.Contains("Lo que ya estaba escrito</textarea>", page);
        Assert.Contains($"value=\"{id}\"", page);
        // La advertencia no depende de lo que se haya podido leer.
        Assert.Contains(action == "RetirarComunicado" ? "No se puede deshacer" : "Sale en cuanto la guardes", page);
    }

    private async Task<HttpClient> FailingClientAsync(string profile)
    {
        var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IFamilyCommunicationDirectory, FailingCommunications>()));
        var (subject, _) = await SesionTests.SeedAccountAsync(profile);
        var client = await SesionTests.LoginAsync(app, subject);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/ProfileScope/Select")).StatusCode);
        return client;
    }

    private sealed class FailingCommunications : IFamilyCommunicationDirectory
    {
        public Task<IReadOnlyList<AdministrationFamilyCommunication>> ListAsync(
            FamilyCommunicationAccess access, DateTimeOffset since, CancellationToken ct = default) =>
            throw new InvalidOperationException("Fallo de prueba.");
    }
}
