using System.Collections.Concurrent;
using ResidApp.Application.Ports;
using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Web.Security;

/// <summary>
/// La zona horaria del centro del ámbito activo, con una caché de unos segundos. La clave es el par (cuenta de la sesión, ámbito) y el valor sale de SQL, que comprueba que ese ámbito
/// es de esa cuenta: una cookie de ámbito manipulada no da la zona de otro centro, solo la de la península. Un cambio de zona vacía la caché de este servidor
/// (<see cref="Clear"/>); en otros servidores, o si el cambio llega por otro lado, vale en cuanto caduca la entrada (<see cref="Lifetime"/>).
/// </summary>
public sealed class CenterZoneCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<(string Subject, Guid ProfileScopeId), (TimeZoneInfo Zone, DateTimeOffset ExpiresAt)> entries = new();

    public void Clear() => entries.Clear();

    public async Task<TimeZoneInfo> GetAsync(
        string subject, Guid profileScopeId, ICenterTimeZoneDirectory directory, ILogger logger, TimeProvider clock, CancellationToken ct)
    {
        var key = (subject, profileScopeId);
        var now = clock.GetUtcNow();
        if (entries.TryGetValue(key, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Zone;
        }

        var zone = CenterTime.Madrid;
        try
        {
            if (await directory.FindZoneCodeForScopeAsync(subject, profileScopeId, ct) is { } code)
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(code);
            }
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            // Sin fila o con un fallo de lectura, las horas saldrían en la zona de la península sin que nadie lo note: se deja constancia (el fallo no se cachea). Una zona que el
            // servidor no conoce es un error de verdad (con una zona lejana serían varias horas equivocadas); un fallo de lectura de la base de datos, un aviso.
            var level = error is TimeZoneNotFoundException or InvalidTimeZoneException ? LogLevel.Error : LogLevel.Warning;
            logger.Log(level, error, "No se pudo leer la zona horaria del centro del ámbito activo; se usa {Zone}.", CenterTimeZoneRules.DefaultZoneCode);
            return zone;
        }

        if (entries.Count > 5000)
        {
            entries.Clear();
        }

        entries[key] = (zone, now + Lifetime);
        return zone;
    }
}

/// <summary>Fija <see cref="CenterTime"/> con la zona del centro del ámbito activo durante toda la petición. Sin sesión o sin ámbito, la de la península.</summary>
public sealed class CenterTimeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context, CenterZoneCache cache, ICenterTimeZoneDirectory directory, TimeProvider clock, ILogger<CenterTimeMiddleware> logger)
    {
        var subject = SessionAuthentication.Subject(context.User);
        var scope = subject is null ? null : ActiveProfileScopeCookie.Read(context.Request);
        if (subject is null || scope is null)
        {
            await next(context);
            return;
        }

        var zone = await cache.GetAsync(subject, scope.ProfileScopeId, directory, logger, clock, context.RequestAborted);
        using (CenterTime.Use(zone))
        {
            await next(context);
        }
    }
}
