using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>Traduce el registro idempotente de un evento clínico propio de Enfermería (ENF-16), mismo
/// patrón que SqlDailyClosureRepository.RegisterAsync: hash de petición + fila IN_PROGRESS/SUCCEEDED en
/// dbo.operaciones_idempotencia dentro de la misma transacción.</summary>
public sealed class SqlClinicalEventRepository(SqlConnectionFactory connections) : IClinicalEventRepository
{
    private const string ActionCode = "CLINICAL_EVENT_REGISTER";

    public async Task<ClinicalEventResult> RegisterAsync(RegisterClinicalEventInput input, CancellationToken ct = default)
    {
        var requestHash = RequestHash.Of(input);
        using var connection = await connections.OpenAsync(ct);

        var previous = await FindIdempotencyAsync(connection, input.AccountId.Value, input.OperationId, requestHash, ct);
        if (previous is not null)
        {
            return previous;
        }

        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            var occurredAt = DateTimeOffset.UtcNow;
            var eventId = Guid.NewGuid();
            var result = new ClinicalEventResult(eventId, occurredAt);

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.operaciones_idempotencia (id, cuenta_id, accion_codigo, operacion_id, hash_solicitud, estado, creado_en)
                VALUES (@Id, @AccountId, @ActionCode, @OperationId, @RequestHash, 'IN_PROGRESS', @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, ActionCode, input.OperationId,
                RequestHash = requestHash, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.eventos_clinicos
                    (id, residente_id, centro_id, unidad_id, observacion, clasificacion_codigo, datos_clinicos_pertinentes,
                     registrado_por_cuenta_id, registrado_por_perfil, ocurrido_en)
                VALUES (@EventId, @ResidentId, @CenterId, @UnitId, @Observation, @ClassificationCode, @ClinicalData,
                        @AccountId, 'ENFERMERIA', @OccurredAt)
                """, new
            {
                EventId = eventId, ResidentId = input.ResidentId.Value, CenterId = input.CenterId.Value, UnitId = input.UnitId.Value,
                input.Observation, ClassificationCode = input.Classification.ToCode(), input.ClinicalData,
                AccountId = input.AccountId.Value, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.eventos_auditoria
                    (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
                VALUES (@Id, @AccountId, 'ENFERMERIA', @CenterId, @UnitId, @ResidentId, 'CLINICAL_EVENT', @EventId, @ActionCode, @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, CenterId = input.CenterId.Value, UnitId = input.UnitId.Value,
                ResidentId = input.ResidentId.Value, EventId = eventId, ActionCode, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            var resultJson = JsonSerializer.Serialize(result, ResidAppJson.Options);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.operaciones_idempotencia
                   SET estado = 'SUCCEEDED', recurso_resultado_id = @EventId, resultado_json = @ResultJson, completado_en = @OccurredAt
                 WHERE cuenta_id = @AccountId AND accion_codigo = @ActionCode
                   AND operacion_id = @OperationId AND hash_solicitud = @RequestHash AND estado = 'IN_PROGRESS'
                """, new
            {
                EventId = eventId, ResultJson = resultJson, OccurredAt = occurredAt,
                AccountId = input.AccountId.Value, ActionCode, input.OperationId, RequestHash = requestHash,
            }, transaction, cancellationToken: ct));

            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            var recovered = await FindIdempotencyAsync(connection, input.AccountId.Value, input.OperationId, requestHash, ct);
            if (recovered is not null)
            {
                return recovered;
            }
            throw;
        }
    }

    private static async Task<ClinicalEventResult?> FindIdempotencyAsync(
        IDbConnection connection, Guid accountId, Guid operationId, string requestHash, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<IdempotencyRow>(new CommandDefinition("""
            SELECT hash_solicitud AS RequestHash, estado AS Status, resultado_json AS ResultJson
              FROM dbo.operaciones_idempotencia
             WHERE cuenta_id = @AccountId AND accion_codigo = @ActionCode AND operacion_id = @OperationId
            """, new { AccountId = accountId, ActionCode, OperationId = operationId }, cancellationToken: ct));
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
            return JsonSerializer.Deserialize<ClinicalEventResult>(row.ResultJson, ResidAppJson.Options);
        }
        throw new DomainValidationException("IDEMPOTENCY_OPERATION_IN_PROGRESS");
    }

    private sealed record IdempotencyRow(string RequestHash, string Status, string? ResultJson);
}

file static class RequestHash
{
    public static string Of(RegisterClinicalEventInput input)
    {
        var canonical = JsonSerializer.Serialize(new object?[]
        {
            input.AccountId.Value, input.CenterId.Value, input.UnitId.Value, input.ResidentId.Value,
            input.Observation, input.Classification.ToCode(), input.ClinicalData, input.OperationId,
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
