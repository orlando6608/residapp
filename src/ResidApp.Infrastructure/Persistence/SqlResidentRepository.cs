using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Traduce createResidentWithInitialLocation de db/repositories/resident-repository.ts: alta de residente
/// idempotente por hash de petición, con 5 escrituras atómicas (idempotencia en curso, residente,
/// episodio, ubicación inicial, evento de auditoría, cierre de idempotencia) dentro de una SqlTransaction.
/// Tablas/columnas en español desde database/scripts/0002_renombrado_espanol_sqlserver.sql.
/// </summary>
public sealed class SqlResidentRepository(SqlConnectionFactory connections) : IResidentRepository
{
    public async Task<CreateResidentResult> CreateWithInitialLocationAsync(CreateResidentInput input, CancellationToken ct = default)
    {
        var requestHash = RequestHash.Of(input);
        using var connection = await connections.OpenAsync(ct);

        var previous = await FindIdempotencyAsync(connection, null, input.AccountId.Value, input.OperationId, requestHash, ct);
        if (previous is not null)
        {
            return previous;
        }

        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            var occurredAt = DateTimeOffset.UtcNow;
            var residentId = ResidentId.New();
            var episodeId = Guid.NewGuid();
            var locationIntervalId = Guid.NewGuid();
            var result = new CreateResidentResult(residentId, episodeId, locationIntervalId);
            var activeProfileCode = input.ActiveProfile.ToCode();

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.operaciones_idempotencia (id, cuenta_id, centro_id, accion_codigo, operacion_id, hash_solicitud, estado, creado_en)
                VALUES (@Id, @AccountId, @CenterId, 'RESIDENT_CREATE', @OperationId, @RequestHash, 'IN_PROGRESS', @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, CenterId = input.CenterId.Value, input.OperationId, RequestHash = requestHash, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.residentes
                    (id, centro_id, nombre_visible, fecha_nacimiento, sexo_documentado_codigo, estado, creado_en, creado_por_cuenta_id, creado_por_perfil)
                VALUES (@ResidentId, @CenterId, @DisplayName, @BirthDate, @DocumentedSexCode, 'ACTIVE', @OccurredAt, @AccountId, @ActiveProfile)
                """, new
            {
                ResidentId = residentId.Value, CenterId = input.CenterId.Value, DisplayName = input.DisplayName.Trim(),
                BirthDate = input.BirthDate, DocumentedSexCode = input.DocumentedSexCode.ToCode(),
                OccurredAt = occurredAt, AccountId = input.AccountId.Value, ActiveProfile = activeProfileCode,
            }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.episodios_residente_centro
                    (id, residente_id, centro_id, referencia_interna, vigente_desde, creado_en, creado_por_cuenta_id, creado_por_perfil)
                VALUES (@EpisodeId, @ResidentId, @CenterId, @InternalReference, @OccurredAt, @OccurredAt, @AccountId, @ActiveProfile)
                """, new
            {
                EpisodeId = episodeId, ResidentId = residentId.Value, CenterId = input.CenterId.Value,
                input.InternalReference, OccurredAt = occurredAt, AccountId = input.AccountId.Value, ActiveProfile = activeProfileCode,
            }, transaction, cancellationToken: ct));
            var location = await ResolveLocationAsync(connection, transaction, input, ct);
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.intervalos_ubicacion_residente
                    (id, residente_id, centro_id, episodio_id, unidad_id, edificio_id, planta_id, habitacion_id, plaza_id,
                     vigente_desde, modificado_en, modificado_por_cuenta_id, modificado_por_perfil)
                VALUES (@LocationIntervalId, @ResidentId, @CenterId, @EpisodeId, @UnitId, @BuildingId, @FloorId, @RoomId, @PlaceId,
                     @OccurredAt, @OccurredAt, @AccountId, @ActiveProfile)
                """, new
            {
                LocationIntervalId = locationIntervalId, ResidentId = residentId.Value, CenterId = input.CenterId.Value,
                EpisodeId = episodeId, UnitId = input.UnitId.Value, BuildingId = location.BuildingId, FloorId = location.FloorId,
                RoomId = location.RoomId, PlaceId = location.PlaceId,
                OccurredAt = occurredAt, AccountId = input.AccountId.Value, ActiveProfile = activeProfileCode,
            }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.eventos_auditoria
                    (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
                VALUES (@Id, @AccountId, @ActiveProfile, @CenterId, @UnitId, @ResidentId, 'RESIDENT', @ResidentId, 'RESIDENT_CREATE', @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, ActiveProfile = activeProfileCode,
                CenterId = input.CenterId.Value, UnitId = input.UnitId.Value, ResidentId = residentId.Value, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            var resultJson = JsonSerializer.Serialize(result, ResidAppJson.Options);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.operaciones_idempotencia
                   SET estado = 'SUCCEEDED', recurso_resultado_id = @ResidentId, resultado_json = @ResultJson, completado_en = @OccurredAt
                 WHERE cuenta_id = @AccountId AND accion_codigo = 'RESIDENT_CREATE'
                   AND operacion_id = @OperationId AND hash_solicitud = @RequestHash AND estado = 'IN_PROGRESS'
                """, new
            {
                ResidentId = residentId.Value, ResultJson = resultJson, OccurredAt = occurredAt,
                AccountId = input.AccountId.Value, input.OperationId, RequestHash = requestHash,
            }, transaction, cancellationToken: ct));

            transaction.Commit();
            return result;
        }
        catch (Exception error)
        {
            transaction.Rollback();
            var recovered = await FindIdempotencyAsync(connection, null, input.AccountId.Value, input.OperationId, requestHash, ct);
            if (recovered is not null)
            {
                return recovered;
            }
            // Dos altas a la vez en la misma plaza: gana una y la otra choca con el índice único.
            if (error is SqlException { Number: 2601 or 2627 } duplicate && duplicate.Message.Contains("UX_rli_place_active", StringComparison.Ordinal))
            {
                throw new DomainValidationException("PLACE_OCCUPIED");
            }
            throw;
        }
    }

    /// <summary>Historia 2 (0029): la habitación y la plaza de la ubicación inicial son opcionales. Una plaza se elige entre las activas de una
    /// habitación activa de esa unidad y centro, y tiene que estar libre; una habitación, entre las activas de esa unidad. Edificio y planta no los
    /// manda el cliente: son los de la unidad.</summary>
    private static async Task<ResolvedLocation> ResolveLocationAsync(
        IDbConnection connection, IDbTransaction transaction, CreateResidentInput input, CancellationToken ct)
    {
        var parameters = new { CenterId = input.CenterId.Value, UnitId = input.UnitId.Value };
        var unit = await connection.QuerySingleOrDefaultAsync<UnitPlacement>(new CommandDefinition(
            "SELECT edificio_id AS BuildingId, planta_id AS FloorId FROM dbo.unidades WHERE id = @UnitId AND centro_id = @CenterId",
            parameters, transaction, cancellationToken: ct)) ?? new UnitPlacement(null, null);
        var roomId = input.RoomId;
        if (input.PlaceId is { } placeId)
        {
            var placeRoom = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition("""
                SELECT place.habitacion_id
                  FROM dbo.plazas place
                  JOIN dbo.habitaciones room ON room.id = place.habitacion_id AND room.centro_id = place.centro_id AND room.unidad_id = place.unidad_id
                 WHERE place.id = @PlaceId AND place.centro_id = @CenterId AND place.unidad_id = @UnitId
                   AND place.estado = 'ACTIVE' AND room.estado = 'ACTIVE'
                """, new { PlaceId = placeId, parameters.CenterId, parameters.UnitId }, transaction, cancellationToken: ct));
            if (placeRoom is null || roomId is not null && roomId != placeRoom)
            {
                throw new DomainValidationException("PLACE_INVALID");
            }

            if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(*) FROM dbo.intervalos_ubicacion_residente WHERE plaza_id = @PlaceId AND vigente_hasta IS NULL",
                    new { PlaceId = placeId }, transaction, cancellationToken: ct)) > 0)
            {
                throw new DomainValidationException("PLACE_OCCUPIED");
            }

            roomId = placeRoom;
        }
        else if (roomId is { } room && await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                    SELECT COUNT(*) FROM dbo.habitaciones
                     WHERE id = @RoomId AND centro_id = @CenterId AND unidad_id = @UnitId AND estado = 'ACTIVE'
                    """, new { RoomId = room, parameters.CenterId, parameters.UnitId }, transaction, cancellationToken: ct)) == 0)
        {
            throw new DomainValidationException("ROOM_INVALID");
        }

        return new ResolvedLocation(unit.BuildingId, unit.FloorId, roomId, input.PlaceId);
    }

    private sealed record UnitPlacement(Guid? BuildingId, Guid? FloorId);

    private sealed record ResolvedLocation(Guid? BuildingId, Guid? FloorId, Guid? RoomId, Guid? PlaceId);

    private static async Task<CreateResidentResult?> FindIdempotencyAsync(
        IDbConnection connection, IDbTransaction? transaction, Guid accountId, Guid operationId, string requestHash, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<IdempotencyRow>(new CommandDefinition("""
            SELECT hash_solicitud AS RequestHash, estado AS Status, resultado_json AS ResultJson
              FROM dbo.operaciones_idempotencia
             WHERE cuenta_id = @AccountId AND accion_codigo = 'RESIDENT_CREATE' AND operacion_id = @OperationId
            """, new { AccountId = accountId, OperationId = operationId }, transaction, cancellationToken: ct));
        if (row is null)
        {
            return null;
        }
        if (row.RequestHash != requestHash)
        {
            throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
        }
        if (row.Status == "SUCCEEDED" && row.ResultJson is not null)
        {
            return JsonSerializer.Deserialize<CreateResidentResult>(row.ResultJson, ResidAppJson.Options);
        }
        throw new DomainValidationException("IDEMPOTENCY_OPERATION_IN_PROGRESS");
    }

    private sealed record IdempotencyRow(string RequestHash, string Status, string? ResultJson);
}

file static class RequestHash
{
    /// <summary>Determinismo propio del lado C#, no paridad byte a byte con el hash del prototipo TS:
    /// cada sistema tiene su propio almacén de idempotencia y no necesitan coincidir entre sí.</summary>
    public static string Of(CreateResidentInput input)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            AccountId = input.AccountId.Value, ActiveProfile = input.ActiveProfile.ToCode(), CenterId = input.CenterId.Value,
            UnitId = input.UnitId.Value, input.DisplayName, BirthDate = input.BirthDate.ToString("yyyy-MM-dd"),
            DocumentedSexCode = input.DocumentedSexCode.ToCode(), input.InternalReference, input.BuildingId,
            input.FloorId, input.RoomId, input.PlaceId, OperationId = input.OperationId,
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
