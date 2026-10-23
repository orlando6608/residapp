using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using ResidApp.Domain.Auxiliar;
using ResidApp.Shared;

// Prueba de precisión del dictado (ADR 0009), aislada de la aplicación. Dos usos:
//   dotnet run --project tools/PruebaDictado -- generar <casos.json> <carpeta-audios>
//       genera con Voxtral TTS un audio <id>.wav por cada caso (la voz Elena, la única en español)
//   dotnet run --project tools/PruebaDictado -- <casos.json> [salida.json] [--voz <carpeta-audios>]
//       evalúa: con --voz transcribe <carpeta>/<id>.(wav|mp3|opus|...) y, sin ella, usa el texto del caso
// La clave va en `dotnet user-secrets --project tools/PruebaDictado set Mistral:ApiKey <clave>`
// o en la variable de entorno Mistral__ApiKey. Ver tools/PruebaDictado/README.md.

var lista = args.ToList();
var generar = lista.Count > 0 && lista[0] == "generar";
if (generar) lista.RemoveAt(0);
string? carpetaVoz = null;
var iVoz = lista.IndexOf("--voz");
if (iVoz >= 0 && iVoz + 1 < lista.Count) { carpetaVoz = Path.GetFullPath(lista[iVoz + 1]); lista.RemoveRange(iVoz, 2); }
if (lista.Count < 1 || (generar && lista.Count < 2))
{
    Console.Error.WriteLine("Uso: PruebaDictado generar <casos.json> <carpeta>  |  PruebaDictado <casos.json> [salida.json] [--voz <carpeta>]");
    return 2;
}

var config = new ConfigurationBuilder().AddUserSecrets<Caso>().AddEnvironmentVariables().Build();
var apiKey = config["Mistral:ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey)) { Console.Error.WriteLine("Falta Mistral:ApiKey (user-secrets o variable Mistral__ApiKey)."); return 2; }
var modeloVoz = config["Mistral:ModeloVoz"] ?? "voxtral-mini-latest";
var modeloTexto = config["Mistral:ModeloTexto"] ?? "mistral-small-latest";
// «voxtral-mini-tts-latest» apunta a una versión sin voces predefinidas ("voice is not supported for speech v1").
var modeloTts = config["Mistral:ModeloTts"] ?? "voxtral-mini-tts-3";
// Elena: voz predefinida pública de Mistral y la única en español (GET v1/audio/voices).
var vozTts = config["Mistral:VozTts"] ?? "01a0f318-4afd-77a5-aa5e-f4d741c30187";

var rutaCasos = Path.GetFullPath(lista[0]);
var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var casos = JsonSerializer.Deserialize<List<Caso>>(File.ReadAllText(rutaCasos), json) ?? [];

using var http = new HttpClient { BaseAddress = new Uri("https://api.mistral.ai/"), Timeout = TimeSpan.FromSeconds(120) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

// Mistral limita las peticiones por segundo (429): se reintenta esperando, hasta 6 veces.
async Task<(int Estado, string Cuerpo)> Enviar(string ruta, Func<HttpContent> contenido)
{
    for (var intento = 1; ; intento++)
    {
        using var contenidoPeticion = contenido();
        using var resp = await http.PostAsync(ruta, contenidoPeticion);
        var cuerpo = await resp.Content.ReadAsStringAsync();
        if ((int)resp.StatusCode != 429 || intento == 6) return ((int)resp.StatusCode, cuerpo);
        await Task.Delay(TimeSpan.FromSeconds(5 * intento));
    }
}

if (generar)
{
    var carpeta = Path.GetFullPath(lista[1]);
    Directory.CreateDirectory(carpeta);
    var fallos = 0;
    foreach (var caso in casos)
    {
        var peticion = new JsonObject
        {
            ["model"] = modeloTts,
            ["input"] = caso.Texto,
            ["voice_id"] = vozTts,
            ["response_format"] = "wav",
        };
        var (estado, cuerpo) = await Enviar("v1/audio/speech",
            () => new StringContent(peticion.ToJsonString(), Encoding.UTF8, "application/json"));
        if (estado != 200) { fallos++; Console.WriteLine($"{caso.Id,-6} ERROR {estado}: {cuerpo}"); continue; }
        File.WriteAllBytes(Path.Combine(carpeta, caso.Id + ".wav"), Convert.FromBase64String(JsonNode.Parse(cuerpo)!["audio_data"]!.GetValue<string>()));
        Console.WriteLine($"{caso.Id,-6} ok");
    }
    return fallos == 0 ? 0 : 1;
}

// Variantes a comparar: Mistral:Prompt (nombre del fichero de Prompts, sin .md) y Mistral:EsquemaPorArea (true/false).
var nombrePrompt = config["Mistral:Prompt"] ?? "registrar-cambio-v2";
var porArea = !string.Equals(config["Mistral:EsquemaPorArea"], "false", StringComparison.OrdinalIgnoreCase);
Console.WriteLine($"Texto: {modeloTexto}, prompt: {nombrePrompt}, esquema por área: {porArea}, voz: {carpetaVoz ?? "(texto escrito)"}");
var esquema = Esquema.Construir(porArea);
var instrucciones = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Prompts", nombrePrompt + ".md"));
var resultados = new List<Resultado>();

foreach (var caso in casos)
{
    var r = new Resultado { Id = caso.Id, Dudoso = caso.Dudoso == true };
    try
    {
        string transcripcion;
        if (carpetaVoz is not null)
        {
            var ruta = Directory.GetFiles(carpetaVoz, caso.Id + ".*").FirstOrDefault()
                ?? throw new Exception($"No hay audio {caso.Id}.* en {carpetaVoz}.");
            var bytes = File.ReadAllBytes(ruta);
            var (estado, cuerpo) = await Enviar("v1/audio/transcriptions", () =>
            {
                var form = new MultipartFormDataContent();
                form.Add(new StringContent(modeloVoz), "model");
                form.Add(new StringContent("es"), "language");
                form.Add(new ByteArrayContent(bytes), "file", Path.GetFileName(ruta));
                return form;
            });
            if (estado != 200) throw new Exception($"Transcripción {estado}: {cuerpo}");
            transcripcion = JsonNode.Parse(cuerpo)!["text"]!.GetValue<string>();
            r.Palabras = Evaluador.Palabras(caso.Texto ?? "");
            r.ErroresPalabras = Evaluador.Distancia(caso.Texto ?? "", transcripcion);
            r.JergaPerdida = (caso.Jerga ?? []).Where(j => !Evaluador.Normalizar(transcripcion).Contains(Evaluador.Normalizar(j))).ToList();
        }
        else transcripcion = caso.Texto ?? throw new Exception("El caso no tiene texto.");
        r.Transcripcion = transcripcion;

        var peticion = new JsonObject
        {
            ["model"] = modeloTexto,
            ["temperature"] = 0,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = instrucciones },
                new JsonObject { ["role"] = "user", ["content"] = transcripcion }),
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = "registrar_cambio", ["strict"] = true, ["schema"] = esquema.DeepClone() },
            },
        };
        var (estado2, cuerpo2) = await Enviar("v1/chat/completions",
            () => new StringContent(peticion.ToJsonString(), Encoding.UTF8, "application/json"));
        if (estado2 != 200) throw new Exception($"Estructuración {estado2}: {cuerpo2}");
        var contenido = JsonNode.Parse(cuerpo2)!["choices"]![0]!["message"]!["content"]!.GetValue<string>();
        r.Sugerencia = JsonSerializer.Deserialize<Sugerencia>(contenido, json);
        Evaluador.Evaluar(caso, r);
    }
    catch (Exception ex) { r.Error = ex.Message; }
    resultados.Add(r);
    var voz = r.Palabras > 0 ? $"  palabras mal {r.ErroresPalabras}/{r.Palabras}  jerga perdida [{string.Join(", ", r.JergaPerdida ?? [])}]" : "";
    Console.WriteLine($"{r.Id,-6} {(r.Error is null ? $"áreas {r.AreasOk,-5} opciones {r.OpcionesOk,-5} temp {r.TemperaturaOk,-5} inválidas {r.Invalidas} fuga {r.Fuga}{(r.Dudoso ? "  (dudoso)" : "")}{voz}" : "ERROR " + r.Error)}");
}

var ok = resultados.Where(r => r.Error is null).ToList();
if (ok.Count > 0)
{
    void Resumen(string titulo, List<Resultado> rs)
    {
        var conTemp = rs.Where(r => r.TemperaturaOk is not null).ToList();
        Console.WriteLine($"{titulo} ({rs.Count} casos): áreas {rs.Count(r => r.AreasOk == true)}, opciones {rs.Count(r => r.OpcionesOk == true)}, " +
            $"temperatura {conTemp.Count(r => r.TemperaturaOk == true)}/{conTemp.Count}");
    }
    Console.WriteLine();
    Console.WriteLine($"Casos: {resultados.Count}, con error: {resultados.Count - ok.Count}");
    Resumen("Todos", ok);
    Resumen("Sin dudosos", ok.Where(r => !r.Dudoso).ToList());
    Console.WriteLine($"Sugerencias inválidas (opción fuera del área, área repetida): {ok.Sum(r => r.Invalidas)}");
    Console.WriteLine($"Fugas de nombre en el resultado: {ok.Count(r => r.Fuga)}");
    if (carpetaVoz is not null)
    {
        var palabras = ok.Sum(r => r.Palabras);
        Console.WriteLine($"Voz: {ok.Sum(r => r.ErroresPalabras)}/{palabras} palabras distintas del texto original " +
            $"({(palabras == 0 ? 0 : 100.0 * ok.Sum(r => r.ErroresPalabras) / palabras).ToString("0.0", CultureInfo.InvariantCulture)} %); " +
            $"jerga perdida en {ok.Count(r => r.JergaPerdida?.Count > 0)} casos");
    }
}
if (lista.Count > 1)
    File.WriteAllText(lista[1], JsonSerializer.Serialize(resultados, new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    }));
return 0;

// ---- tipos ----

record Caso(string Id, string? Audio, string? Texto, Esperado Esperado, List<string>? Jerga = null, bool? Dudoso = null,
    string? Nota = null, List<string>? NoDebeAparecer = null);
record Esperado(List<AreaSug>? Areas, decimal? Temperatura);
record AreaSug(string Area, string? Texto, List<string>? Opciones);

class Sugerencia
{
    public List<AreaSug>? Areas { get; set; }
    public decimal? Temperatura { get; set; }
}

class Resultado
{
    public string Id { get; set; } = "";
    public bool Dudoso { get; set; }
    public string? Transcripcion { get; set; }
    public Sugerencia? Sugerencia { get; set; }
    public bool? AreasOk { get; set; }
    public bool? OpcionesOk { get; set; }
    public bool? TemperaturaOk { get; set; }
    public int Invalidas { get; set; }
    public bool Fuga { get; set; }
    public int Palabras { get; set; }
    public int ErroresPalabras { get; set; }
    public List<string>? JergaPerdida { get; set; }
    public string? Error { get; set; }
}

static class Evaluador
{
    public static string Normalizar(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static int Palabras(string s) => Normalizar(s).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Distancia de edición entre palabras (sustituciones, altas y bajas) del texto original y el transcrito.</summary>
    public static int Distancia(string a, string b)
    {
        var x = Normalizar(a).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var y = Normalizar(b).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var d = new int[x.Length + 1, y.Length + 1];
        for (var i = 0; i <= x.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= y.Length; j++) d[0, j] = j;
        for (var i = 1; i <= x.Length; i++)
            for (var j = 1; j <= y.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (x[i - 1] == y[j - 1] ? 0 : 1));
        return d[x.Length, y.Length];
    }

    public static void Evaluar(Caso caso, Resultado r)
    {
        var esp = caso.Esperado.Areas ?? [];

        // Lo que el servidor haría con la sugerencia: unir áreas repetidas y descartar las opciones que no son del área.
        var sug = new List<AreaSug>();
        foreach (var a in r.Sugerencia?.Areas ?? [])
        {
            if (!EnumCode.TryParseCode<DailyChangeAreaCode>(a.Area, out var area)) { r.Invalidas++; continue; }
            var permitidas = DailyChangeAreaOptionsCatalog.OptionsFor(area).Select(o => o.ToCode()).ToHashSet();
            var validas = (a.Opciones ?? []).Where(permitidas.Contains).ToList();
            r.Invalidas += (a.Opciones ?? []).Count - validas.Count;
            var previa = sug.FirstOrDefault(s => s.Area == a.Area);
            if (previa is null) sug.Add(new AreaSug(a.Area, a.Texto, validas));
            else { r.Invalidas++; previa.Opciones!.AddRange(validas.Except(previa.Opciones)); }
        }

        r.AreasOk = sug.Select(a => a.Area).ToHashSet().SetEquals(esp.Select(a => a.Area));
        r.OpcionesOk = r.AreasOk == true && esp.All(e =>
            sug.Where(a => a.Area == e.Area).SelectMany(a => a.Opciones ?? []).ToHashSet().SetEquals(e.Opciones ?? []));
        r.TemperaturaOk = caso.Esperado.Temperatura is null
            ? (r.Sugerencia?.Temperatura is null ? null : false)
            : r.Sugerencia?.Temperatura == caso.Esperado.Temperatura;
        r.Fuga = (caso.NoDebeAparecer ?? []).Any(n => sug.Any(a => Normalizar(a.Texto ?? "").Contains(Normalizar(n))));
    }
}

static class Esquema
{
    static JsonArray Arr(IEnumerable<string> v) => new(v.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());

    /// <summary>Con <paramref name="porArea"/>, cada área es una alternativa del esquema con solo sus opciones; las tres sin
    /// checklist no admiten ninguna.</summary>
    static JsonObject ElementoPorArea(DailyChangeAreaCode area)
    {
        var opciones = DailyChangeAreaOptionsCatalog.OptionsFor(area).Select(o => o.ToCode()).ToList();
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = Arr(["area", "texto", "opciones"]),
            ["properties"] = new JsonObject
            {
                ["area"] = new JsonObject { ["type"] = "string", ["enum"] = Arr([area.ToCode()]) },
                ["texto"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                ["opciones"] = opciones.Count == 0
                    ? new JsonObject { ["type"] = "array", ["maxItems"] = 0, ["items"] = new JsonObject { ["type"] = "string" } }
                    : new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string", ["enum"] = Arr(opciones) } },
            },
        };
    }

    public static JsonObject Construir(bool porArea = false)
    {
        var areas = Enum.GetValues<DailyChangeAreaCode>().Select(a => a.ToCode());
        var opciones = Enum.GetValues<DailyChangeAreaOptionCode>().Select(o => o.ToCode());
        if (porArea)
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = Arr(["areas", "temperatura"]),
                ["properties"] = new JsonObject
                {
                    ["areas"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["anyOf"] = new JsonArray(Enum.GetValues<DailyChangeAreaCode>().Select(a => (JsonNode)ElementoPorArea(a)).ToArray()) },
                    },
                    ["temperatura"] = new JsonObject { ["type"] = new JsonArray("number", "null") },
                },
            };
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = Arr(["areas", "temperatura"]),
            ["properties"] = new JsonObject
            {
                ["areas"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = Arr(["area", "texto", "opciones"]),
                        ["properties"] = new JsonObject
                        {
                            ["area"] = new JsonObject { ["type"] = "string", ["enum"] = Arr(areas) },
                            ["texto"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                            ["opciones"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["items"] = new JsonObject { ["type"] = "string", ["enum"] = Arr(opciones) },
                            },
                        },
                    },
                },
                ["temperatura"] = new JsonObject { ["type"] = new JsonArray("number", "null") },
            },
        };
    }
}
