using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResidApp.Application.UseCases;
using ResidApp.Infrastructure.IA;

namespace ResidApp.IntegrationTests;

/// <summary>El adaptador de Mistral (ADR 0011) contra un servidor falso: lo que envía, el reintento único y que el registro no lleve contenido. No hay
/// ninguna llamada real a internet.</summary>
public class MistralClientTests
{
    private const string Transcripcion = "TEXTO-DICTADO-QUE-NO-DEBE-SALIR-EN-EL-LOG";

    private static string ChatBody(string content) =>
        new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["content"] = content } }),
            ["usage"] = new JsonObject { ["prompt_tokens"] = 120, ["completion_tokens"] = 30 },
        }.ToJsonString();

    private static (MistralClient Client, StubHandler Handler, CapturingLogger Log) Build(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
    {
        var handler = new StubHandler(responses);
        var log = new CapturingLogger();
        var options = new IaOptions { Mistral = { ApiKey = "clave-de-prueba", EsperaReintentoMilisegundos = 0 } };
        var http = new HttpClient(handler) { BaseAddress = new Uri(MistralOptions.BaseUrl) };
        return (new MistralClient(http, Options.Create(options), log), handler, log);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Transcribir_EnviaModeloIdiomaYAudio_YDevuelveElTextoSinEspacios()
    {
        var (client, handler, _) = Build(_ => Json(HttpStatusCode.OK, $$"""{"text":"  {{Transcripcion}}  "}"""));

        var text = await client.TranscribeAsync(new MemoryStream([1, 2, 3, 4]), "audio/webm", "es", default);

        Assert.Equal(Transcripcion, text);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v1/audio/transcriptions", request.Path);
        Assert.Contains("name=model", request.Body);
        Assert.Contains("voxtral-mini-latest", request.Body);
        Assert.Contains("name=language", request.Body);
        Assert.Contains("filename=audio.webm", request.Body);
        Assert.Contains("Content-Type: audio/webm", request.Body);
    }

    [Fact]
    public async Task Estructurar_EnviaElPromptIncrustadoYElEsquemaEstricto_YDeserializaLaRespuesta()
    {
        var (client, handler, _) = Build(_ => Json(HttpStatusCode.OK, ChatBody("""{"areas":[{"area":"SUENO","texto":"no ha dormido","opciones":["INSOMNIO"]}]}""")));
        var schema = DailyChangeDictationSchema.Build();

        var output = await client.GenerateAsync<VoiceModelOutput>("registrar-cambio", "no ha dormido nada", schema, default);

        var area = Assert.Single(output!.Areas!);
        Assert.Equal(("SUENO", "no ha dormido", "INSOMNIO"), (area.Area, area.Texto, area.Opciones!.Single()));
        var body = JsonNode.Parse(Assert.Single(handler.Requests).Body)!;
        Assert.Equal("mistral-small-latest", body["model"]!.GetValue<string>());
        Assert.Equal(0, body["temperature"]!.GetValue<int>());
        Assert.True(body["response_format"]!["json_schema"]!["strict"]!.GetValue<bool>());
        Assert.Equal(schema.ToJsonString(), body["response_format"]!["json_schema"]!["schema"]!.ToJsonString());
        Assert.Contains("Registrar cambio", body["messages"]![0]!["content"]!.GetValue<string>());
        Assert.DoesNotContain("temperatura", body["messages"]![0]!["content"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("no ha dormido nada", body["messages"]![1]!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Estructurar_SiElModeloNoContestaConElEsquema_DevuelveNulo()
    {
        var (client, _, _) = Build(_ => Json(HttpStatusCode.OK, ChatBody("esto no es JSON")));

        Assert.Null(await client.GenerateAsync<VoiceModelOutput>("registrar-cambio", "hola", DailyChangeDictationSchema.Build(), default));
    }

    [Fact]
    public async Task Reintenta_UnaSolaVez_AnteUnLimiteDeTasaOUnErrorDelServidor()
    {
        var (client, handler, _) = Build(
            _ => Json(HttpStatusCode.TooManyRequests, "{}"), _ => Json(HttpStatusCode.OK, $$"""{"text":"{{Transcripcion}}"}"""));

        Assert.Equal(Transcripcion, await client.TranscribeAsync(new MemoryStream([1]), "audio/webm", "es", default));
        Assert.Equal(2, handler.Requests.Count);

        var (cae, caeHandler, _) = Build(_ => Json(HttpStatusCode.InternalServerError, "{}"), _ => Json(HttpStatusCode.BadGateway, "{}"), _ => Json(HttpStatusCode.OK, "{}"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => cae.TranscribeAsync(new MemoryStream([1]), "audio/webm", "es", default));
        Assert.Equal(MistralClient.UnavailableCode, error.Message);
        Assert.Equal(2, caeHandler.Requests.Count);
    }

    [Fact]
    public async Task NoReintenta_AnteUnErrorDeLaPeticion_YSeReintentaUnFalloDeRed()
    {
        var (rechazada, rechazadaHandler, _) = Build(_ => Json(HttpStatusCode.BadRequest, "{}"), _ => Json(HttpStatusCode.OK, "{}"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rechazada.TranscribeAsync(new MemoryStream([1]), "audio/webm", "es", default));
        Assert.Single(rechazadaHandler.Requests);

        var (red, redHandler, _) = Build(_ => throw new HttpRequestException("sin red"), _ => Json(HttpStatusCode.OK, $$"""{"text":"{{Transcripcion}}"}"""));
        Assert.Equal(Transcripcion, await red.TranscribeAsync(new MemoryStream([1]), "audio/webm", "es", default));
        Assert.Equal(2, redHandler.Requests.Count);

        var (caida, caidaHandler, _) = Build(_ => throw new HttpRequestException("sin red"), _ => throw new HttpRequestException("sin red"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => caida.TranscribeAsync(new MemoryStream([1]), "audio/webm", "es", default));
        Assert.Equal(MistralClient.UnavailableCode, error.Message);
        Assert.Equal(2, caidaHandler.Requests.Count);
    }

    [Fact]
    public async Task Transcribir_SinTexto_NoEstaDisponible()
    {
        var (client, _, _) = Build(_ => Json(HttpStatusCode.OK, """{"text":"   "}"""));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.TranscribeAsync(new MemoryStream([1]), "audio/webm", "es", default));
    }

    [Fact]
    public async Task ElRegistro_LlevaModeloDuracionYTokens_PeroNuncaElContenido()
    {
        var (client, _, log) = Build(
            _ => Json(HttpStatusCode.OK, new JsonObject { ["text"] = Transcripcion, ["usage"] = new JsonObject { ["prompt_tokens"] = 7 } }.ToJsonString()),
            _ => Json(HttpStatusCode.OK, ChatBody($$"""{"areas":[{"area":"SUENO","texto":"{{Transcripcion}}","opciones":[]}]}""")),
            _ => Json(HttpStatusCode.BadRequest, $$"""{"message":"eco: {{Transcripcion}}"}"""));

        await client.TranscribeAsync(new MemoryStream([1, 2, 3]), "audio/webm", "es", default);
        await client.GenerateAsync<VoiceModelOutput>("registrar-cambio", Transcripcion, DailyChangeDictationSchema.Build(), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.TranscribeAsync(new MemoryStream([1]), "audio/webm", "es", default));

        var text = log.Text;
        Assert.Contains("voxtral-mini-latest", text);
        Assert.Contains("mistral-small-latest", text);
        Assert.Contains("3 bytes de audio", text);
        Assert.Contains("120/30", text);
        Assert.DoesNotContain("TEXTO-DICTADO", text);
        Assert.DoesNotContain("eco:", text);
        Assert.DoesNotContain("Registrar cambio", text);
    }

    [Fact]
    public void LosProveedoresAutorizados_SoloAdmitenMistral_YSinClaveElDictadoEstaDesactivado()
    {
        IaOptions.Validate(new IaOptions());
        IaOptions.Validate(new IaOptions { Proveedor = "mistral" });
        foreach (var otro in new[] { "OpenAI", "Claude", "", "Ollama" })
        {
            var error = Assert.Throws<InvalidOperationException>(() => IaOptions.Validate(new IaOptions { Proveedor = otro }));
            Assert.Contains("ADR", error.Message);
        }

        Assert.False(new IaOptions().Activa);
        Assert.False(new IaOptions { Mistral = { ApiKey = "  " } }.Activa);
        Assert.True(new IaOptions { Mistral = { ApiKey = "clave" } }.Activa);
    }

    private sealed record Seen(string Path, string Body);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Seen(request.RequestUri!.AbsolutePath, body));
            return responses[Math.Min(Requests.Count, responses.Length) - 1](request);
        }
    }

    private sealed class CapturingLogger : ILogger<MistralClient>
    {
        private readonly List<string> _lines = [];

        public string Text => string.Join('\n', _lines);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _lines.Add(formatter(state, exception) + (exception is null ? "" : " " + exception));
    }
}
