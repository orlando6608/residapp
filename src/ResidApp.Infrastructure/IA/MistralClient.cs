using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResidApp.Application.Ports;

namespace ResidApp.Infrastructure.IA;

/// <summary>
/// El único adaptador de IA (ADR 0011): transcripción con Voxtral y salida estructurada con Mistral Small, sobre un <see cref="HttpClient"/> tipado
/// (la clave y la dirección se fijan al registrarlo). Un tiempo de espera y <b>un</b> reintento, solo ante un fallo transitorio (429, 5xx, red o tiempo
/// agotado). Registra el modelo, el tamaño del audio, la duración y los tokens; <b>nunca</b> el audio, el texto ni el prompt, ni el cuerpo de un error.
/// Cualquier fallo sale como «no disponible» (VOICE_DICTATION_UNAVAILABLE), sin detalles que puedan llevar contenido.
/// </summary>
public sealed class MistralClient(HttpClient http, IOptions<IaOptions> options, ILogger<MistralClient> logger) : ISpeechTranscriber, IStructuredGenerator
{
    public const string UnavailableCode = "VOICE_DICTATION_UNAVAILABLE";

    private static readonly ConcurrentDictionary<string, string> Prompts = new();

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<string> TranscribeAsync(Stream audio, string contentType, string language, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await audio.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var model = options.Value.Mistral.ModeloVoz;
        var fileName = "audio" + ExtensionOf(contentType);

        var body = await SendAsync("transcripcion", model, bytes.Length, "v1/audio/transcriptions", () =>
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            return new MultipartFormDataContent
            {
                { new StringContent(model), "model" },
                { new StringContent(language), "language" },
                { file, "file", fileName },
            };
        }, cancellationToken);

        var text = JsonNode.Parse(body)?["text"]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(text) ? throw new InvalidOperationException(UnavailableCode) : text.Trim();
    }

    public async Task<T?> GenerateAsync<T>(string promptName, string input, JsonObject schema, CancellationToken cancellationToken) where T : class
    {
        var model = options.Value.Mistral.ModeloTexto;
        var request = new JsonObject
        {
            ["model"] = model,
            ["temperature"] = 0,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = Prompt(promptName) },
                new JsonObject { ["role"] = "user", ["content"] = input }),
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = promptName.Replace('-', '_'), ["strict"] = true, ["schema"] = schema.DeepClone() },
            },
        };
        var json = request.ToJsonString();

        var body = await SendAsync("estructuracion", model, 0, "v1/chat/completions",
            () => new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);

        var content = JsonNode.Parse(body)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(content, ReadOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string> SendAsync(
        string operation, string model, long audioBytes, string route, Func<HttpContent> content, CancellationToken ct)
    {
        var wait = TimeSpan.FromMilliseconds(options.Value.Mistral.EsperaReintentoMilisegundos);
        var clock = Stopwatch.StartNew();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = content();
                using var response = await http.PostAsync(route, request, ct);
                var status = (int)response.StatusCode;
                var transient = status == 429 || status >= 500;
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    Log(operation, model, audioBytes, clock.ElapsedMilliseconds, status, body);
                    return body;
                }
                if (!transient || attempt == 2)
                {
                    logger.LogWarning("IA {Operacion}: modelo {Modelo}, respuesta {Estado}, {Ms} ms", operation, model, status, clock.ElapsedMilliseconds);
                    throw new InvalidOperationException(UnavailableCode);
                }
                logger.LogInformation("IA {Operacion}: respuesta {Estado}, se reintenta una vez", operation, status);
            }
            catch (HttpRequestException) when (attempt == 1)
            {
                logger.LogInformation("IA {Operacion}: fallo de red, se reintenta una vez", operation);
            }
            catch (TaskCanceledException) when (attempt == 1 && !ct.IsCancellationRequested)
            {
                logger.LogInformation("IA {Operacion}: tiempo agotado, se reintenta una vez", operation);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning("IA {Operacion}: modelo {Modelo}, fallo definitivo tras el reintento", operation, model);
                throw new InvalidOperationException(UnavailableCode);
            }
            await Task.Delay(wait, ct);
        }
    }

    private void Log(string operation, string model, long audioBytes, long milliseconds, int status, string body)
    {
        long? promptTokens = null, completionTokens = null;
        try
        {
            var usage = JsonNode.Parse(body)?["usage"];
            promptTokens = usage?["prompt_tokens"]?.GetValue<long>();
            completionTokens = usage?["completion_tokens"]?.GetValue<long>();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // Sin cifras de uso: no es un fallo.
        }
        logger.LogInformation(
            "IA {Operacion}: modelo {Modelo}, respuesta {Estado}, {Bytes} bytes de audio, {Ms} ms, tokens {Entrada}/{Salida}",
            operation, model, status, audioBytes, milliseconds, promptTokens, completionTokens);
    }

    private static string Prompt(string name) => Prompts.GetOrAdd(name, key =>
    {
        var resource = $"ResidApp.IA.Prompts.{key}.md";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"No existe el prompt incrustado «{resource}».");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    });

    private static string ExtensionOf(string contentType) => contentType.ToLowerInvariant() switch
    {
        "audio/webm" => ".webm",
        "audio/ogg" => ".ogg",
        "audio/mp4" => ".mp4",
        "audio/mpeg" => ".mp3",
        _ => ".wav",
    };
}
