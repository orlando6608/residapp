using Dapper;
using Microsoft.Data.SqlClient;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>HIS-03: guarda, en la misma transacción que crea el evento asistencial, la versión del basal y el
/// intervalo de ubicación vigentes en ese momento (dbo.eventos_contexto, 0019). Así el Historial muestra el
/// contexto de la fecha del evento y no el actual.</summary>
internal static class EventContextSnapshot
{
    public static Task CaptureAsync(
        SqlConnection connection, SqlTransaction transaction, Guid eventId, Guid residentId, Guid centerId,
        DateTimeOffset occurredAt, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.eventos_contexto (evento_id, residente_id, centro_id, version_basal_id, intervalo_ubicacion_id, capturado_en)
            SELECT @EventId, @ResidentId, @CenterId,
                   (SELECT version_basal_id FROM dbo.basales_vigentes_residente WHERE residente_id = @ResidentId AND centro_id = @CenterId),
                   (SELECT id FROM dbo.intervalos_ubicacion_residente
                     WHERE residente_id = @ResidentId AND centro_id = @CenterId AND vigente_hasta IS NULL),
                   @OccurredAt
            """, new { EventId = eventId, ResidentId = residentId, CenterId = centerId, OccurredAt = occurredAt },
            transaction, cancellationToken: ct));
}
