using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>La petición del historial tras un traslado (script 0077). La declaración y su fila de auditoría se escriben en la misma transacción; la comprobación
/// de que el residente está en el ámbito y de que hay episodios cerrados de una unidad anterior que abrir es la del predicado de las lecturas
/// (SqlChangeInboxDirectory.TransferHistoryScopedEventsFrom), así que pedir no concede nada que la lectura no concediera.</summary>
public sealed class SqlTransferHistoryAccess(SqlConnectionFactory connections) : ITransferHistoryAccess
{
    public async Task<Guid?> DeclareAsync(
        string externalSubject, Guid profileScopeId, CenterId centerId, ResidentId residentId, string reason, int minutes, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            var id = Guid.NewGuid();
            var written = await connection.ExecuteScalarAsync<int>(new CommandDefinition($"""
                DECLARE @AccountId UNIQUEIDENTIFIER = (SELECT id FROM dbo.cuentas WHERE sujeto_externo = @ExternalSubject AND estado = 'ACTIVE');

                IF @AccountId IS NULL OR NOT EXISTS (
                    SELECT 1
                    {SqlChangeInboxDirectory.TransferHistoryScopedEventsFrom(requireDeclaration: false)}
                       AND ea.residente_id = @ResidentId AND profile.cuenta_id = @AccountId)
                BEGIN
                    SELECT 0;
                    RETURN;
                END

                -- Una petición por cuenta: pedir otra (otro residente, o renovar con un motivo nuevo) termina la anterior.
                UPDATE dbo.declaraciones_historial_traslado SET terminada_en = SYSUTCDATETIME()
                 WHERE cuenta_id = @AccountId AND terminada_en IS NULL AND caduca_en > SYSUTCDATETIME();

                INSERT INTO dbo.declaraciones_historial_traslado
                    (id, centro_id, cuenta_id, ambito_perfil_id, residente_id, motivo, creada_en, caduca_en)
                VALUES (@Id, @CenterId, @AccountId, @ProfileScopeId, @ResidentId, @Reason,
                        SYSUTCDATETIME(), DATEADD(MINUTE, @Minutes, SYSUTCDATETIME()));

                INSERT INTO dbo.eventos_auditoria
                    (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, justificacion, ocurrido_en)
                SELECT NEWID(), @AccountId, profile.perfil_codigo, @CenterId,
                       (SELECT TOP (1) location.unidad_id FROM dbo.intervalos_ubicacion_residente location
                         WHERE location.residente_id = @ResidentId AND location.centro_id = @CenterId AND location.vigente_hasta IS NULL),
                       @ResidentId, 'RESIDENT_TRANSFER_HISTORY', @Id, 'TRANSFER_HISTORY_READ', @Reason, SYSUTCDATETIME()
                  FROM dbo.ambitos_perfil profile WHERE profile.id = @ProfileScopeId;

                SELECT 1;
                """, new
            {
                ExternalSubject = externalSubject, ProfileScopeId = profileScopeId, CenterId = centerId.Value, ResidentId = residentId.Value,
                Id = id, Reason = reason, Minutes = minutes,
            }, transaction, cancellationToken: ct));
            if (written == 0)
            {
                transaction.Rollback();
                return null;
            }

            transaction.Commit();
            return id;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
