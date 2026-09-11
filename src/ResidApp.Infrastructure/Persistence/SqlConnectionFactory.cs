using Microsoft.Data.SqlClient;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>Fábrica mínima de conexiones SQL Server. La cadena de conexión llega ya resuelta desde
/// ResidApp.Web (configuración/appsettings); Infrastructure no conoce el origen.</summary>
public sealed class SqlConnectionFactory(string connectionString)
{
    public async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
