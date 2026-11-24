using System.Runtime.CompilerServices;
using Dapper;
using ResidApp.Application.Ports;
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

    /// <summary>
    /// Una fábrica de conexiones con el ámbito de Plataforma activo de la cuenta en la sesión de SQL, como la de la aplicación. Las pruebas que dan de alta un centro la necesitan: el procedimiento
    /// fijar_horario_inicial_centro (0114) comprueba por su cuenta que la sesión es de Plataforma, y con la fábrica sin ámbito de arriba no lo es.
    /// </summary>
    public static SqlConnectionFactory PlatformFactoryFor(string subject) => new(ConnectionString, new PlatformSessionTenant(subject));

    private sealed class PlatformSessionTenant(string subject) : ITenantContext
    {
        public async Task<TenantScope?> GetAsync(CancellationToken ct = default)
        {
            using var connection = await ConnectionFactory.OpenAsync(ct);
            var scopeId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
                SELECT TOP (1) ambito.id
                  FROM dbo.ambitos_perfil ambito JOIN dbo.cuentas cuenta ON cuenta.id = ambito.cuenta_id
                 WHERE cuenta.sujeto_externo = @subject AND ambito.perfil_codigo = 'PLATAFORMA' AND ambito.estado = 'ACTIVE' AND ambito.revocado_en IS NULL
                """, new { subject }, cancellationToken: ct));
            return scopeId is { } id ? new TenantScope(subject, id) : null;
        }
    }

    [ModuleInitializer]
    public static void Init() => DapperDateOnlyTypeHandler.Register();
}
