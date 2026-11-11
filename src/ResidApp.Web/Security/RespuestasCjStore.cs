using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ResidApp.Web.Security;

/// <summary>Lo que cambió en un documento la última vez que CJ lo guardó: para el aviso a Orlando, nunca las respuestas.</summary>
public sealed record CambioCj(int Respondidas, int Total);

/// <summary>Los documentos que CJ ha cambiado desde el último aviso y cuándo fue el último cambio.</summary>
public sealed record AvisoCjPendiente(DateTimeOffset UltimoCambio, Dictionary<string, CambioCj> Documentos);

/// <summary>
/// Las respuestas que CJ da en los documentos de /pendientes-cj: un fichero JSON por documento en el disco del servidor, que cada guardado sustituye (sin historial;
/// solo se conserva la versión anterior). No son datos de la aplicación, así que no pasan por la base de datos: es una herramienta de desarrollo para intercambiar con CJ.
/// La carpeta (<c>PendientesCj:CarpetaRespuestas</c>) debe estar fuera del paquete desplegado, que el despliegue vacía; por defecto, /home/data/pendientes-cj en Linux
/// (App Service lo conserva entre despliegues) y la carpeta de datos locales del usuario en el resto. Un solo bloqueo para todo: lo usa una persona.
/// Además anota en <c>aviso-pendiente.json</c> qué documentos cambiaron, para el aviso por correo a Orlando.
/// </summary>
public sealed partial class RespuestasCjStore(IConfiguration configuration)
{
    public const int MaxBytes = 256 * 1024;

    private const string PendingFile = "aviso-pendiente.json";

    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly JsonSerializerOptions PendingJson = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _lock = new(1, 1);

    [GeneratedRegex("^[a-z0-9-]{1,100}$")]
    private static partial Regex NameRegex();

    /// <summary>Un documento activo de CJ: nombre en minúsculas, cifras y guiones (así nunca sale de la carpeta), con su .html y su .notas.json publicados en la raíz de
    /// /pendientes-cj. Los índices (index, preguntas…) y las páginas de flujo no llevan notas, así que no son documentos.</summary>
    public static bool IsValidName(string? name)
    {
        if (name is null || !NameRegex().IsMatch(name))
        {
            return false;
        }

        var root = Path.Combine(AppContext.BaseDirectory, "pendientes-cj");
        return File.Exists(Path.Combine(root, name + ".html")) && File.Exists(Path.Combine(root, name + ".notas.json"));
    }

    private string Folder
    {
        get
        {
            var configured = configuration["PendientesCj:CarpetaRespuestas"];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            return RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && Directory.Exists("/home")
                ? "/home/data/pendientes-cj"
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResidApp", "pendientes-cj");
        }
    }

    private string PathOf(string name) => Path.Combine(Folder, name + ".respuestas.json");

    public async Task<string?> ReadAsync(string name, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var path = PathOf(name);
            return File.Exists(path) ? await File.ReadAllTextAsync(path, Utf8, ct) : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Sustituye las respuestas del documento; con <paramref name="notify"/> lo anota para el aviso a Orlando (no cuando es una carga de respuestas antiguas). El fichero nuevo se escribe aparte y se mueve encima, para no dejar nunca un JSON a medias; el anterior se guarda
    /// como <c>.anterior.json</c> (una sola copia).</summary>
    public async Task SaveAsync(string name, string json, CambioCj change, DateTimeOffset now, bool notify, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Folder);
            var path = PathOf(name);
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, json, Utf8, ct);
            if (File.Exists(path))
            {
                File.Copy(path, Path.Combine(Folder, name + ".respuestas.anterior.json"), overwrite: true);
            }

            File.Move(temp, path, overwrite: true);

            if (notify)
            {
                var pending = await ReadPendingAsync(ct);
                var documents = pending?.Documentos ?? [];
                documents[name] = change;
                await WritePendingAsync(new AvisoCjPendiente(now, documents), ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Los cambios pendientes de avisar, si llevan <paramref name="quiet"/> sin tocarse. No los saca de la lista: hasta que el correo no está aceptado (<see cref="MarkToldAsync"/>)
    /// siguen ahí, así que si la aplicación se para durante el envío el aviso no se pierde (puede salir dos veces). Null si no hay nada o CJ sigue respondiendo.</summary>
    public async Task<AvisoCjPendiente?> PeekDueAsync(DateTimeOffset now, TimeSpan quiet, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var pending = await ReadPendingAsync(ct);
            return pending is null || pending.Documentos.Count == 0 || now - pending.UltimoCambio < quiet ? null : pending;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>El correo ya está aceptado: quita de la lista lo avisado. Lo que CJ haya cambiado mientras se enviaba (otra cifra en ese documento, u otro documento) se queda
    /// para el siguiente aviso.</summary>
    public async Task MarkToldAsync(AvisoCjPendiente told, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var current = await ReadPendingAsync(ct);
            if (current is null)
            {
                return;
            }

            var left = current.Documentos.Where(d => !(told.Documentos.TryGetValue(d.Key, out var sent) && sent == d.Value)).ToDictionary(d => d.Key, d => d.Value);
            if (left.Count == 0)
            {
                File.Delete(Path.Combine(Folder, PendingFile));
            }
            else
            {
                await WritePendingAsync(new AvisoCjPendiente(current.UltimoCambio, left), ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>El envío falló: lo pendiente se reintenta tras otro silencio contado desde <paramref name="now"/>, no desde el cambio original (así un fallo no se repite cada minuto).</summary>
    public async Task DeferAsync(DateTimeOffset now, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var current = await ReadPendingAsync(ct);
            if (current is not null)
            {
                await WritePendingAsync(current with { UltimoCambio = now }, ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<AvisoCjPendiente?> ReadPendingAsync(CancellationToken ct)
    {
        var path = Path.Combine(Folder, PendingFile);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var pending = JsonSerializer.Deserialize<AvisoCjPendiente>(await File.ReadAllTextAsync(path, Utf8, ct), PendingJson);
            return pending?.Documentos is null ? null : pending;
        }
        catch (JsonException)
        {
            return null; // un estado ilegible solo cuesta un aviso
        }
    }

    private Task WritePendingAsync(AvisoCjPendiente pending, CancellationToken ct) =>
        File.WriteAllTextAsync(Path.Combine(Folder, PendingFile), JsonSerializer.Serialize(pending, PendingJson), Utf8, ct);
}
