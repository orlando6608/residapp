using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ResidApp.FunctionalTests;

/// <summary>Cabeceras de seguridad (hallazgo M3): las de siempre en todas las respuestas, la política de contenido y la orden de no guardar copia solo en las páginas de la
/// aplicación (no en los estáticos), y el «nonce» de cada petición en los scripts escritos dentro de la página. «Server: Kestrel» no se puede probar aquí (el servidor de pruebas
/// no es Kestrel): se comprueba contra Azure con curl.</summary>
public class CabecerasSeguridadTests(ResidentsFlowTests.WebAppFactory factory) : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : response.Content.Headers.TryGetValues(name, out var content) ? string.Join(", ", content) : "";

    private static string NonceOf(string csp) => Regex.Match(csp, "'nonce-([^']+)'").Groups[1].Value;

    [Fact]
    public async Task UnaPaginaDeLaAplicacionLlevaTodasLasCabecerasYNoSeGuardaEnLaCache()
    {
        var response = await factory.CreateClient(NoRedirect).GetAsync("/Home/Privacy");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Contains("microphone=(self)", Header(response, "Permissions-Policy"));
        Assert.Contains("camera=()", Header(response, "Permissions-Policy"));
        Assert.Contains("no-store", Header(response, "Cache-Control"));

        var csp = Header(response, "Content-Security-Policy");
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("form-action 'self'", csp);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp);
        Assert.DoesNotContain("'unsafe-eval'", csp);
        Assert.Equal("", Header(response, "Content-Security-Policy-Report-Only"));
    }

    [Fact]
    public async Task LosScriptsEnLineaLlevanElNonceDeLaPeticion_YNingunoSeQuedaSinEl()
    {
        var response = await factory.CreateClient(NoRedirect).GetAsync("/Home/Privacy");
        // El HTML escribe el nonce con entidades (+ como &#x2B;); el navegador las decodifica al comparar.
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var nonce = NonceOf(Header(response, "Content-Security-Policy"));

        Assert.False(string.IsNullOrEmpty(nonce));
        // La plantilla escribe al menos un script en línea (el registro del service worker): debe llevar el nonce de esta petición.
        Assert.Contains($"<script nonce=\"{nonce}\"", html);
        // Y ningún script en línea (sin src) sin nonce: la política lo bloquearía.
        Assert.DoesNotMatch(@"<script(?![^>]*\b(?:src|nonce)=)[^>]*>", html);
        // Ni manejadores en línea (onclick=…), que el nonce no cubre.
        Assert.DoesNotMatch(@"<[^>]+\son(?:click|change|submit|load|input|error|focus|blur)\s*=", html);
    }

    [Fact]
    public async Task ElNonceCambiaEnCadaPeticion()
    {
        var client = factory.CreateClient(NoRedirect);
        var first = NonceOf(Header(await client.GetAsync("/Home/Privacy"), "Content-Security-Policy"));
        var second = NonceOf(Header(await client.GetAsync("/Home/Privacy"), "Content-Security-Policy"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task LosEstaticosLlevanLasCabecerasDeSiempreSinPoliticaDeContenidoNiNoStore()
    {
        var response = await factory.CreateClient(NoRedirect).GetAsync("/css/site.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("", Header(response, "Content-Security-Policy"));
        Assert.DoesNotContain("no-store", Header(response, "Cache-Control"));
    }

    [Fact]
    public async Task ConSeguridadCspSoloInformeLaPoliticaSoloAvisa()
    {
        using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Seguridad:CspSoloInforme"] = "true" })));
        var response = await app.CreateClient(NoRedirect).GetAsync("/Home/Privacy");

        Assert.Equal("", Header(response, "Content-Security-Policy"));
        Assert.Contains("script-src 'self' 'nonce-", Header(response, "Content-Security-Policy-Report-Only"));
        // Las demás cabeceras no dependen del modo.
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
    }

    [Fact]
    public async Task UnaPaginaConSesionSinFormularioLlevaNoStoreCspYElNonceDeSusScripts()
    {
        var (subject, _) = await SesionTests.SeedAccountAsync();
        var client = await SesionTests.LoginAsync(factory, subject);

        // Con un solo perfil, la primera visita pasa por la elección de ámbito (302) y vuelve: se siguen las redirecciones a mano.
        var response = await client.GetAsync("/Administracion");
        for (var hops = 0; hops < 4 && response.StatusCode == HttpStatusCode.Found; hops++)
        {
            response = await client.GetAsync(response.Headers.Location);
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", Header(response, "Cache-Control"));
        var csp = Header(response, "Content-Security-Policy");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains($"<script nonce=\"{NonceOf(csp)}\"", html);
        Assert.DoesNotMatch(@"<script(?![^>]*\b(?:src|nonce)=)[^>]*>", html);
    }

    [Fact]
    public async Task UnaPaginaConFormularioLlevaDenyYUnSoloCacheControl()
    {
        var response = await factory.CreateClient(NoRedirect).GetAsync("/Acceso/Entrar");

        // El antiforgery pone SAMEORIGIN y su propio Cache-Control: el middleware debe dejar DENY y no duplicar.
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Single(response.Headers.GetValues("Cache-Control"));
        Assert.Contains("no-store", Header(response, "Cache-Control"));
    }

    [Fact]
    public async Task LaRedireccionAlLoginTambienLlevaLasCabeceras()
    {
        var response = await factory.CreateClient(NoRedirect).GetAsync("/Administracion");

        Assert.True(SesionTests.RedirectsToLogin(response));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("si")]
    [InlineData("")]
    public async Task UnValorInvalidoDelInterruptorNoTumbaLaAppYSeQuedaEnBloquear(string value)
    {
        using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Seguridad:CspSoloInforme"] = value })));
        var response = await app.CreateClient(NoRedirect).GetAsync("/Home/Privacy");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("script-src 'self' 'nonce-", Header(response, "Content-Security-Policy"));
        Assert.Equal("", Header(response, "Content-Security-Policy-Report-Only"));
    }

    [Fact]
    public void NingunaVistaUsaManejadoresEnLineaNiDireccionesJavascript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "ResidApp.Web", "Views")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(directory!.FullName, "src", "ResidApp.Web", "Views"), "*.cshtml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var name = Path.GetRelativePath(directory.FullName, file);
            if (Regex.IsMatch(text, @"<[^>@]*\son[a-z]+\s*=", RegexOptions.IgnoreCase)) problems.Add($"{name}: manejador en línea (onclick=…)");
            if (Regex.IsMatch(text, "(?:href|src|action|formaction)\\s*=\\s*[\"']\\s*javascript:", RegexOptions.IgnoreCase)) problems.Add($"{name}: dirección javascript:");
        }

        Assert.True(problems.Count == 0, "La política de contenido (CSP) los bloquea; usa un data-… con su manejador en site.js:\n" + string.Join("\n", problems));
    }
}
