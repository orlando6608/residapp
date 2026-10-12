using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;

namespace ResidApp.IntegrationTests.TestSupport;

/// <summary>Diagnóstico de CI: la aplicación convierte cualquier excepción en «No disponible» (ApplicationResultRunner) y, cuando un test falla
/// así, el log no dice por qué (interbloqueo 1205, tiempo de espera, conexión…). Con RESIDAPP_TEST_SQL_ERRORS_LOG apuntando a un fichero, cada
/// SqlException que se lanza durante las pruebas deja una línea con su número, su mensaje y las llamadas de ResidApp que la provocaron; el workflow
/// imprime el fichero si las pruebas fallan. Las de THROW 50000+ (reglas de dominio de los triggers) se omiten porque son parte de lo esperado.
/// Sin la variable no hace nada.</summary>
internal static class SqlErrorLog
{
    private const string Variable = "RESIDAPP_TEST_SQL_ERRORS_LOG";

    [ThreadStatic]
    private static bool writing;

    [ModuleInitializer]
    internal static void Start()
    {
        var path = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        AppDomain.CurrentDomain.FirstChanceException += (_, args) =>
        {
            if (writing || args.Exception is not SqlException { Number: < 50000 } error)
            {
                return;
            }

            writing = true;
            try
            {
                var calls = Environment.StackTrace.Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => line.StartsWith("at ResidApp.", StringComparison.Ordinal) && !line.Contains("SqlErrorLog", StringComparison.Ordinal))
                    .Take(5)
                    .Select(line => line.Replace("at ", string.Empty, StringComparison.Ordinal).Split(" in ")[0]);
                var message = error.Message.Split('\n')[0].Trim();
                File.AppendAllText(path, $"{DateTime.UtcNow:HH:mm:ss.fff} SQL #{error.Number}: {message} | {string.Join(" < ", calls)}{Environment.NewLine}");
            }
            catch
            {
                // El diagnóstico nunca debe romper una prueba.
            }
            finally
            {
                writing = false;
            }
        };
    }
}
