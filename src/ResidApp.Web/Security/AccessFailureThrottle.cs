using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace ResidApp.Web.Security;

/// <summary>
/// Frena el password spraying (una contraseña probada en muchas cuentas, que el bloqueo por cuenta no ve): cuenta los fallos de acceso por
/// dirección IP en una ventana fija y, al llegar a <see cref="AccesoOptions.FallosPorIp"/>, rechaza los intentos siguientes de esa IP hasta
/// que termina la ventana. Los accesos correctos no cuentan, así que el personal de una residencia detrás de una sola IP no se bloquea
/// por entrar a la vez. Vive en memoria: se reinicia con cada despliegue y, con varias instancias, el límite se multiplica por su número.
/// </summary>
public sealed class AccessFailureThrottle(IOptions<AccesoOptions> options, TimeProvider clock)
{
    private const int SweepThreshold = 1000;
    private readonly ConcurrentDictionary<string, Window> windows = new();

    public bool IsBlocked(string? ip)
    {
        var now = clock.GetUtcNow();
        return windows.TryGetValue(Key(ip), out var window) && window.Expires > now && window.Failures >= options.Value.FallosPorIp;
    }

    public void RecordFailure(string? ip)
    {
        var now = clock.GetUtcNow();
        if (windows.Count > SweepThreshold)
        {
            foreach (var expired in windows.Where(pair => pair.Value.Expires <= now))
            {
                windows.TryRemove(expired);
            }
        }

        windows.AddOrUpdate(Key(ip), _ => new Window(now + AccesoOptions.VentanaFallos, 1),
            (_, current) => current.Expires <= now ? new Window(now + AccesoOptions.VentanaFallos, 1) : current with { Failures = current.Failures + 1 });
    }

    private static string Key(string? ip) => ip ?? "sin-ip";

    private sealed record Window(DateTimeOffset Expires, int Failures);
}
