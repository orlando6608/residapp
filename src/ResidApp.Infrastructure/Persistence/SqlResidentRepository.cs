using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
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
                INSERT INTO dbo.operaciones_idempotencia (id, cuenta_id, accion_codigo, operacion_id, hash_solicitud, estado, creado_en)
                VALUES (@Id, @AccountId, 'RESIDENT_CREATE', @OperationId, @RequestHash, 'IN_PROGRESS', @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, input.OperationId, RequestHash = requestHash, OccurredAt = occurredAt,
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

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.intervalos_ubicacion_residente
                    (id, residente_id, centro_id, episodio_id, unidad_id, edificio_id, planta_id, habitacion_id, plaza_id,
                     vigente_desde, modificado_en, modificado_por_cuenta_id, modificado_por_perfil)
                VALUES (@LocationIntervalId, @ResidentId, @CenterId, @EpisodeId, @UnitId, @BuildingId, @FloorId, @RoomId, @PlaceId,
                     @OccurredAt, @OccurredAt, @AccountId, @ActiveProfile)
                """, new
            {
                LocationIntervalId = locationIntervalId, ResidentId = residentId.Value, CenterId = input.CenterId.Value,
                EpisodeId = episodeId, UnitId = input.UnitId.Value, input.BuildingId, input.FloorId, input.RoomId, input.PlaceId,
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
        catch
        {
            transaction.Rollback();
            var recovered = await FindIdempotencyAsync(connection, null, input.AccountId.Value, input.OperationId, requestHash, ct);
            if (recovered is not null)
            {
                return recovered;
            }
            throw;
        }
    }

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
