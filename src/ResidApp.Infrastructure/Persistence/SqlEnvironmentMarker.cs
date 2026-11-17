using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ResidApp.Application.Ports;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// La marca de entorno de la base de datos (script 0100, ADR 0015 punto 7). Solo se consulta cuando el ajuste <c>Modulos__ActivarTodos</c> está puesto. Falla cerrado: sin fila, sin la
/// tabla (una base anterior al 0100) o con un fallo al leerla, la base cuenta como producción y el ajuste se ignora. Una marca confirmada se recuerda mientras viva el proceso (la
/// aplicación no puede cambiarla); lo demás se vuelve a consultar. Cada proceso deja un aviso en el registro la primera vez que ignora el ajuste. El aviso no lleva la cadena de conexión.
/// </summary>
public sealed class SqlEnvironmentMarker(SqlConnectionFactory connections, ILogger<SqlEnvironmentMarker> logger) : IEnvironmentMarker
{
    private const int InvalidObjectName = 208;
    private static volatile bool confirmed;
    private static int warned;

    public async Task<bool> IsDevelopmentDatabaseAsync(CancellationToken ct = default)
    {
        if (confirmed)
        {
            return true;
        }

        bool marked;
        try
        {
            using var connection = await connections.OpenAsync(ct);
            marked = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.entorno_bd WHERE id = 1 AND tipo = N'Desarrollo'", cancellationToken: ct)) == 1;
        }
        catch (SqlException ex) when (ex.Number == InvalidObjectName)
        {
            marked = false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "No se pudo leer la marca de entorno de la base de datos: Modulos:ActivarTodos se ignora y los módulos se rigen por las filas de cada centro.");
            return false;
        }

        if (marked)
        {
            confirmed = true;
            return true;
        }

        if (Interlocked.Exchange(ref warned, 1) == 0)
        {
            logger.LogWarning(
                "Modulos:ActivarTodos está puesto pero la base de datos no declara ser de desarrollo (tabla dbo.entorno_bd sin la fila 'Desarrollo'): el ajuste se ignora y los módulos se rigen por las filas de cada centro. " +
                "Si es una base de producción, quita el ajuste; si es de desarrollo, aplica database/seed/entorno_desarrollo.sql.");
        }

        return false;
    }
}
