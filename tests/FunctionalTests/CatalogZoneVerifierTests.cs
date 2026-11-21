using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ResidApp.Domain.Platform;
using ResidApp.Web.Security;

namespace ResidApp.FunctionalTests;

/// <summary>El servidor tiene que conocer todas las zonas activas del catálogo: un centro cuya zona no resuelve vería las horas en la de la península, y con una zona a varias horas de distancia
/// serían horas equivocadas. En producción no arranca; en desarrollo y pruebas deja un error en el registro.</summary>
public sealed class CatalogZoneVerifierTests
{
    private static readonly CenterTimeZone Madrid = new("Europe/Madrid", "Península y Baleares (Madrid)");
    private static readonly CenterTimeZone Canarias = new("Atlantic/Canary", "Canarias");
    private static readonly CenterTimeZone Desconocida = new("Nowhere/Atlantis", "Zona inventada");

    [Fact]
    public void UnknownZones_SoloDevuelveLasQueElServidorNoConoce()
    {
        Assert.Empty(CatalogZoneVerifier.UnknownZones([Madrid, Canarias]));
        Assert.Equal(["Nowhere/Atlantis"], CatalogZoneVerifier.UnknownZones([Madrid, Desconocida, Canarias]));
    }

    [Fact]
    public void Verify_EnProduccionConUnaZonaDesconocida_NoArranca_YConTodasConocidasSi()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => CatalogZoneVerifier.Verify([Madrid, Desconocida], new Environment(Environments.Production), new Logger()));
        Assert.Contains("Nowhere/Atlantis", failure.Message);
        Assert.Contains("tzdata", failure.Message);

        var logger = new Logger();
        CatalogZoneVerifier.Verify([Madrid, Canarias], new Environment(Environments.Production), logger);
        Assert.Empty(logger.Errors);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void Verify_FueraDeProduccionConUnaZonaDesconocida_SoloDejaUnError(string environment)
    {
        var logger = new Logger();

        CatalogZoneVerifier.Verify([Madrid, Desconocida], new Environment(environment), logger);

        Assert.Contains("Nowhere/Atlantis", Assert.Single(logger.Errors));
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "ResidApp.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class Logger : ILogger
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                Errors.Add(formatter(state, exception));
            }
        }
    }
}
