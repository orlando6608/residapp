using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>Traduce el registro idempotente de un cierre cotidiano (AUX-04/AUX-05), mismo patrón que
/// SqlResidentRepository.CreateWithInitialLocationAsync: hash de petición + fila IN_PROGRESS/SUCCEEDED en
/// dbo.operaciones_idempotencia dentro de la misma transacción.</summary>
public sealed class SqlDailyClosureRepository(SqlConnectionFactory connections) : IDailyClosureRepository
{
    private const string SinCambiosAction = "DAILY_CLOSURE_NO_CHANGE";
    private const string NoValorableAction = "DAILY_CLOSURE_NOT_ASSESSABLE";
    private const string CambioEnviadoAction = "DAILY_CLOSURE_CHANGE_REPORTED";

    public async Task<DailyClosureResult> RegisterAsync(RegisterDailyClosureInput input, CancellationToken ct = default)
    {
        var actionCode = ActionCodeOf(input.Type);
        var requestHash = RequestHash.Of(input, actionCode);
        using var connection = await connections.OpenAsync(ct);

        var previous = await FindIdempotencyAsync(connection, null, input.AccountId.Value, actionCode, input.OperationId, requestHash, ct);
        if (previous is not null)
        {
            return previous;
        }

        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            var occurredAt = DateTimeOffset.UtcNow;
            var closureId = Guid.NewGuid();
            var result = new DailyClosureResult(closureId, occurredAt);

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.operaciones_idempotencia (id, cuenta_id, accion_codigo, operacion_id, hash_solicitud, estado, creado_en)
                VALUES (@Id, @AccountId, @ActionCode, @OperationId, @RequestHash, 'IN_PROGRESS', @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, ActionCode = actionCode,
                input.OperationId, RequestHash = requestHash, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.cierres_cotidianos_residente
                    (id, residente_id, centro_id, unidad_id, tipo_codigo, motivo_no_valorable,
                     registrado_por_cuenta_id, registrado_por_perfil, ocurrido_en)
                VALUES (@ClosureId, @ResidentId, @CenterId, @UnitId, @TypeCode, @Reason, @AccountId, 'AUXILIAR', @OccurredAt)
                """, new
            {
                ClosureId = closureId, ResidentId = input.ResidentId.Value, CenterId = input.CenterId.Value, UnitId = input.UnitId.Value,
                TypeCode = input.Type.ToCode(), input.Reason, AccountId = input.AccountId.Value, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.eventos_auditoria
                    (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
                VALUES (@Id, @AccountId, 'AUXILIAR', @CenterId, @UnitId, @ResidentId, 'DAILY_CLOSURE', @ClosureId, @ActionCode, @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, CenterId = input.CenterId.Value, UnitId = input.UnitId.Value,
                ResidentId = input.ResidentId.Value, ClosureId = closureId, ActionCode = actionCode, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            var resultJson = JsonSerializer.Serialize(result, ResidAppJson.Options);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.operaciones_idempotencia
                   SET estado = 'SUCCEEDED', recurso_resultado_id = @ClosureId, resultado_json = @ResultJson, completado_en = @OccurredAt
                 WHERE cuenta_id = @AccountId AND accion_codigo = @ActionCode
                   AND operacion_id = @OperationId AND hash_solicitud = @RequestHash AND estado = 'IN_PROGRESS'
                """, new
            {
                ClosureId = closureId, ResultJson = resultJson, OccurredAt = occurredAt,
                AccountId = input.AccountId.Value, ActionCode = actionCode, input.OperationId, RequestHash = requestHash,
            }, transaction, cancellationToken: ct));

            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            var recovered = await FindIdempotencyAsync(connection, null, input.AccountId.Value, actionCode, input.OperationId, requestHash, ct);
            if (recovered is not null)
            {
                return recovered;
            }
            throw;
        }
    }

    /// <summary>Traduce AUX-06 a AUX-12 "Registrar cambio": mismo patrón de idempotencia que RegisterAsync,
    /// más la inserción de una fila por área observada en dbo.cierres_cotidianos_cambio_areas.</summary>
    public async Task<DailyClosureResult> RegisterChangeAsync(RegisterDailyChangeInput input, CancellationToken ct = default)
    {
        var requestHash = RequestHash.OfChange(input);
        using var connection = await connections.OpenAsync(ct);

        var previous = await FindIdempotencyAsync(connection, null, input.AccountId.Value, CambioEnviadoAction, input.OperationId, requestHash, ct);
        if (previous is not null)
        {
            return previous;
        }

        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            var occurredAt = DateTimeOffset.UtcNow;
            var closureId = Guid.NewGuid();
            var result = new DailyClosureResult(closureId, occurredAt);

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.operaciones_idempotencia (id, cuenta_id, accion_codigo, operacion_id, hash_solicitud, estado, creado_en)
                VALUES (@Id, @AccountId, @ActionCode, @OperationId, @RequestHash, 'IN_PROGRESS', @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, ActionCode = CambioEnviadoAction,
                input.OperationId, RequestHash = requestHash, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.cierres_cotidianos_residente
                    (id, residente_id, centro_id, unidad_id, tipo_codigo, motivo_no_valorable,
                     temperatura_celsius, clasificacion_codigo, motivo_prioritario_codigo, aviso_directo_documentado,
                     registrado_por_cuenta_id, registrado_por_perfil, ocurrido_en)
                VALUES (@ClosureId, @ResidentId, @CenterId, @UnitId, 'CAMBIO_ENVIADO', NULL,
                     @Temperature, @ClassificationCode, @PriorityReasonCode, @DirectNoticeNotes,
                     @AccountId, 'AUXILIAR', @OccurredAt)
                """, new
            {
                ClosureId = closureId, ResidentId = input.ResidentId.Value, CenterId = input.CenterId.Value, UnitId = input.UnitId.Value,
                Temperature = input.TemperatureCelsius, ClassificationCode = input.Classification.ToCode(),
                PriorityReasonCode = input.PriorityReason?.ToCode(), input.DirectNoticeNotes,
                AccountId = input.AccountId.Value, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            foreach (var area in input.Areas)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.cierres_cotidianos_cambio_areas (id, cierre_id, residente_id, centro_id, area_codigo, texto_libre)
                    VALUES (@Id, @ClosureId, @ResidentId, @CenterId, @AreaCode, @FreeText)
                    """, new
                {
                    Id = Guid.NewGuid(), ClosureId = closureId, ResidentId = input.ResidentId.Value, CenterId = input.CenterId.Value,
                    AreaCode = area.AreaCode.ToCode(), area.FreeText,
                }, transaction, cancellationToken: ct));
            }

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.eventos_auditoria
                    (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
                VALUES (@Id, @AccountId, 'AUXILIAR', @CenterId, @UnitId, @ResidentId, 'DAILY_CLOSURE', @ClosureId, @ActionCode, @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, CenterId = input.CenterId.Value, UnitId = input.UnitId.Value,
                ResidentId = input.ResidentId.Value, ClosureId = closureId, ActionCode = CambioEnviadoAction, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            var resultJson = JsonSerializer.Serialize(result, ResidAppJson.Options);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.operaciones_idempotencia
                   SET estado = 'SUCCEEDED', recurso_resultado_id = @ClosureId, resultado_json = @ResultJson, completado_en = @OccurredAt
                 WHERE cuenta_id = @AccountId AND accion_codigo = @ActionCode
                   AND operacion_id = @OperationId AND hash_solicitud = @RequestHash AND estado = 'IN_PROGRESS'
                """, new
            {
                ClosureId = closureId, ResultJson = resultJson, OccurredAt = occurredAt,
                AccountId = input.AccountId.Value, ActionCode = CambioEnviadoAction, input.OperationId, RequestHash = requestHash,
            }, transaction, cancellationToken: ct));

            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            var recovered = await FindIdempotencyAsync(connection, null, input.AccountId.Value, CambioEnviadoAction, input.OperationId, requestHash, ct);
            if (recovered is not null)
            {
                return recovered;
            }
            throw;
        }
    }

    private static string ActionCodeOf(DailyClosureType type) =>
        type == DailyClosureType.NoValorable ? NoValorableAction : SinCambiosAction;

    private static async Task<DailyClosureResult?> FindIdempotencyAsync(
        IDbConnection connection, IDbTransaction? transaction, Guid accountId, string actionCode, Guid operationId, string requestHash, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<IdempotencyRow>(new CommandDefinition("""
            SELECT hash_solicitud AS RequestHash, estado AS Status, resultado_json AS ResultJson
              FROM dbo.operaciones_idempotencia
             WHERE cuenta_id = @AccountId AND accion_codigo = @ActionCode AND operacion_id = @OperationId
            """, new { AccountId = accountId, ActionCode = actionCode, OperationId = operationId }, transaction, cancellationToken: ct));
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
            return JsonSerializer.Deserialize<DailyClosureResult>(row.ResultJson, ResidAppJson.Options);
        }
        throw new DomainValidationException("IDEMPOTENCY_OPERATION_IN_PROGRESS");
    }

    private sealed record IdempotencyRow(string RequestHash, string Status, string? ResultJson);
}

file static class RequestHash
{
    public static string Of(RegisterDailyClosureInput input, string actionCode)
    {
        var canonical = JsonSerializer.Serialize(new object?[]
        {
            input.AccountId.Value, actionCode, input.CenterId.Value, input.UnitId.Value,
            input.ResidentId.Value, input.Type.ToCode(), input.Reason, input.OperationId,
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static string OfChange(RegisterDailyChangeInput input)
    {
        var areas = input.Areas
            .OrderBy(a => a.AreaCode.ToCode(), StringComparer.Ordinal)
            .Select(a => new object[] { a.AreaCode.ToCode(), a.FreeText });
        var canonical = JsonSerializer.Serialize(new object?[]
        {
            input.AccountId.Value, input.CenterId.Value, input.UnitId.Value, input.ResidentId.Value,
            areas, input.TemperatureCelsius, input.Classification.ToCode(), input.PriorityReason?.ToCode(),
            input.DirectNoticeNotes, input.OperationId,
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
