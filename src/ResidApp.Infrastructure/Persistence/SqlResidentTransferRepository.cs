using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Traslado del residente (script 0040). En una transacción: bloquea la ubicación vigente (UPDLOCK) y comprueba que sigue en la unidad
/// que enseñaba el formulario; valida el destino como el alta; cierra la ubicación y abre la nueva; guarda el traslado
/// (dbo.traslados_residente, inmutable); pasa a la unidad de destino los eventos no cerrados (TR_ea_transition_guard solo lo admite con
/// ese traslado creado en la misma transacción); cancela el borrador de basal si cambia de unidad; y audita RESIDENT_TRANSFER. Un reenvío
/// con el mismo OperationId no repite nada.
/// </summary>
public sealed class SqlResidentTransferRepository(SqlConnectionFactory connections) : IResidentTransferRepository
{
    private const string BaselineDraftCancellationReason = "Traslado de unidad: el basal se rehace en la unidad de destino.";

    public async Task<TransferResidentResult> TransferAsync(TransferResidentInput input, CancellationToken ct = default)
    {
        var target = input.Target;
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            var parameters = new { ResidentId = target.ResidentId.Value, CenterId = target.CenterId.Value, TransferId = input.OperationId };
            var previous = await connection.QuerySingleOrDefaultAsync<PreviousRow>(new CommandDefinition("""
                SELECT residente_id AS ResidentId, unidad_destino_id AS DestinationUnitId, eventos_trasladados AS OpenEventsMoved,
                       borrador_basal_cancelado AS BaselineDraftCancelled
                  FROM dbo.traslados_residente WHERE id = @TransferId AND centro_id = @CenterId
                """, parameters, transaction, cancellationToken: ct));
            if (previous is not null)
            {
                // Un reenvío del mismo formulario no repite el traslado; el mismo identificador con otro destino es otra petición.
                return previous.ResidentId == target.ResidentId.Value && previous.DestinationUnitId == input.DestinationUnitId.Value
                    ? new TransferResidentResult(previous.OpenEventsMoved, previous.BaselineDraftCancelled)
                    : throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
            }

            var current = await connection.QuerySingleOrDefaultAsync<CurrentRow>(new CommandDefinition("""
                SELECT location.id AS IntervalId, location.episodio_id AS EpisodeId, location.unidad_id AS UnitId,
                       location.habitacion_id AS RoomId, location.plaza_id AS PlaceId, location.vigente_desde AS Since
                  FROM dbo.intervalos_ubicacion_residente location WITH (UPDLOCK, ROWLOCK)
                 WHERE location.residente_id = @ResidentId AND location.centro_id = @CenterId AND location.vigente_hasta IS NULL
                """, parameters, transaction, cancellationToken: ct))
                ?? throw new DomainValidationException("RESIDENT_TRANSFER_CONFLICT");
            if (current.UnitId != input.ExpectedUnitId.Value)
            {
                throw new DomainValidationException("RESIDENT_TRANSFER_CONFLICT");
            }

            if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(*) FROM dbo.unidades WHERE id = @UnitId AND centro_id = @CenterId AND estado = 'ACTIVE'",
                    new { UnitId = input.DestinationUnitId.Value, parameters.CenterId }, transaction, cancellationToken: ct)) == 0)
            {
                throw new DomainValidationException("UNIT_INVALID");
            }

            var location = await SqlResidentRepository.ResolveLocationAsync(
                connection, transaction, target.CenterId, input.DestinationUnitId, input.RoomId, input.PlaceId, ct);
            var changesUnit = current.UnitId != input.DestinationUnitId.Value;
            if (!changesUnit && current.RoomId == location.RoomId && current.PlaceId == location.PlaceId)
            {
                throw new DomainValidationException("RESIDENT_TRANSFER_INVALID");
            }

            var occurredAt = DateTimeOffset.UtcNow;
            var since = new DateTimeOffset(DateTime.SpecifyKind(current.Since, DateTimeKind.Utc));
            if (occurredAt <= since)
            {
                occurredAt = since.AddMilliseconds(1);
            }

            var eventIds = changesUnit
                ? (await connection.QueryAsync<Guid>(new CommandDefinition("""
                    SELECT id FROM dbo.eventos_asistenciales WITH (UPDLOCK, HOLDLOCK)
                     WHERE residente_id = @ResidentId AND centro_id = @CenterId AND estado_codigo <> 'CERRADO'
                    """, parameters, transaction, cancellationToken: ct))).ToList()
                : [];
            var draftId = changesUnit
                ? await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition("""
                    SELECT id FROM dbo.basales_borrador WITH (UPDLOCK, HOLDLOCK)
                     WHERE residente_id = @ResidentId AND centro_id = @CenterId AND estado = 'ACTIVE'
                    """, parameters, transaction, cancellationToken: ct))
                : null;

            var newIntervalId = Guid.NewGuid();
            var write = new
            {
                parameters.ResidentId, parameters.CenterId, parameters.TransferId, current.EpisodeId, OriginUnitId = current.UnitId,
                DestinationUnitId = input.DestinationUnitId.Value, OriginIntervalId = current.IntervalId, NewIntervalId = newIntervalId,
                location.BuildingId, location.FloorId, location.RoomId, location.PlaceId, AccountId = target.AccountId.Value,
                OccurredAt = occurredAt, EventCount = eventIds.Count, DraftCancelled = draftId is not null, DraftId = draftId,
                CancellationReason = BaselineDraftCancellationReason, AuditId = Guid.NewGuid(),
            };
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.intervalos_ubicacion_residente SET vigente_hasta = @OccurredAt WHERE id = @OriginIntervalId;

                INSERT INTO dbo.intervalos_ubicacion_residente
                    (id, residente_id, centro_id, episodio_id, unidad_id, edificio_id, planta_id, habitacion_id, plaza_id,
                     vigente_desde, modificado_en, modificado_por_cuenta_id, modificado_por_perfil)
                VALUES (@NewIntervalId, @ResidentId, @CenterId, @EpisodeId, @DestinationUnitId, @BuildingId, @FloorId, @RoomId, @PlaceId,
                     @OccurredAt, @OccurredAt, @AccountId, 'ADMINISTRACION');

                INSERT INTO dbo.traslados_residente
                    (id, residente_id, centro_id, episodio_id, unidad_origen_id, unidad_destino_id, intervalo_origen_id, intervalo_destino_id,
                     eventos_trasladados, borrador_basal_cancelado, trasladado_por_cuenta_id, trasladado_por_perfil, trasladado_en)
                VALUES (@TransferId, @ResidentId, @CenterId, @EpisodeId, @OriginUnitId, @DestinationUnitId, @OriginIntervalId, @NewIntervalId,
                     @EventCount, @DraftCancelled, @AccountId, 'ADMINISTRACION', @OccurredAt);

                INSERT INTO dbo.eventos_auditoria
                    (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
                VALUES (@AuditId, @AccountId, 'ADMINISTRACION', @CenterId, @DestinationUnitId, @ResidentId, 'RESIDENT', @ResidentId,
                     'RESIDENT_TRANSFER', @OccurredAt);
                """, write, transaction, cancellationToken: ct));

            if (eventIds.Count > 0)
            {
                var moved = await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.eventos_asistenciales SET unidad_id = @DestinationUnitId, revision = revision + 1
                     WHERE residente_id = @ResidentId AND centro_id = @CenterId AND estado_codigo <> 'CERRADO'
                    """, write, transaction, cancellationToken: ct));
                if (moved != eventIds.Count)
                {
                    throw new DomainValidationException("RESIDENT_TRANSFER_CONFLICT");
                }
            }

            if (draftId is not null)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.basales_borrador
                       SET estado = 'CANCELLED', cancelado_en = @OccurredAt, cancelado_por_cuenta_id = @AccountId,
                           cancelado_por_perfil = 'ADMINISTRACION', motivo_cancelacion = @CancellationReason,
                           cancelado_por_traslado_id = @TransferId
                     WHERE id = @DraftId AND estado = 'ACTIVE'
                    """, write, transaction, cancellationToken: ct));
            }

            transaction.Commit();
            return new TransferResidentResult(eventIds.Count, draftId is not null);
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            transaction.Rollback();
            // Dos traslados a la misma plaza a la vez: gana uno y el otro choca con el índice único. Un reenvío simultáneo del mismo
            // formulario choca con la clave del traslado.
            throw error.Message.Contains("UX_rli_place_active", StringComparison.Ordinal)
                ? new DomainValidationException("PLACE_OCCUPIED")
                : new DomainValidationException("RESIDENT_TRANSFER_CONFLICT");
        }
    }

    private sealed record PreviousRow(Guid ResidentId, Guid DestinationUnitId, int OpenEventsMoved, bool BaselineDraftCancelled);

    private sealed record CurrentRow(Guid IntervalId, Guid EpisodeId, Guid UnitId, Guid? RoomId, Guid? PlaceId, DateTime Since);
}
