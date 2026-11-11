using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ResidApp.FunctionalTests;

/// <summary>Las respuestas de CJ se guardan en el servidor (/pendientes-cj/api/respuestas/{documento}): un JSON por documento que cada guardado sustituye. Detrás de la
/// misma clave que los documentos. La carpeta de cada prueba es una temporal.</summary>
public sealed class RespuestasCjTests : IClassFixture<ResidentsFlowTests.WebAppFactory>, IDisposable
{
    private const string Documento = "pruebas-2-medicina-indicaciones";
    private const string Url = "/pendientes-cj/api/respuestas/" + Documento;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "residapp-respuestas-cj-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;

    public RespuestasCjTests(ResidentsFlowTests.WebAppFactory factory) =>
        _factory = factory.WithWebHostBuilder(builder => builder.UseSetting("PendientesCj:CarpetaRespuestas", _folder));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<HttpClient> LoggedInAsync()
    {
        var client = NewClient();
        var form = await client.GetStringAsync("/AccesoCj");
        var token = Regex.Match(form, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;
        var response = await client.PostAsync("/AccesoCj", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["clave"] = "CJ123",
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private static string Json(string texto, string documento = Documento) =>
        "{\"documento\":\"" + documento + "\",\"cuestionario\":\"2026-11-08\",\"guardado\":\"2026-11-11T10:00:00.000Z\",\"respondidas\":1,\"total\":13,"
        + "\"respuestas\":{\"1\":{\"pregunta\":\"P\",\"opcion\":\"A\",\"texto\":\"" + texto + "\",\"respondida\":true}}}";

    private static StringContent Body(string json, string mediaType = "application/json") => new(json, Encoding.UTF8, mediaType);

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task SinClave_LaApiDa401_YNoGuardaNada(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), Url) { Content = method == "PUT" ? Body(Json("no")) : null };

        var response = await NewClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(Directory.Exists(_folder) && Directory.EnumerateFileSystemEntries(_folder).Any());
    }

    [Fact]
    public async Task SinNadaGuardado_ElGetDa204()
    {
        var response = await (await LoggedInAsync()).GetAsync(Url);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Guardar_DevuelveLoGuardado_Con_Tildes()
    {
        var client = await LoggedInAsync();
        var json = Json("Funciona, pero no me convence: ¿añadís más avisos?");

        var put = await client.PutAsync(Url, Body(json));
        var get = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("application/json", get.Content.Headers.ContentType!.MediaType);
        Assert.Equal(json, await get.Content.ReadAsStringAsync());
        Assert.Contains("no-store", get.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task UnSegundoGuardado_SustituyeAlPrimero_Y_ConservaSoloLaVersionAnterior()
    {
        var client = await LoggedInAsync();
        var first = Json("primera");
        var second = Json("segunda");
        var third = Json("tercera");

        await client.PutAsync(Url, Body(first));
        await client.PutAsync(Url, Body(second));
        Assert.Equal(first, await File.ReadAllTextAsync(Path.Combine(_folder, Documento + ".respuestas.anterior.json")));
        await client.PutAsync(Url, Body(third));

        Assert.Equal(third, await (await client.GetAsync(Url)).Content.ReadAsStringAsync());
        Assert.Equal(second, await File.ReadAllTextAsync(Path.Combine(_folder, Documento + ".respuestas.anterior.json")));
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Theory]
    [InlineData("no-existe")]
    [InlineData("archivados")]
    [InlineData("index")]
    [InlineData("orlando")]
    [InlineData("pruebas-1-auxiliar-enfermeria-flujo")]
    [InlineData("PRUEBAS-2-MEDICINA-INDICACIONES")]
    [InlineData("pruebas-2-medicina-indicaciones.respuestas")]
    [InlineData("..%2Fappsettings")]
    public async Task UnDocumentoQueNoEs_DeLaRaizDeCj_Da404_ParaLeerYParaGuardar(string documento)
    {
        var client = await LoggedInAsync();
        var url = "/pendientes-cj/api/respuestas/" + documento;

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync(url, Body(Json("x", documento)))).StatusCode);
        Assert.False(Directory.Exists(_folder) && Directory.EnumerateFileSystemEntries(_folder).Any());
    }

    [Fact]
    public async Task UnCuerpoQueNoSirve_SeRechaza_Y_LoGuardadoSeQueda()
    {
        var client = await LoggedInAsync();
        var good = Json("buena");
        await client.PutAsync(Url, Body(good));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PutAsync(Url, Body(Json("x"), "text/plain"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync(Url, Body("{ esto no es json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync(Url, Body("[1,2]"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync(Url, Body(Json("x", "pruebas-1-auxiliar-enfermeria")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync(Url, Body("{\"documento\":\"" + Documento + "\"}"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync(Url, Body("{\"documento\":\"" + Documento + "\",\"respuestas\":[]}"))).StatusCode);

        Assert.Equal(good, await (await client.GetAsync(Url)).Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("confirmaciones-tercera-tanda-cj")]
    [InlineData("direccion-comunicados-flujo-cj")]
    [InlineData("iconos-pantallas-cj")]
    [InlineData("modulos-base-y-opcionales-cj")]
    [InlineData("portal-familiar-confirmaciones-cj")]
    [InlineData("portal-familiar-dos-idiomas-cj")]
    [InlineData("pruebas-1-auxiliar-enfermeria")]
    [InlineData("pruebas-2-medicina-indicaciones")]
    [InlineData("pruebas-3-basal-barthel")]
    [InlineData("pruebas-4-direccion")]
    [InlineData("pruebas-5-administracion")]
    [InlineData("pruebas-6-comunicados-portal")]
    [InlineData("pruebas-7-citas")]
    [InlineData("pruebas-8-aislamiento")]
    public async Task CadaDocumento_TieneSuFicheroDeNotas_QueSoloSeSirveConLaClave(string document)
    {
        var url = $"/pendientes-cj/{document}.notas.json";

        Assert.Equal(HttpStatusCode.Unauthorized, (await NewClient().GetAsync(url)).StatusCode);
        var response = await (await LoggedInAsync()).GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains($"\"documento\": \"{document}\"", json);
        Assert.Contains("\"notas\"", json);
    }

    [Theory]
    [InlineData("index")]
    [InlineData("preguntas")]
    [InlineData("pruebas")]
    [InlineData("pruebas-1-auxiliar-enfermeria")]
    [InlineData("pruebas-2-medicina-indicaciones")]
    [InlineData("pruebas-8-aislamiento")]
    [InlineData("modulos-base-y-opcionales-cj")]
    [InlineData("iconos-pantallas-cj")]
    public async Task LasPaginasDeCj_YaNoMandanGuardarNiEnviarUnFichero(string page)
    {
        var html = await (await LoggedInAsync()).GetStringAsync($"/pendientes-cj/{page}.html");

        Assert.DoesNotContain("Guardar respuestas", html);
        Assert.DoesNotContain("Abrir respuestas guardadas", html);
        Assert.DoesNotContain("Envíaselo a Orlando", html);
        Assert.DoesNotContain("envía el fichero", html);
        Assert.DoesNotContain("archivo.html", html);
    }

    [Fact]
    public async Task ElLimiteDeTamano_EsExacto_EnBytes()
    {
        var client = await LoggedInAsync();
        var overhead = Encoding.UTF8.GetByteCount(Json(""));
        var exact = Json(new string('á', (256 * 1024 - overhead) / 2)); // 'á' son 2 bytes: el límite cuenta bytes, no caracteres
        var size = Encoding.UTF8.GetByteCount(exact);

        Assert.True(size <= 256 * 1024);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync(Url, Body(exact))).StatusCode);

        var over = Json(new string('á', 256 * 1024 / 2)); // más de 256 KB en bytes aunque no en caracteres
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PutAsync(Url, Body(over))).StatusCode);
    }

    [Fact]
    public async Task UnaCookieInventada_NoDaAccesoALaApi_NiTrasSalir()
    {
        var forged = NewClient();
        forged.DefaultRequestHeaders.Add("Cookie", "residapp_cj_acceso=0000000000000000000000000000000000000000000000000000000000000000");
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.PutAsync(Url, Body(Json("x")))).StatusCode);

        var client = await LoggedInAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync(Url, Body(Json("antes")))).StatusCode);
        await client.GetAsync("/AccesoCj/Salir");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsync(Url, Body(Json("despues")))).StatusCode);
    }

    [Fact]
    public async Task GuardarEnUnDocumento_NoTocaOtro()
    {
        var client = await LoggedInAsync();
        var other = "/pendientes-cj/api/respuestas/pruebas-1-auxiliar-enfermeria";

        await client.PutAsync(Url, Body(Json("de-la-guia-2")));

        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync(other)).StatusCode);
    }

    [Fact]
    public async Task VariosGuardadosAlMismoTiempo_DejanUnJsonValido_SinTemporales_Y_ElAvisoCompleto()
    {
        var client = await LoggedInAsync();
        var documents = new[] { "pruebas-1-auxiliar-enfermeria", "pruebas-2-medicina-indicaciones", "pruebas-3-basal-barthel", "iconos-pantallas-cj" };

        var responses = await Task.WhenAll(Enumerable.Range(0, 40).Select(i =>
        {
            var document = documents[i % documents.Length];
            return client.PutAsync("/pendientes-cj/api/respuestas/" + document, Body(Json("texto " + i, document)));
        }));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
        foreach (var document in documents)
        {
            using var parsed = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_folder, document + ".respuestas.json")));
            Assert.Equal(document, parsed.RootElement.GetProperty("documento").GetString());
        }

        var pending = await File.ReadAllTextAsync(Path.Combine(_folder, "aviso-pendiente.json"));
        Assert.All(documents, document => Assert.Contains(document, pending));
    }

    [Fact]
    public async Task LaCargaDeRespuestasAntiguas_NoDejaAvisoPendiente()
    {
        var client = await LoggedInAsync();
        var request = new HttpRequestMessage(HttpMethod.Put, Url) { Content = Body(Json("antigua")) };
        request.Headers.Add("X-Sin-Aviso", "1");

        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request)).StatusCode);

        Assert.False(File.Exists(Path.Combine(_folder, "aviso-pendiente.json")));
        Assert.True(File.Exists(Path.Combine(_folder, Documento + ".respuestas.json")));
    }

    [Fact]
    public async Task UnCuerpoDemasiadoGrande_SeRechaza()
    {
        var client = await LoggedInAsync();

        var response = await client.PutAsync(Url, Body(Json(new string('a', 300 * 1024))));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.False(Directory.Exists(_folder) && Directory.EnumerateFileSystemEntries(_folder).Any());
    }
}
