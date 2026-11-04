using Dapper;
using ResidApp.Application.Ports;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// El cierre de los avisos de correo que quedaron PENDIENTE (script 0089): llama al procedimiento <c>dbo.cerrar_envios_correo_sin_resolver</c>, que los marca FALLIDO con el motivo
/// RESULTADO_DESCONOCIDO pasado un plazo. El plazo y el motivo están en la base de datos, no aquí. La conexión es la de la tarea programada (rol <c>residapp_tareas</c>).
/// </summary>
public sealed class SqlUnresolvedNoticeCloser(SqlConnectionFactory connections) : IUnresolvedNoticeCloser
{
    public async Task<bool> HasTaskIdentityAsync(CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(SqlChangeNoticeStore.IdentitySql, cancellationToken: ct)) == 1;
    }

    public async Task<int> CloseAsync(CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition("EXEC dbo.cerrar_envios_correo_sin_resolver", commandTimeout: 120, cancellationToken: ct));
    }
}
