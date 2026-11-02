using Dapper;
using ResidApp.Application.Ports;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// La purga del registro de envíos de correo (script 0085): llama al procedimiento <c>dbo.purgar_envios_correo</c>, que borra lo que lleva más de 12 meses. El plazo está en la
/// base de datos, no aquí. La conexión es la de la tarea programada (rol <c>residapp_tareas</c>, que solo puede ejecutar el procedimiento).
/// </summary>
public sealed class SqlEmailLogPurge(SqlConnectionFactory connections) : IEmailLogPurge
{
    public async Task<bool> HasTaskIdentityAsync(CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(SqlChangeNoticeStore.IdentitySql, cancellationToken: ct)) == 1;
    }

    public async Task<int> PurgeAsync(CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition("EXEC dbo.purgar_envios_correo", commandTimeout: 120, cancellationToken: ct));
    }
}
