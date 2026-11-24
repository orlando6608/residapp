using System.Collections.Concurrent;
using ResidApp.Application.Ports;
using ResidApp.Domain.Platform;

namespace ResidApp.Web.Security;

/// <summary>
/// El tipo de servicio del centro del ámbito activo, con una caché de unos segundos (ADR 0017). La clave es el par (cuenta de la sesión, ámbito) y el valor sale de SQL, que comprueba que ese ámbito
/// es de esa cuenta: una cookie de ámbito manipulada no da el tipo de otro centro. El tipo solo cambia al corregir un error de alta en un centro vacío, así que unos segundos de caché bastan; una
/// corrección vacía la de este servidor (<see cref="Clear"/>).
/// </summary>
public sealed class CenterServiceTypeCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<(string Subject, Guid ProfileScopeId), (CenterServiceType? Type, DateTimeOffset ExpiresAt)> entries = new();

    public void Clear() => entries.Clear();

    public async Task<CenterServiceType?> GetAsync(
        string subject, Guid profileScopeId, ICenterServiceTypeDirectory directory, ILogger logger, TimeProvider clock, CancellationToken ct)
    {
        var key = (subject, profileScopeId);
        var now = clock.GetUtcNow();
        if (entries.TryGetValue(key, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Type;
        }

        CenterServiceType? type;
        try
        {
            type = await directory.FindForScopeAsync(subject, profileScopeId, ct);
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            // Con un fallo de lectura la pantalla sale como la de una Residencia (lo de siempre) sin que nadie lo note: se deja constancia y no se cachea.
            logger.LogWarning(error, "No se pudo leer el tipo de servicio del centro del ámbito activo; se muestra como una Residencia.");
            return null;
        }

        if (entries.Count > 5000)
        {
            entries.Clear();
        }

        entries[key] = (type, now + Lifetime);
        return type;
    }
}

/// <summary>Fija <see cref="CurrentCenterService"/> con el tipo del centro del ámbito activo durante toda la petición. Sin sesión o sin ámbito, no hay tipo.</summary>
public sealed class CenterServiceTypeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context, CenterServiceTypeCache cache, ICenterServiceTypeDirectory directory, TimeProvider clock, ILogger<CenterServiceTypeMiddleware> logger)
    {
        var subject = SessionAuthentication.Subject(context.User);
        var scope = subject is null ? null : ActiveProfileScopeCookie.Read(context.Request);
        if (subject is null || scope is null)
        {
            await next(context);
            return;
        }

        var type = await cache.GetAsync(subject, scope.ProfileScopeId, directory, logger, clock, context.RequestAborted);
        using (CurrentCenterService.Use(type))
        {
            await next(context);
        }
    }
}
