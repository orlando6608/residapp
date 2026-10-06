using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>Fábrica mínima de conexiones SQL Server. La cadena de conexión llega ya resuelta desde
/// ResidApp.Web (configuración/appsettings); Infrastructure no conoce el origen.
/// Con un <see cref="ITenantContext"/>, cada conexión abierta lleva en SESSION_CONTEXT el sujeto verificado y el ámbito activo para
/// la seguridad por filas (ADR 0008, database/scripts/0030). Se fija en cada apertura, con o sin ámbito, porque el pool reutiliza
/// conexiones; sin tenant (tests, scripts) la conexión no lleva contexto.</summary>
public sealed class SqlConnectionFactory(string connectionString, ITenantContext? tenant = null)
{
    public async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        if (tenant is not null)
        {
            try
            {
                await SetSessionContextAsync(connection, await tenant.GetAsync(ct), ct);
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }
        return connection;
    }

    private static async Task SetSessionContextAsync(SqlConnection connection, TenantScope? scope, CancellationToken ct)
    {
        // read_only: una consulta posterior en esta conexión (p. ej. un SQL inyectado) no puede cambiar el contexto.
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_set_session_context @key = N'sujeto_externo', @value = @sujeto, @read_only = 1;
            EXEC sys.sp_set_session_context @key = N'ambito_perfil_id', @value = @ambito, @read_only = 1;
            """;
        command.Parameters.Add(new SqlParameter("@sujeto", System.Data.SqlDbType.NVarChar, 200) { Value = (object?)scope?.ExternalSubject ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@ambito", System.Data.SqlDbType.UniqueIdentifier) { Value = (object?)scope?.ProfileScopeId ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(ct);
    }
}
