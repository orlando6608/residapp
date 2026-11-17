using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Dapper;

namespace ResidApp.IntegrationTests;

/// <summary>La marca de entorno de la base de datos (script 0100, ADR 0015 punto 7): las bases de pruebas se marcan con database/seed/entorno_desarrollo.sql.</summary>
public class EntornoBdMarcaTests
{
    [Fact]
    public async Task La_base_de_pruebas_declara_ser_de_desarrollo_y_el_lector_lo_ve()
    {
        var marker = new SqlEnvironmentMarker(TestDatabase.ConnectionFactory, NullLogger<SqlEnvironmentMarker>.Instance);

        Assert.True(await marker.IsDevelopmentDatabaseAsync(), "Falta la fila de dbo.entorno_bd: aplica database/seed/entorno_desarrollo.sql a la base de pruebas.");
    }

    [Fact]
    public async Task La_tabla_solo_admite_una_fila_y_el_valor_Desarrollo()
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => connection.ExecuteAsync("INSERT INTO dbo.entorno_bd (id, tipo) VALUES (2, N'Desarrollo')"));
        await Assert.ThrowsAnyAsync<Exception>(() => connection.ExecuteAsync("INSERT INTO dbo.entorno_bd (id, tipo) VALUES (1, N'Produccion')"));
    }
}
