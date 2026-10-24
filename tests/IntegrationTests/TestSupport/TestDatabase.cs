using System.Runtime.CompilerServices;
using ResidApp.Infrastructure.Persistence;

namespace ResidApp.IntegrationTests.TestSupport;

/// <summary>
/// Apunta a una base propia de las pruebas en la instancia local (ResidAppPruebas, creada con database/aplicar-scripts.sh, sin
/// semillas), no a la de la aplicación: así las pruebas no la llenan de centros y auditoría. En el CI es ResidAppTest.
/// Sobrescribible con RESIDAPP_TEST_CONNECTION_STRING en otra máquina.
/// </summary>
internal static class TestDatabase
{
    private const string DefaultConnectionString =
        "Server=ACER-ORLANDO;Database=ResidAppPruebas;Integrated Security=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("RESIDAPP_TEST_CONNECTION_STRING") ?? DefaultConnectionString;

    public static SqlConnectionFactory ConnectionFactory { get; } = new(ConnectionString);

    [ModuleInitializer]
    public static void Init() => DapperDateOnlyTypeHandler.Register();
}
