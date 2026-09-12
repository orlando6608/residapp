using System.Runtime.CompilerServices;
using ResidApp.Infrastructure.Persistence;

namespace ResidApp.IntegrationTests.TestSupport;

/// <summary>
/// Apunta a la misma instancia real de SQL Server usada en desarrollo (ver database/scripts y
/// database/seed); no hay todavía una instancia de CI decidida (docs/producto/roadmap.md, "Decisiones
/// abiertas"). Sobrescribible con RESIDAPP_TEST_CONNECTION_STRING en otra máquina.
/// </summary>
internal static class TestDatabase
{
    private const string DefaultConnectionString =
        "Server=ACER-ORLANDO;Database=ResidApp;Integrated Security=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("RESIDAPP_TEST_CONNECTION_STRING") ?? DefaultConnectionString;

    public static SqlConnectionFactory ConnectionFactory { get; } = new(ConnectionString);

    [ModuleInitializer]
    public static void Init() => DapperDateOnlyTypeHandler.Register();
}
