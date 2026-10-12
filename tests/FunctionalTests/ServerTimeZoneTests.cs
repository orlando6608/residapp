using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ResidApp.Web.Security;

namespace ResidApp.FunctionalTests;

/// <summary>La aplicación calcula todas las horas con la zona del servidor; si el servidor no está en Europe/Madrid, en producción no arranca y en
/// desarrollo y pruebas avisa en el registro (sin ello, un entorno nuevo en UTC desplaza las horas sin que nada lo diga).</summary>
public sealed class ServerTimeZoneTests
{
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
    private static readonly TimeZoneInfo Canarias = TimeZoneInfo.FindSystemTimeZoneById("Atlantic/Canary");

    [Fact]
    public void SameOffsets_MadridSi_UtcYCanariasNo()
    {
        Assert.True(ServerTimeZone.SameOffsets(Madrid, Madrid));
        Assert.False(ServerTimeZone.SameOffsets(TimeZoneInfo.Utc, Madrid));
        Assert.False(ServerTimeZone.SameOffsets(Canarias, Madrid));
    }

    [Fact]
    public void Verify_EnProduccionConOtraZona_NoArranca_YConMadridSi()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => ServerTimeZone.Verify(new Environment(Environments.Production), new Logger(), TimeZoneInfo.Utc));
        Assert.Contains("Europe/Madrid", failure.Message);
        Assert.Contains("WEBSITE_TIME_ZONE", failure.Message);

        var logger = new Logger();
        ServerTimeZone.Verify(new Environment(Environments.Production), logger, Madrid);
        Assert.Empty(logger.Warnings);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void Verify_FueraDeProduccionConOtraZona_SoloAvisa(string environment)
    {
        var logger = new Logger();
        ServerTimeZone.Verify(new Environment(environment), logger, TimeZoneInfo.Utc);
        Assert.Contains("Europe/Madrid", Assert.Single(logger.Warnings));
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
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
