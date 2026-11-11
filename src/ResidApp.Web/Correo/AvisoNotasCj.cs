using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ResidApp.Application.Email;
using ResidApp.Application.Ports;
using ResidApp.Web.Security;

namespace ResidApp.Web.Correo;

/// <summary>Lo que la tarea recuerda en memoria entre vueltas: si no ha podido escribir el estado, no vuelve a intentarlo (y a reenviar) cada minuto.</summary>
public sealed class NotasAvisoMemoria
{
    public DateTimeOffset? EsperarHasta { get; set; }
}

/// <summary>
/// El aviso a CJ de que hay un documento nuevo con preguntas (un <c>.html</c> de la raíz con <c>data-doc</c>) o de que ingeniería ha dejado notas nuevas (o cambiadas) en sus documentos. Las notas viajan en el repositorio y llegan con un despliegue
/// (<c>&lt;documento&gt;.notas.json</c>): esta tarea compara la huella de cada nota (SHA-256 de su texto) con la última que avisó (<see cref="AvisoNotasEstado"/>, en la carpeta de las
/// respuestas) y manda UN correo con los documentos que tienen alguna nota nueva o cambiada. El correo lleva el título de cada documento y su enlace; nunca el texto de las notas
/// ni sus respuestas. Retirar una nota no avisa a nadie, aunque el documento conserve otras. Un fichero de notas que no se puede leer en este momento (a medio copiar, roto) no hace
/// olvidar lo ya avisado. La dirección sale de <c>PendientesCj:AvisarACj</c> (ajuste de la Web App, no del repositorio: el repositorio es público); sin ella, o con el correo
/// apagado, no hace nada. Mismo criterio de destinos que el resto de avisos. Si el envío falla, o no se puede anotar el estado, se reintenta pasados <see cref="RetryAfter"/>.
/// Sin la carpeta de documentos (como en un paquete de producción) es inerte. Es un uso de desarrollo.
/// </summary>
public sealed partial class AvisoNotasCj(
    RespuestasCjStore store, string documentsFolder, IEmailSender sender, EmailSettings settings, string? address, NotasAvisoMemoria memory, Action<string> warn)
{
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);

    [GeneratedRegex("<title>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TitleRegex();

    /// <summary>Una vuelta de la tarea. True si mandó el correo.</summary>
    public async Task<bool> ProcessAsync(DateTimeOffset now, CancellationToken ct)
    {
        address = address?.Trim();
        if (!settings.Active || string.IsNullOrWhiteSpace(address) || !AvisoRespuestasCj.RecipientAllowed(settings, address) || !Directory.Exists(documentsFolder))
        {
            return false;
        }

        if (memory.EsperarHasta is { } wait && now < wait)
        {
            return false;
        }

        var state = await store.ReadNotesStateAsync(ct);
        if (state?.ReintentarDespues is { } retry && now < retry)
        {
            return false;
        }

        var told = new Dictionary<string, string>(state?.Huellas ?? []);
        var (current, unreadable) = CurrentFingerprints(documentsFolder);

        // Los documentos de CJ con preguntas (los que llevan data-doc) se anotan con una marca «documento/#documento»: uno nuevo avisa como documento nuevo.
        // Un estado anterior a este aviso no tiene marcas: los documentos que ya había se dan por conocidos, sin correo (si no, avisaría de todos a la vez).
        var documents = CurrentDocuments(documentsFolder, unreadable);
        var learn = documents.Count > 0 && !told.Keys.Any(IsDocumentKey);
        foreach (var document in documents)
        {
            current[document + "/" + DocumentMarker] = "1";
            if (learn)
            {
                told[document + "/" + DocumentMarker] = "1";
            }
        }

        // Lo ya avisado, menos las notas retiradas de documentos que se leen bien (esas se olvidan); de un documento que no se puede leer ahora se conserva todo.
        var kept = told.Where(t => unreadable.Contains(DocumentOf(t.Key)) || current.ContainsKey(t.Key)).ToDictionary(t => t.Key, t => t.Value);
        var added = current.Where(c => !kept.TryGetValue(c.Key, out var fingerprint) || fingerprint != c.Value).Select(c => c.Key).ToList();
        var newDocuments = added.Where(IsDocumentKey).Select(DocumentOf).Distinct().Order(StringComparer.Ordinal).ToList();
        var changed = added.Where(key => !IsDocumentKey(key)).Select(DocumentOf).Distinct().Except(newDocuments).Order(StringComparer.Ordinal).ToList();
        var next = new Dictionary<string, string>(kept);
        foreach (var (key, value) in current)
        {
            next[key] = value;
        }

        if (changed.Count == 0 && newDocuments.Count == 0)
        {
            if ((state is not null || learn) && (state?.ReintentarDespues is not null || next.Count != told.Count || learn))
            {
                await TryWriteAsync(new AvisoNotasEstado(next, null), now, ct); // una nota retirada solo se olvida
            }

            return false;
        }

        try
        {
            var result = await sender.SendAsync(Compose(changed.Select(name => (Title(documentsFolder, name), name)).ToList(),
                newDocuments.Select(name => (Title(documentsFolder, name), name)).ToList(), settings.BaseUrl, address), ct);
            if (result.Accepted)
            {
                // El correo ya salió: si el estado no se puede anotar, no se reenvía hasta pasados los 10 minutos.
                await TryWriteAsync(new AvisoNotasEstado(next, null), now, CancellationToken.None);
                return true;
            }

            warn($"Aviso de notas a CJ: el proveedor no aceptó el correo ({result.FailureCode}); se reintenta pasados {RetryAfter.TotalMinutes:0} minutos.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warn($"Aviso de notas a CJ: el envío falló ({ex.GetType().Name}); se reintenta pasados {RetryAfter.TotalMinutes:0} minutos.");
        }

        await TryWriteAsync(new AvisoNotasEstado(kept, now + RetryAfter), now, CancellationToken.None);
        return false;
    }

    private async Task TryWriteAsync(AvisoNotasEstado state, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            await store.WriteNotesStateAsync(state, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            memory.EsperarHasta = now + RetryAfter;
            warn($"Aviso de notas a CJ: no se pudo anotar el estado ({ex.GetType().Name}); no se vuelve a intentar hasta pasados {RetryAfter.TotalMinutes:0} minutos.");
        }
    }

    private const string DocumentMarker = "#documento";

    private static string DocumentOf(string key) => key[..key.IndexOf('/')];

    private static bool IsDocumentKey(string key) => key.EndsWith("/" + DocumentMarker, StringComparison.Ordinal);

    /// <summary>Los documentos de CJ con preguntas: los <c>.html</c> de la raíz que llevan <c>data-doc</c> (no el índice, las secciones ni las páginas de flujo). Uno que no se puede leer
    /// ahora se añade a <paramref name="unreadable"/>, para no olvidarlo ni darlo por nuevo.</summary>
    public static List<string> CurrentDocuments(string folder, HashSet<string> unreadable)
    {
        var result = new List<string>();
        if (!Directory.Exists(folder))
        {
            return result;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.html", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            try
            {
                if (File.ReadAllText(file, Encoding.UTF8).Contains("data-doc=\"", StringComparison.Ordinal))
                {
                    result.Add(name);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                unreadable.Add(name);
            }
        }

        return result;
    }

    /// <summary>La huella de cada nota con texto (clave «documento/nota»; el texto se normaliza y se recorta, así que el formato del fichero no cuenta) y los documentos que no se han
    /// podido leer ahora. Un documento sin <c>.html</c> publicado no es un documento de CJ y no cuenta.</summary>
    public static (Dictionary<string, string> Fingerprints, HashSet<string> Unreadable) CurrentFingerprints(string folder)
    {
        var result = new Dictionary<string, string>();
        var unreadable = new HashSet<string>();
        if (!Directory.Exists(folder))
        {
            return (result, unreadable);
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.notas.json", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file)[..^".notas.json".Length];
            if (!File.Exists(Path.Combine(folder, name + ".html")))
            {
                continue;
            }

            try
            {
                using var parsed = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
                if (!parsed.RootElement.TryGetProperty("notas", out var notes) || notes.ValueKind != JsonValueKind.Object)
                {
                    unreadable.Add(name);
                    continue;
                }

                foreach (var note in notes.EnumerateObject())
                {
                    if (note.Value.ValueKind == JsonValueKind.String && note.Value.GetString()?.Normalize(NormalizationForm.FormC).Trim() is { Length: > 0 } text)
                    {
                        result[name + "/" + note.Name] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                unreadable.Add(name); // roto, a medio copiar o con texto que no se puede leer: no avisa de nada y no hace olvidar lo ya avisado
            }
        }

        return (result, unreadable);
    }

    private static string Title(string folder, string name)
    {
        try
        {
            var match = TitleRegex().Match(File.ReadAllText(Path.Combine(folder, name + ".html"), Encoding.UTF8));
            var title = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
            return title.Length is > 0 and <= 120 ? title : name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return name;
        }
    }

    public static EmailMessage Compose(IReadOnlyList<(string Title, string Name)> notes, IReadOnlyList<(string Title, string Name)> newDocuments, string baseUrl, string to)
    {
        var root = baseUrl.TrimEnd('/');
        var subject = (newDocuments.Count, notes.Count) switch
        {
            (0, 1) => "Ingeniería ha dejado notas en 1 documento de ResidApp",
            (0, _) => $"Ingeniería ha dejado notas en {notes.Count} documentos de ResidApp",
            (1, 0) => "Hay 1 documento nuevo con preguntas para ti en ResidApp",
            (_, 0) => $"Hay {newDocuments.Count} documentos nuevos con preguntas para ti en ResidApp",
            _ => "Novedades en tus documentos de ResidApp",
        };
        // El pie es lo único que nombra a alguien (a quién decírselo para dejar de recibirlo); el asunto y el cuerpo no, porque se ven en pantallas de bloqueo.
        const string footer = "Aviso del entorno de pruebas de ResidApp. No contiene datos de pacientes, ni el texto de las notas, ni tus respuestas. ResidApp nunca te pedirá tu clave por correo: si no esperabas este aviso, no entres. Para dejar de recibirlo, díselo a Orlando (este buzón no recibe respuestas).";

        var text = new StringBuilder();
        var paragraphs = new List<(string Html, bool Highlighted)>();
        void Section(string intro, IReadOnlyList<(string Title, string Name)> documents, string outro)
        {
            if (documents.Count == 0)
            {
                return;
            }

            text.Append(intro + "\n\n");
            paragraphs.Add((WebUtility.HtmlEncode(intro), false));
            foreach (var (title, name) in documents)
            {
                var url = $"{root}/pendientes-cj/{name}.html";
                text.Append($"- {title}\n  {url}\n");
                paragraphs.Add(($"<strong>{WebUtility.HtmlEncode(title)}</strong><br><a href=\"{WebUtility.HtmlEncode(url)}\">{WebUtility.HtmlEncode(url)}</a>", true));
            }

            text.Append("\n" + outro + "\n\n");
            paragraphs.Add((WebUtility.HtmlEncode(outro), false));
        }

        Section("Ingeniería ha publicado documentos nuevos con preguntas para ti:", newDocuments, "Ábrelos y contesta cuando puedas: lo que marques se guarda solo.");
        Section("Ingeniería ha dejado notas debajo de tus respuestas en:", notes, "Léelas y, si te pedimos repetir alguna prueba, vuelve a marcar tu respuesta.");
        text.Append("—\n" + footer + "\n");
        return new EmailMessage(to, subject, text.ToString(), EmailLayout.Render("es", subject, paragraphs, WebUtility.HtmlEncode(footer)), [EmailLayout.Icon]);
    }
}
