using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ResidApp.FunctionalTests;

/// <summary>Los documentos de docs/pendientes-cj se publican con la aplicación en /pendientes-cj, detrás de una clave
/// (PendientesCj:Clave; es un freno para el desarrollo, no autenticación real), para que CJ los responda desde la web. Tres secciones:
/// Preguntas, Pruebas y Archivo (documentos contestados e implementados, en archivados/).</summary>
public class PendientesCjPagesTests : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    private readonly ResidentsFlowTests.WebAppFactory _factory;

    public PendientesCjPagesTests(ResidentsFlowTests.WebAppFactory factory) => _factory = factory;

    private const string Key = "CJ123";

    // Sin seguir redirecciones: se comprueba a dónde manda cada respuesta.
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };

    private HttpClient NewClient() => _factory.CreateClient(NoRedirect);

    private static string Token(string html) =>
        Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

    private static async Task<HttpResponseMessage> PostKeyAsync(HttpClient client, string key, string? returnUrl = null)
    {
        var form = await client.GetStringAsync("/AccesoCj");
        var fields = new Dictionary<string, string> { ["__RequestVerificationToken"] = Token(form), ["clave"] = key };
        if (returnUrl is not null) fields["returnUrl"] = returnUrl;
        return await client.PostAsync("/AccesoCj", new FormUrlEncodedContent(fields));
    }

    private async Task<HttpClient> LoggedInAsync()
    {
        var client = NewClient();
        Assert.Equal(HttpStatusCode.Redirect, (await PostKeyAsync(client, Key)).StatusCode);
        return client;
    }

    public static readonly TheoryData<string> Pages =
    [
        "index", "preguntas", "pruebas", "archivo",
        "aclaraciones-respuestas-cj", "traslado-y-baja-residente", "administracion-ambito-familiares-cargos",
        "continuidad-supervision-comunicacion", "guia-de-pruebas-cj",
        "archivados/rangos-referencia-constantes", "archivados/decisiones-direccion-basal-derivacion",
    ];

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task SinClave_LasPaginasRedirigenAlFormulario(string name)
    {
        var response = await NewClient().GetAsync($"/pendientes-cj/{name}.html");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/AccesoCj?returnUrl={Uri.EscapeDataString($"/pendientes-cj/{name}.html")}", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("/pendientes-cj/archivados/rangos-referencia-constantes.respuestas.json")]
    [InlineData("/pendientes-cj/archivados/decisiones-direccion-basal-derivacion.respuestas.json")]
    [InlineData("/pendientes-cj/otro-documento.respuestas.json")]
    public async Task SinClave_LasRespuestasDan401(string path)
    {
        var response = await NewClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ElFormulario_PideLaClave_YNoMuestraNingunDocumento()
    {
        var html = await NewClient().GetStringAsync("/AccesoCj?returnUrl=%2Fpendientes-cj%2Fpreguntas.html");

        Assert.Contains("<label for=\"clave\"", html);
        Assert.Contains("type=\"password\"", html);
        Assert.Contains("value=\"/pendientes-cj/preguntas.html\"", html);
        Assert.DoesNotContain("Aclaraciones", html);
    }

    [Fact]
    public async Task ClaveIncorrecta_MuestraElError_SinCookie_YLaCarpetaSigueCerrada()
    {
        var client = NewClient();

        var response = await PostKeyAsync(client, "CJ124");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Clave incorrecta.", html);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/pendientes-cj/index.html")).StatusCode);
    }

    [Fact]
    public async Task ClaveCorrecta_VuelveALaPaginaPedida_YAbreLaCarpeta()
    {
        var client = NewClient();

        var response = await PostKeyAsync(client, Key, "/pendientes-cj/preguntas.html");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/pendientes-cj/preguntas.html", response.Headers.Location!.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("residapp_cj_acceso=") && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/pendientes-cj/preguntas.html")).StatusCode);
    }

    [Theory]
    [InlineData("https://sitio-ajeno.example/pendientes-cj/index.html")]
    [InlineData("//sitio-ajeno.example/pendientes-cj/index.html")]
    [InlineData("/Home/Manual")]
    [InlineData(null)]
    public async Task ClaveCorrecta_SoloVuelveARutasLocalesDeLosDocumentos(string? returnUrl)
    {
        var response = await PostKeyAsync(NewClient(), Key, returnUrl);

        Assert.Equal("/pendientes-cj/index.html", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("residapp_cj_acceso=1")]
    [InlineData("residapp_cj_acceso=zz")]
    [InlineData("residapp_cj_acceso=")]
    [InlineData("residapp_cj_acceso=0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task UnaCookieInventada_NoDaAcceso(string cookie)
    {
        var client = NewClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);

        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/pendientes-cj/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/pendientes-cj/archivados/rangos-referencia-constantes.respuestas.json")).StatusCode);
    }

    [Fact]
    public async Task SinClaveConfigurada_NoEntraNadie_NiConLaClaveDeSiempre()
    {
        var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting("PendientesCj:Clave", ""));
        var client = factory.CreateClient(NoRedirect);

        var response = await PostKeyAsync(client, Key);

        Assert.Contains("Clave incorrecta.", await response.Content.ReadAsStringAsync());
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/pendientes-cj/index.html")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task ConClave_LasPaginasSeSirvenComoHtml(string name)
    {
        var response = await (await LoggedInAsync()).GetAsync($"/pendientes-cj/{name}.html");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ElIndice_ExplicaLasTresSeccionesYEnlazaCadaUna()
    {
        var html = await (await LoggedInAsync()).GetStringAsync("/pendientes-cj/index.html");

        Assert.Contains("tres secciones", html);
        Assert.Contains("href=\"preguntas.html\"", html);
        Assert.Contains("href=\"pruebas.html\"", html);
        Assert.Contains("href=\"archivo.html\"", html);
        Assert.Contains("<h2>Preguntas</h2>", html);
        Assert.Contains("<h2>Pruebas</h2>", html);
        Assert.Contains("<h2>Archivo</h2>", html);
    }

    [Theory]
    [InlineData("preguntas")]
    [InlineData("pruebas")]
    [InlineData("archivo")]
    public async Task CadaSeccion_TieneSuTitulo_YTodosSusEnlacesExisten(string section)
    {
        var client = await LoggedInAsync();

        var html = await client.GetStringAsync($"/pendientes-cj/{section}.html");
        var links = Regex.Matches(html, "<li>\\s*<a href=\"([^\"#]+\\.html)\"").Select(m => m.Groups[1].Value).ToList();

        Assert.Contains($"<h1>{char.ToUpper(section[0])}{section[1..]}</h1>", html);
        Assert.NotEmpty(links);
        foreach (var link in links)
        {
            var response = await client.GetAsync($"/pendientes-cj/{link}");
            Assert.True(response.IsSuccessStatusCode, $"{section}.html enlaza {link}: {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Preguntas_NoIncluyeLosArchivados_YArchivoSi()
    {
        var client = await LoggedInAsync();

        var questions = await client.GetStringAsync("/pendientes-cj/preguntas.html");
        var archive = await client.GetStringAsync("/pendientes-cj/archivo.html");

        Assert.Contains("aclaraciones-respuestas-cj.html", questions);
        Assert.DoesNotContain("rangos-referencia-constantes", questions);
        Assert.DoesNotContain("decisiones-direccion-basal-derivacion", questions);
        Assert.Contains("archivados/rangos-referencia-constantes.html", archive);
        Assert.Contains("archivados/decisiones-direccion-basal-derivacion.html", archive);
    }

    [Theory]
    [InlineData("rangos-referencia-constantes", 12)]
    [InlineData("decisiones-direccion-basal-derivacion", 7)]
    public async Task LosArchivados_TienenSusRespuestasAlLadoParaQueSeAbranContestados(string document, int answered)
    {
        var client = await LoggedInAsync();

        var json = await client.GetStringAsync($"/pendientes-cj/archivados/{document}.respuestas.json");
        var page = await client.GetStringAsync($"/pendientes-cj/archivados/{document}.html");

        Assert.Contains($"\"documento\": \"{document}\"", json);
        Assert.Contains($"\"respondidas\": {answered}", json);
        Assert.Contains($"data-doc=\"{document}\"", page);
        Assert.Contains("Archivado el 07/10/2026", page);
    }

    [Theory]
    [InlineData("rangos-referencia-constantes")]
    [InlineData("decisiones-direccion-basal-derivacion")]
    public async Task LosArchivados_LlevanArribaLosEnlacesAlIndiceYALaAplicacion(string document)
    {
        var page = await (await LoggedInAsync()).GetStringAsync($"/pendientes-cj/archivados/{document}.html");

        var nav = Regex.Match(page, "<nav class=\"back\"[^>]*>(.*?)</nav>", RegexOptions.Singleline);
        Assert.True(nav.Success, "Falta el bloque de enlaces.");
        Assert.Contains("<a href=\"../index.html\">← Documentos para CJ</a>", nav.Groups[1].Value);
        Assert.Contains("<a href=\"/\">Volver a ResidApp</a>", nav.Groups[1].Value);
        Assert.True(nav.Index < page.IndexOf("<h1>", StringComparison.Ordinal), "Los enlaces van arriba, antes del título.");
    }

    [Theory]
    [InlineData("/pendientes-cj/rangos-referencia-constantes.html")]
    [InlineData("/pendientes-cj/decisiones-direccion-basal-derivacion.html")]
    [InlineData("/pendientes-cj/rangos-referencia-constantes.respuestas.json")]
    public async Task LasRutasAntiguasDeLosArchivados_YaNoExisten(string path)
    {
        var response = await (await LoggedInAsync()).GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Salir_CierraLaCarpetaDeNuevo()
    {
        var client = await LoggedInAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/pendientes-cj/index.html")).StatusCode);

        var exit = await client.GetAsync("/AccesoCj/Salir");

        Assert.Equal(HttpStatusCode.Redirect, exit.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/pendientes-cj/index.html")).StatusCode);
    }

    [Fact]
    public async Task Footer_LinksToTheIndex()
    {
        var html = await _factory.CreateClient().GetStringAsync("/DevAuth/Login");

        Assert.Contains("href=\"/pendientes-cj/index.html\"", html);
    }
}
