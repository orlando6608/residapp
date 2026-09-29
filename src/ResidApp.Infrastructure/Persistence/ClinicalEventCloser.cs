using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>Quién cierra: perfil de la auditoría, estados desde los que puede cerrar y valoración (en
/// BORRADOR) que pasa a CERRADA; sin ella, <see cref="AssessmentRequiredCode"/>. Valores fijos del código,
/// nunca entrada del usuario.</summary>
internal sealed record ClinicalEventCloseRule(
    string ProfileCode, string AllowedStatesSql, string AssessmentTable, string AssessmentRequiredCode)
{
    public static readonly ClinicalEventCloseRule Enfermeria = new(
        "ENFERMERIA", "'EN_VALORACION', 'EN_SEGUIMIENTO'", "dbo.valoraciones_enfermeria", "NURSING_ASSESSMENT_REQUIRED");

    public static readonly ClinicalEventCloseRule Medicina = new(
        "MEDICINA", "'EN_VALORACION_MEDICA', 'CON_INDICACION_PENDIENTE', 'EN_SEGUIMIENTO_MEDICO'", "dbo.valoraciones_medicas", "MEDICAL_ASSESSMENT_REQUIRED");
}

/// <summary>
/// ENF-07A y MED-15: cierre del evento con la decisión de comunicación familiar, común a Enfermería y
/// Medicina. Idempotente con el mismo patrón que SqlClinicalEventRepository.RegisterAsync: hash de la
/// petición y fila IN_PROGRESS/SUCCEEDED en dbo.operaciones_idempotencia dentro de la misma transacción.
/// Un cierre distinto sobre un evento ya cerrado no encuentra un estado de origen y es un conflicto.
/// </summary>
internal static class ClinicalEventCloser
{
    private const string CloseActionCode = "CLINICAL_EVENT_CLOSE";

    public static async Task<int> CloseAsync(
        SqlConnectionFactory connections, ClinicalEventCloseRule rule, CloseClinicalEventInput input, CancellationToken ct)
    {
        var requestHash = CloseRequestHash.Of(input);
        using var connection = await connections.OpenAsync(ct);

        var previous = await FindCloseIdempotencyAsync(connection, input.AccountId.Value, input.OperationId, requestHash, ct);
        if (previous is not null)
        {
            return previous.Revision;
        }

        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            var occurredAt = DateTimeOffset.UtcNow;
            var communication = input.Communication;

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.operaciones_idempotencia (id, cuenta_id, accion_codigo, operacion_id, hash_solicitud, estado, creado_en)
                VALUES (@Id, @AccountId, @ActionCode, @OperationId, @RequestHash, 'IN_PROGRESS', @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, ActionCode = CloseActionCode, input.OperationId,
                RequestHash = requestHash, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            var updated = await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE dbo.eventos_asistenciales
                   SET estado_codigo = 'CERRADO', revision = revision + 1, cerrado_por_cuenta_id = @AccountId, cerrado_en = @OccurredAt,
                       comunicacion_familiar_codigo = @DecisionCode
                 WHERE id = @EventId AND centro_id = @CenterId AND revision = @ExpectedRevision
                   AND estado_codigo IN ({rule.AllowedStatesSql})
                """, new
            {
                AccountId = input.AccountId.Value, OccurredAt = occurredAt, DecisionCode = communication.Decision.ToCode(),
                input.EventId, CenterId = input.CenterId.Value, input.ExpectedRevision,
            }, transaction, cancellationToken: ct));
            if (updated != 1)
            {
                throw new DomainValidationException("CLINICAL_EVENT_REVISION_CONFLICT");
            }

            var closedAssessments = await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE v
                   SET estado_codigo = 'CERRADA', actualizado_por_cuenta_id = @AccountId, actualizado_en = @OccurredAt
                  FROM {rule.AssessmentTable} v WITH (FORCESEEK)
                 WHERE v.evento_id = @EventId AND v.estado_codigo = 'BORRADOR'
                """, new { AccountId = input.AccountId.Value, OccurredAt = occurredAt, input.EventId }, transaction, cancellationToken: ct));
            if (closedAssessments != 1)
            {
                throw new DomainValidationException(rule.AssessmentRequiredCode);
            }

            var revision = await ClinicalEventAudit.RecordAsync(connection, transaction, input.AccountId, rule.ProfileCode, input.CenterId,
                input.EventId, "CLINICAL_EVENT", input.EventId, CloseActionCode, occurredAt, ct);

            if (communication.Decision == FamilyCommunicationDecision.Preparar)
            {
                var communicationId = Guid.NewGuid();
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.comunicaciones_familiares
                        (id, evento_id, residente_id, centro_id, tipo_codigo, texto, preparado_por_cuenta_id, preparado_en)
                    SELECT @Id, ea.id, ea.residente_id, ea.centro_id, @TypeCode, @Text, @AccountId, @OccurredAt
                      FROM dbo.eventos_asistenciales ea
                     WHERE ea.id = @EventId;

                    INSERT INTO dbo.eventos_auditoria
                        (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
                    SELECT @AuditId, @AccountId, @ProfileCode, ea.centro_id, ea.unidad_id, ea.residente_id, 'FAMILY_COMMUNICATION', @Id,
                           'FAMILY_COMMUNICATION_PREPARE', @OccurredAt
                      FROM dbo.eventos_asistenciales ea
                     WHERE ea.id = @EventId;
                    """, new
                {
                    Id = communicationId, AuditId = Guid.NewGuid(), TypeCode = communication.Type!.Value.ToCode(), communication.Text,
                    AccountId = input.AccountId.Value, rule.ProfileCode, OccurredAt = occurredAt, input.EventId,
                }, transaction, cancellationToken: ct));
            }

            var resultJson = JsonSerializer.Serialize(new CloseResult(revision), ResidAppJson.Options);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.operaciones_idempotencia
                   SET estado = 'SUCCEEDED', recurso_resultado_id = @EventId, resultado_json = @ResultJson, completado_en = @OccurredAt
                 WHERE cuenta_id = @AccountId AND accion_codigo = @ActionCode
                   AND operacion_id = @OperationId AND hash_solicitud = @RequestHash AND estado = 'IN_PROGRESS'
                """, new
            {
                input.EventId, ResultJson = resultJson, OccurredAt = occurredAt,
                AccountId = input.AccountId.Value, ActionCode = CloseActionCode, input.OperationId, RequestHash = requestHash,
            }, transaction, cancellationToken: ct));

            transaction.Commit();
            return revision;
        }
        catch
        {
            transaction.Rollback();
            var recovered = await FindCloseIdempotencyAsync(connection, input.AccountId.Value, input.OperationId, requestHash, ct);
            if (recovered is not null)
            {
                return recovered.Revision;
            }
            throw;
        }
    }

    private static async Task<CloseResult?> FindCloseIdempotencyAsync(
        IDbConnection connection, Guid accountId, Guid operationId, string requestHash, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<IdempotencyRow>(new CommandDefinition("""
            SELECT hash_solicitud AS RequestHash, estado AS Status, resultado_json AS ResultJson
              FROM dbo.operaciones_idempotencia
             WHERE cuenta_id = @AccountId AND accion_codigo = @ActionCode AND operacion_id = @OperationId
            """, new { AccountId = accountId, ActionCode = CloseActionCode, OperationId = operationId }, cancellationToken: ct));
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
            return JsonSerializer.Deserialize<CloseResult>(row.ResultJson, ResidAppJson.Options);
        }
        throw new DomainValidationException("IDEMPOTENCY_OPERATION_IN_PROGRESS");
    }

    private sealed record CloseResult(int Revision);

    private sealed record IdempotencyRow(string RequestHash, string Status, string? ResultJson);
}

file static class CloseRequestHash
{
    public static string Of(CloseClinicalEventInput input)
    {
        var canonical = JsonSerializer.Serialize(new object?[]
        {
            input.AccountId.Value, input.CenterId.Value, input.EventId, input.ExpectedRevision, input.OperationId,
            input.Communication.Decision.ToCode(), input.Communication.Type?.ToCode(), input.Communication.Text,
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
