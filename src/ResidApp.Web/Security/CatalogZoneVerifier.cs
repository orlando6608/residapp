using ResidApp.Application.Ports;
using ResidApp.Domain.Platform;

namespace ResidApp.Web.Security;

/// <summary>
/// Que el servidor conozca todas las zonas activas del catálogo (<c>zonas_horarias</c>). Un centro cuya zona el servidor no resuelve vería las horas en la de Madrid sin que nadie lo
/// note (<see cref="CenterZoneCache"/>), y con una zona a varias horas de distancia eso son horas equivocadas, no una de diferencia. Al arrancar se comprueba: en producción la aplicación
/// no arranca y en desarrollo y pruebas deja un error en el registro. Si el catálogo no se puede leer (la base de datos aún no responde), solo avisa: no es un fallo de zona.
/// </summary>
public static class CatalogZoneVerifier
{
    /// <summary>Los códigos del catálogo que este servidor no conoce.</summary>
    public static IReadOnlyList<string> UnknownZones(IEnumerable<CenterTimeZone> catalog) =>
        [.. catalog.Select(zone => zone.Code).Where(code => !Resolves(code))];

    public static void Verify(IEnumerable<CenterTimeZone> catalog, IHostEnvironment environment, ILogger logger)
    {
        var unknown = UnknownZones(catalog);
        if (unknown.Count == 0)
        {
            return;
        }

        var message = $"El servidor no conoce estas zonas horarias activas del catálogo: {string.Join(", ", unknown)}. Los centros que las usan verían las horas en la zona de la península. " +
                      "En Linux hace falta la base de datos de zonas (tzdata) con esos nombres.";
        if (environment.IsProduction())
        {
            throw new InvalidOperationException(message);
        }

        logger.LogError("{Message}", message);
    }

    public static async Task VerifyAsync(IServiceProvider services, IHostEnvironment environment, ILogger logger)
    {
        IReadOnlyList<CenterTimeZone> catalog;
        try
        {
            using var scope = services.CreateScope();
            catalog = await scope.ServiceProvider.GetRequiredService<ICenterTimeZoneDirectory>().ListCatalogAsync();
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "No se pudo leer el catálogo de zonas horarias al arrancar; no se comprobó que el servidor las conozca.");
            return;
        }

        Verify(catalog, environment, logger);
    }

    private static bool Resolves(string code)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(code);
            return true;
        }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }
}
