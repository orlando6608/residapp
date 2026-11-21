using Dapper;
using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// La zona horaria de un centro para las tareas de correo (script 0110). La tarea (rol residapp_tareas) no ve <c>zonas_centro</c> por la seguridad por filas: pregunta al procedimiento
/// <c>zona_horaria_del_centro</c>, que solo devuelve el nombre de la zona. Un centro sin fila usa Madrid; si la zona no se puede leer, la excepción sube y el aviso se reintenta en la vuelta siguiente
/// (mejor esperar que enviar en una franja que no es la del centro).
/// </summary>
internal static class SqlTaskCenterZone
{
    internal const string Sql = "EXEC dbo.zona_horaria_del_centro @CenterId";

    internal static async Task<TimeZoneInfo> ReadAsync(SqlConnectionFactory connections, CenterId centerId, CancellationToken ct)
    {
        using var connection = await connections.OpenAsync(ct);
        var code = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(Sql, new { CenterId = centerId.Value }, cancellationToken: ct));
        return TimeZoneInfo.FindSystemTimeZoneById(code ?? CenterTimeZoneRules.DefaultZoneCode);
    }
}
