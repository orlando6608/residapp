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
                INSERT INTO dbo.idempotency_operations (id, account_id, action_code, operation_id, request_hash, status, created_at)
                VALUES (@Id, @AccountId, 'RESIDENT_CREATE', @OperationId, @RequestHash, 'IN_PROGRESS', @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, input.OperationId, RequestHash = requestHash, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.residents
                    (id, center_id, display_name, birth_date, documented_sex_code, status, created_at, created_by_account_id, created_by_profile)
                VALUES (@ResidentId, @CenterId, @DisplayName, @BirthDate, @DocumentedSexCode, 'ACTIVE', @OccurredAt, @AccountId, @ActiveProfile)
                """, new
            {
                ResidentId = residentId.Value, CenterId = input.CenterId.Value, DisplayName = input.DisplayName.Trim(),
                BirthDate = input.BirthDate, DocumentedSexCode = input.DocumentedSexCode.ToCode(),
                OccurredAt = occurredAt, AccountId = input.AccountId.Value, ActiveProfile = activeProfileCode,
            }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.resident_center_episodes
                    (id, resident_id, center_id, internal_reference, valid_from, created_at, created_by_account_id, created_by_profile)
                VALUES (@EpisodeId, @ResidentId, @CenterId, @InternalReference, @OccurredAt, @OccurredAt, @AccountId, @ActiveProfile)
                """, new
            {
                EpisodeId = episodeId, ResidentId = residentId.Value, CenterId = input.CenterId.Value,
                input.InternalReference, OccurredAt = occurredAt, AccountId = input.AccountId.Value, ActiveProfile = activeProfileCode,
            }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.resident_location_intervals
                    (id, resident_id, center_id, episode_id, unit_id, building_id, floor_id, room_id, place_id,
                     valid_from, changed_at, changed_by_account_id, changed_by_profile)
                VALUES (@LocationIntervalId, @ResidentId, @CenterId, @EpisodeId, @UnitId, @BuildingId, @FloorId, @RoomId, @PlaceId,
                     @OccurredAt, @OccurredAt, @AccountId, @ActiveProfile)
                """, new
            {
                LocationIntervalId = locationIntervalId, ResidentId = residentId.Value, CenterId = input.CenterId.Value,
                EpisodeId = episodeId, UnitId = input.UnitId.Value, input.BuildingId, input.FloorId, input.RoomId, input.PlaceId,
                OccurredAt = occurredAt, AccountId = input.AccountId.Value, ActiveProfile = activeProfileCode,
            }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.audit_events
                    (id, account_id, active_profile, center_id, unit_id, resident_id, resource_type, resource_id, action_code, occurred_at)
                VALUES (@Id, @AccountId, @ActiveProfile, @CenterId, @UnitId, @ResidentId, 'RESIDENT', @ResidentId, 'RESIDENT_CREATE', @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, ActiveProfile = activeProfileCode,
                CenterId = input.CenterId.Value, UnitId = input.UnitId.Value, ResidentId = residentId.Value, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            var resultJson = JsonSerializer.Serialize(result, ResidAppJson.Options);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.idempotency_operations
                   SET status = 'SUCCEEDED', result_resource_id = @ResidentId, result_json = @ResultJson, completed_at = @OccurredAt
                 WHERE account_id = @AccountId AND action_code = 'RESIDENT_CREATE'
                   AND operation_id = @OperationId AND request_hash = @RequestHash AND status = 'IN_PROGRESS'
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
            SELECT request_hash AS RequestHash, status AS Status, result_json AS ResultJson
              FROM dbo.idempotency_operations
             WHERE account_id = @AccountId AND action_code = 'RESIDENT_CREATE' AND operation_id = @OperationId
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
