using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Baja, reactivación y suspensión del residente (script 0041). Cada operación es una transacción que bloquea al residente (UPDLOCK),
/// comprueba que su estado es el que necesita y deja su fila de auditoría. El identificador de operación es el de la fila que crea
/// (baja, episodio nuevo o suspensión), así que un reenvío del formulario no repite nada.
/// </summary>
public sealed class SqlResidentStatusRepository(SqlConnectionFactory connections) : IResidentStatusRepository
{
    private const string InsertAudit = """
        INSERT INTO dbo.eventos_auditoria
            (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
        VALUES (NEWID(), @AccountId, 'ADMINISTRACION', @CenterId, @UnitId, @ResidentId, 'RESIDENT', @ResidentId, @Action, @OccurredAt);
        """;

    public async Task<int> DischargeAsync(DischargeResidentInput input, CancellationToken ct = default)
    {
        var target = input.Target;
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            var p = new { ResidentId = target.ResidentId.Value, CenterId = target.CenterId.Value, DischargeId = input.OperationId };
            if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(*) FROM dbo.bajas_residente WHERE id = @DischargeId AND residente_id = @ResidentId AND centro_id = @CenterId",
                    p, transaction, cancellationToken: ct)) > 0)
            {
                return 0;
            }

            await LockResidentAsync(connection, transaction, p.ResidentId, p.CenterId, "ACTIVE", ct);
            var current = await connection.QuerySingleOrDefaultAsync<CurrentLocationRow>(new CommandDefinition("""
                SELECT location.id AS IntervalId, location.episodio_id AS EpisodeId, location.unidad_id AS UnitId, location.vigente_desde AS Since
                  FROM dbo.intervalos_ubicacion_residente location WITH (UPDLOCK, ROWLOCK)
                 WHERE location.residente_id = @ResidentId AND location.centro_id = @CenterId AND location.vigente_hasta IS NULL
                """, p, transaction, cancellationToken: ct)) ?? throw new DomainValidationException("RESIDENT_STATUS_CONFLICT");
            var episodeSince = await connection.ExecuteScalarAsync<DateTime>(new CommandDefinition(
                "SELECT vigente_desde FROM dbo.episodios_residente_centro WHERE id = @EpisodeId", new { current.EpisodeId }, transaction, cancellationToken: ct));

            var occurredAt = AfterAll(DateTimeOffset.UtcNow, current.Since, episodeSince);
            await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE dbo.suspensiones_residente
                   SET finalizada_en = @OccurredAt, finalizada_por_cuenta_id = @AccountId
                 WHERE residente_id = @ResidentId AND centro_id = @CenterId AND finalizada_en IS NULL;

                UPDATE dbo.intervalos_ubicacion_residente SET vigente_hasta = @OccurredAt WHERE id = @IntervalId;
                UPDATE dbo.episodios_residente_centro SET vigente_hasta = @OccurredAt WHERE id = @EpisodeId;

                UPDATE dbo.residentes
                   SET estado = 'INACTIVE', motivo_inactivacion = @Description, inactivado_en = @OccurredAt,
                       inactivado_por_cuenta_id = @AccountId, inactivado_por_perfil = 'ADMINISTRACION'
                 WHERE id = @ResidentId AND centro_id = @CenterId;

                INSERT INTO dbo.bajas_residente
                    (id, residente_id, centro_id, episodio_id, unidad_id, motivo_codigo, motivo_texto, conservar_hasta, dada_por_cuenta_id, dada_en)
                VALUES (@DischargeId, @ResidentId, @CenterId, @EpisodeId, @UnitId, @ReasonCode, @ReasonText, @RetainUntil, @AccountId, @OccurredAt);

                {InsertAudit}
                """, new
            {
                p.ResidentId, p.CenterId, p.DischargeId, current.IntervalId, current.EpisodeId, UnitId = current.UnitId,
                ReasonCode = input.Reason.ToCode(), input.ReasonText, Description = ResidentDischarge.Describe(input.Reason, input.ReasonText),
                RetainUntil = ResidentDischarge.RetainUntil(occurredAt).ToDateTime(TimeOnly.MinValue),
                AccountId = target.AccountId.Value, OccurredAt = occurredAt, Action = "RESIDENT_DISCHARGE",
            }, transaction, cancellationToken: ct));

            // CJ (2026-10-07): la baja por fallecimiento cierra sola los episodios abiertos, con una anotación de sistema (script 0042).
            // TR_ea_transition_guard lo admite solo con esta baja creada en la misma transacción.
            var closed = input.Reason != ResidentDischargeReason.Fallecimiento ? 0 : await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.eventos_asistenciales
                   SET estado_codigo = 'CERRADO', revision = revision + 1, cerrado_por_cuenta_id = @AccountId, cerrado_en = @OccurredAt,
                       comunicacion_familiar_codigo = 'NO_COMUNICAR', cierre_sistema_codigo = 'FALLECIMIENTO'
                 WHERE residente_id = @ResidentId AND centro_id = @CenterId AND estado_codigo <> 'CERRADO'
                """, new { p.ResidentId, p.CenterId, AccountId = target.AccountId.Value, OccurredAt = occurredAt }, transaction, cancellationToken: ct));
            transaction.Commit();
            return closed;
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            transaction.Rollback();
            throw new DomainValidationException("RESIDENT_STATUS_CONFLICT");
        }
    }

    public async Task ReactivateAsync(ReactivateResidentInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            var p = new { ResidentId = input.ResidentId.Value, CenterId = input.CenterId.Value, EpisodeId = input.OperationId };
            if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(*) FROM dbo.episodios_residente_centro WHERE id = @EpisodeId AND residente_id = @ResidentId AND centro_id = @CenterId",
                    p, transaction, cancellationToken: ct)) > 0)
            {
                return;
            }

            await LockResidentAsync(connection, transaction, p.ResidentId, p.CenterId, "INACTIVE", ct);
            var discharge = await connection.QuerySingleOrDefaultAsync<OpenDischargeRow>(new CommandDefinition("""
                SELECT discharge.id AS DischargeId, discharge.dada_en AS DischargedAt, episode.referencia_interna AS InternalReference
                  FROM dbo.bajas_residente discharge
                  JOIN dbo.episodios_residente_centro episode ON episode.id = discharge.episodio_id
                 WHERE discharge.residente_id = @ResidentId AND discharge.centro_id = @CenterId AND discharge.reactivada_en IS NULL
                """, p, transaction, cancellationToken: ct)) ?? throw new DomainValidationException("RESIDENT_STATUS_CONFLICT");
            if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(*) FROM dbo.unidades WHERE id = @UnitId AND centro_id = @CenterId AND estado = 'ACTIVE'",
                    new { UnitId = input.UnitId.Value, p.CenterId }, transaction, cancellationToken: ct)) == 0)
            {
                throw new DomainValidationException("UNIT_INVALID");
            }

            var location = await SqlResidentRepository.ResolveLocationAsync(
                connection, transaction, input.CenterId, input.UnitId, input.RoomId, input.PlaceId, ct);
            var occurredAt = AfterAll(DateTimeOffset.UtcNow, discharge.DischargedAt);
            await connection.ExecuteAsync(new CommandDefinition($"""
                INSERT INTO dbo.episodios_residente_centro
                    (id, residente_id, centro_id, referencia_interna, vigente_desde, creado_en, creado_por_cuenta_id, creado_por_perfil)
                VALUES (@EpisodeId, @ResidentId, @CenterId, @InternalReference, @OccurredAt, @OccurredAt, @AccountId, 'ADMINISTRACION');

                INSERT INTO dbo.intervalos_ubicacion_residente
                    (id, residente_id, centro_id, episodio_id, unidad_id, edificio_id, planta_id, habitacion_id, plaza_id,
                     vigente_desde, modificado_en, modificado_por_cuenta_id, modificado_por_perfil)
                VALUES (NEWID(), @ResidentId, @CenterId, @EpisodeId, @UnitId, @BuildingId, @FloorId, @RoomId, @PlaceId,
                     @OccurredAt, @OccurredAt, @AccountId, 'ADMINISTRACION');

                UPDATE dbo.residentes
                   SET estado = 'ACTIVE', motivo_inactivacion = NULL, inactivado_en = NULL,
                       inactivado_por_cuenta_id = NULL, inactivado_por_perfil = NULL
                 WHERE id = @ResidentId AND centro_id = @CenterId;

                UPDATE dbo.bajas_residente SET reactivada_en = @OccurredAt, reactivada_por_cuenta_id = @AccountId WHERE id = @DischargeId;

                {InsertAudit}
                """, new
            {
                p.ResidentId, p.CenterId, p.EpisodeId, discharge.DischargeId, discharge.InternalReference, UnitId = input.UnitId.Value,
                location.BuildingId, location.FloorId, location.RoomId, location.PlaceId,
                AccountId = input.AccountId.Value, OccurredAt = occurredAt, Action = "RESIDENT_REACTIVATE",
            }, transaction, cancellationToken: ct));
            transaction.Commit();
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            transaction.Rollback();
            throw error.Message.Contains("UX_rli_place_active", StringComparison.Ordinal)
                ? new DomainValidationException("PLACE_OCCUPIED")
                : new DomainValidationException("RESIDENT_STATUS_CONFLICT");
        }
    }

    public async Task SuspendAsync(SuspendResidentInput input, CancellationToken ct = default)
    {
        var target = input.Target;
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            var p = new { ResidentId = target.ResidentId.Value, CenterId = target.CenterId.Value };
            if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(*) FROM dbo.suspensiones_residente WHERE id = @SuspensionId AND residente_id = @ResidentId AND centro_id = @CenterId",
                    new { SuspensionId = input.OperationId, p.ResidentId, p.CenterId }, transaction, cancellationToken: ct)) > 0)
            {
                return;
            }

            await LockResidentAsync(connection, transaction, p.ResidentId, p.CenterId, "ACTIVE", ct);
            await connection.ExecuteAsync(new CommandDefinition($"""
                INSERT INTO dbo.suspensiones_residente (id, residente_id, centro_id, unidad_id, nota, iniciada_por_cuenta_id, iniciada_en)
                VALUES (@SuspensionId, @ResidentId, @CenterId, @UnitId, @Note, @AccountId, @OccurredAt);

                {InsertAudit}
                """, new
            {
                SuspensionId = input.OperationId, p.ResidentId, p.CenterId, UnitId = target.UnitId.Value, input.Note,
                AccountId = target.AccountId.Value, OccurredAt = DateTimeOffset.UtcNow, Action = "RESIDENT_SUSPEND",
            }, transaction, cancellationToken: ct));
            transaction.Commit();
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            // UX_spr_open: ya tiene una suspensión abierta.
            transaction.Rollback();
            throw new DomainValidationException("RESIDENT_STATUS_CONFLICT");
        }
    }

    public async Task ResumeAsync(AdministrativeResidentTarget target, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var p = new { ResidentId = target.ResidentId.Value, CenterId = target.CenterId.Value };
        var since = await connection.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition("""
            SELECT iniciada_en FROM dbo.suspensiones_residente WITH (UPDLOCK, ROWLOCK)
             WHERE residente_id = @ResidentId AND centro_id = @CenterId AND finalizada_en IS NULL
            """, p, transaction, cancellationToken: ct)) ?? throw new DomainValidationException("RESIDENT_STATUS_CONFLICT");
        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE dbo.suspensiones_residente SET finalizada_en = @OccurredAt, finalizada_por_cuenta_id = @AccountId
             WHERE residente_id = @ResidentId AND centro_id = @CenterId AND finalizada_en IS NULL;

            {InsertAudit}
            """, new
        {
            p.ResidentId, p.CenterId, UnitId = target.UnitId.Value, AccountId = target.AccountId.Value,
            OccurredAt = AfterAll(DateTimeOffset.UtcNow, since), Action = "RESIDENT_RESUME",
        }, transaction, cancellationToken: ct));
        transaction.Commit();
    }

    /// <summary>Bloquea al residente y exige el estado esperado; si no, RESIDENT_STATUS_CONFLICT.</summary>
    private static async Task LockResidentAsync(
        SqlConnection connection, SqlTransaction transaction, Guid residentId, Guid centerId, string expectedState, CancellationToken ct)
    {
        var state = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT estado FROM dbo.residentes WITH (UPDLOCK, ROWLOCK) WHERE id = @residentId AND centro_id = @centerId",
            new { residentId, centerId }, transaction, cancellationToken: ct));
        if (state != expectedState)
        {
            throw new DomainValidationException("RESIDENT_STATUS_CONFLICT");
        }
    }

    /// <summary>Ahora, o un milisegundo después del más reciente de los instantes dados si el reloj va por detrás (las restricciones de
    /// las tablas exigen que un cierre sea posterior a su inicio).</summary>
    private static DateTimeOffset AfterAll(DateTimeOffset now, params DateTime[] earlier)
    {
        var latest = earlier.Select(d => new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc))).Max();
        return now <= latest ? latest.AddMilliseconds(1) : now;
    }

    private sealed record CurrentLocationRow(Guid IntervalId, Guid EpisodeId, Guid UnitId, DateTime Since);

    private sealed record OpenDischargeRow(Guid DischargeId, DateTime DischargedAt, string? InternalReference);
}
