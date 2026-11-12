using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ResidApp.Application.Errors;
using ResidApp.Infrastructure.Registro;
using ResidApp.IntegrationTests.TestSupport;
using Xunit;

namespace ResidApp.IntegrationTests;

/// <summary>Hallazgo A4 de seguridad: el runner convierte toda excepción en un mensaje genérico, así que el registro técnico es lo único que deja rastro
/// de un acceso denegado o de un error inesperado. Lo que más importa aquí es lo que NO se escribe: el mensaje de una excepción puede llevar datos
/// personales (el de un error de SQL por clave duplicada lleva el valor), así que solo se escribe si es, entero, un código en mayúsculas.</summary>
public sealed class RegistroFallosTecnicosTests
{
    private const string Personal = "Ana-Lopez-87654321X";

    private static (CapturingLogger Log, ApplicationFailure Failure) Write(Exception error, Func<TechnicalFailureLog.RequestContext?>? context = null)
    {
        var log = new CapturingLogger();
        try { throw error; } catch (Exception thrown) { error = thrown; } // una excepción sin lanzar no tiene pila de llamadas
        var failure = ApplicationResultRunner.Normalize(error);
        TechnicalFailureLog.Write(log, error, failure, context);
        return (log, failure);
    }

    [Fact]
    public void AccesoDenegado_ConCodigo_RegistraUnAvisoConElCodigoYElOrigen()
    {
        var (log, failure) = Write(new InvalidOperationException("CLINICAL_DETAIL_READ_NOT_AUTHORIZED"));

        Assert.Equal(ApplicationFailureCode.AccessDenied, failure.Code);
        var line = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Contains("CLINICAL_DETAIL_READ_NOT_AUTHORIZED", line.Text);
        Assert.Contains(nameof(RegistroFallosTecnicosTests), line.Text);
    }

    [Fact]
    public void AccesoDenegado_ConContexto_LlevaLaSesionYElCentro()
    {
        var session = Guid.NewGuid();
        var center = Guid.NewGuid();

        var (log, _) = Write(new InvalidOperationException("CLINICAL_DETAIL_READ_NOT_AUTHORIZED"), () => new TechnicalFailureLog.RequestContext(session, center));

        var line = Assert.Single(log.Lines);
        Assert.Contains(session.ToString("D"), line.Text);
        Assert.Contains(center.ToString("D"), line.Text);
    }

    [Fact]
    public void AccesoDenegado_SinContextoOSiObtenerloFalla_EscribeElAvisoIgual()
    {
        var (withoutContext, _) = Write(new InvalidOperationException("CLINICAL_DETAIL_READ_NOT_AUTHORIZED"));
        var (failingContext, _) = Write(new InvalidOperationException("CLINICAL_DETAIL_READ_NOT_AUTHORIZED"), () => throw new InvalidOperationException("sin petición"));

        foreach (var log in new[] { withoutContext, failingContext })
        {
            var line = Assert.Single(log.Lines);
            Assert.Contains("CLINICAL_DETAIL_READ_NOT_AUTHORIZED", line.Text);
            Assert.Contains("sesión -, centro declarado -", line.Text);
        }
    }

    [Fact]
    public void FalloInesperado_NoLlevaLaSesionNiElCentro()
    {
        var session = Guid.NewGuid();

        var (log, failure) = Write(new InvalidOperationException("paciente"), () => new TechnicalFailureLog.RequestContext(session, Guid.NewGuid()));

        Assert.Equal(ApplicationFailureCode.Unavailable, failure.Code);
        Assert.DoesNotContain(session.ToString("D"), Assert.Single(log.Lines).Text);
    }

    [Fact]
    public void AccesoDenegado_ConTextoAlrededorDelCodigo_NoRegistraElMensaje()
    {
        // El patrón de acceso denegado no está anclado: reconoce el código aunque haya texto antes o después. Ese texto no se escribe.
        var (log, failure) = Write(new InvalidOperationException($"Paciente {Personal}: CLINICAL_DETAIL_READ_NOT_AUTHORIZED"));

        Assert.Equal(ApplicationFailureCode.AccessDenied, failure.Code);
        var line = Assert.Single(log.Lines);
        Assert.DoesNotContain(Personal, line.Text);
        Assert.Contains(TechnicalFailureLog.OmittedMessage, line.Text);
    }

    [Fact]
    public void FalloInesperado_RegistraUnErrorConElTipo_NoElMensaje()
    {
        var (log, failure) = Write(new InvalidOperationException($"No se pudo guardar a {Personal}"));

        Assert.Equal(ApplicationFailureCode.Unavailable, failure.Code);
        var line = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Contains(nameof(InvalidOperationException), line.Text);
        Assert.DoesNotContain(Personal, line.Text);
        Assert.DoesNotContain("No se pudo guardar", line.Text);
    }

    [Fact]
    public async Task ErrorDeSql_RegistraNumeroYEstado_NuncaElValorDeLaClave()
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        // En un solo lote: Dapper ejecuta cada llamada dentro de sp_executesql, que descarta la tabla temporal al terminar.
        var error = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "CREATE TABLE #registro_fallos (valor NVARCHAR(60) NOT NULL PRIMARY KEY); INSERT INTO #registro_fallos VALUES (@Valor); INSERT INTO #registro_fallos VALUES (@Valor);", new { Valor = Personal }));
        Assert.Contains(Personal, error.Message); // la premisa: sin esta regla, el mensaje de SQL llevaría el dato

        var (log, failure) = Write(error);

        Assert.Equal(ApplicationFailureCode.Unavailable, failure.Code);
        var line = Assert.Single(log.Lines);
        Assert.Contains($"SQL {error.Number}/{error.State}", line.Text);
        Assert.Contains(nameof(SqlException), line.Text);
        Assert.DoesNotContain(Personal, line.Text);
        Assert.DoesNotContain("duplicate key", line.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(RegistroFallosTecnicosTests), line.Text);
    }

    [Theory]
    [InlineData("IDEMPOTENCY_KEY_REUSED")] // conflicto
    [InlineData("APPLICATION_INPUT_INVALID")] // datos no válidos
    public void ConflictosYDatosNoValidos_SonErroresFuncionalesEsperados_NoSeRegistran(string code)
    {
        var (log, failure) = Write(new InvalidOperationException(code));

        Assert.NotEqual(ApplicationFailureCode.AccessDenied, failure.Code);
        Assert.NotEqual(ApplicationFailureCode.Unavailable, failure.Code);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public async Task ElRunner_AvisaAlObservador_YUnObservadorQueFalla_NoCambiaElResultado()
    {
        var previous = ApplicationResultRunner.FailureObserver;
        try
        {
            var seen = new List<ApplicationFailureCode>();
            ApplicationResultRunner.FailureObserver = (_, failure) => seen.Add(failure.Code);
            var denied = await ApplicationResultRunner.RunAsync<int>(() => throw new AccessDeniedException());
            Assert.Equal(ApplicationFailureCode.AccessDenied, denied.Error!.Code);
            Assert.Contains(ApplicationFailureCode.AccessDenied, seen);

            ApplicationResultRunner.FailureObserver = (_, _) => throw new InvalidOperationException("el registro está roto");
            var unavailable = await ApplicationResultRunner.RunAsync<int>(() => throw new InvalidOperationException("fallo"));
            Assert.False(unavailable.Ok);
            Assert.Equal(ApplicationFailureCode.Unavailable, unavailable.Error!.Code);
        }
        finally
        {
            ApplicationResultRunner.FailureObserver = previous;
        }
    }

    [Fact]
    public void Register_ConectaElRegistroConElRunner()
    {
        var previous = ApplicationResultRunner.FailureObserver;
        try
        {
            ApplicationResultRunner.FailureObserver = null;
            TechnicalFailureLog.Register(new CapturingLogger());
            Assert.NotNull(ApplicationResultRunner.FailureObserver);
        }
        finally
        {
            ApplicationResultRunner.FailureObserver = previous;
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Text)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, formatter(state, exception) + (exception is null ? "" : " " + exception)));
    }
}
