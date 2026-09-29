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

/// <summary>
/// ENF-12/ENF-14 y MED-14/MED-16: firmar el informe de derivación a Urgencias y registrar los intentos de
/// llamada a la familia, común a Enfermería y Medicina (DER-01). Solo sobre el protocolo urgente activo del
/// propio perfil (UrgentProtocolRule.ProtocolState): el evento sigue en el protocolo, cada operación exige su
/// revisión y la avanza en 1, y deja su fila en dbo.eventos_auditoria con el perfil que actúa. La firma es
/// idempotente como el cierre (ClinicalEventCloser): repetirla con la misma operación devuelve el mismo
/// resultado sin firmar dos veces.
/// </summary>
internal static class ReferralWriter
{
    private const string SignActionCode = "REFERRAL_REPORT_SIGN";

    public static async Task<int> SignAsync(
        SqlConnectionFactory connections, UrgentProtocolRule rule, SignReferralReportInput input, CancellationToken ct)
    {
        var requestHash = SignRequestHash(input);
        using var connection = await connections.OpenAsync(ct);

        var previous = await ClinicalEventCloser.FindIdempotencyAsync(
            connection, SignActionCode, input.AccountId.Value, input.OperationId, requestHash, ct);
        if (previous is not null)
        {
            return previous.Revision;
        }

        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.operaciones_idempotencia (id, cuenta_id, accion_codigo, operacion_id, hash_solicitud, estado, creado_en)
                VALUES (@Id, @AccountId, @ActionCode, @OperationId, @RequestHash, 'IN_PROGRESS', @SignedAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, ActionCode = SignActionCode, input.OperationId,
                RequestHash = requestHash, input.SignedAt,
            }, transaction, cancellationToken: ct));

            var updated = await connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE dbo.eventos_asistenciales SET revision = revision + 1
                 WHERE id = @EventId AND centro_id = @CenterId AND revision = @ExpectedRevision AND estado_codigo = '{rule.ProtocolState}'
                """, new { input.EventId, CenterId = input.CenterId.Value, input.ExpectedRevision }, transaction, cancellationToken: ct));
            if (updated != 1)
            {
                throw new DomainValidationException("CLINICAL_EVENT_REVISION_CONFLICT");
            }

            var reportId = Guid.NewGuid();
            var inserted = await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.informes_derivacion
                    (id, evento_id, residente_id, centro_id, perfil_codigo, motivo, informacion_adicional, contenido_json,
                     huella_contenido, pdf, huella_pdf, firmado_por_cuenta_id, firmado_en)
                SELECT @Id, ea.id, ea.residente_id, ea.centro_id, @ProfileCode, @Reason, @AdditionalInformation, @ContentJson,
                       @ContentHash, @Pdf, @PdfHash, @AccountId, @SignedAt
                  FROM dbo.eventos_asistenciales ea
                 WHERE ea.id = @EventId
                   AND NOT EXISTS (SELECT 1 FROM dbo.informes_derivacion d WITH (FORCESEEK) WHERE d.evento_id = ea.id)
                """, new
            {
                Id = reportId, rule.ProfileCode, input.Report.Reason, input.Report.AdditionalInformation,
                ContentJson = input.Content.ToJson(), input.ContentHash, input.Pdf, input.PdfHash,
                AccountId = input.AccountId.Value, SignedAt = input.SignedAt.UtcDateTime, input.EventId,
            }, transaction, cancellationToken: ct));
            if (inserted != 1)
            {
                throw new DomainValidationException("REFERRAL_REPORT_ALREADY_SIGNED");
            }

            var revision = await ClinicalEventAudit.RecordAsync(connection, transaction, input.AccountId, rule.ProfileCode, input.CenterId,
                input.EventId, "REFERRAL_REPORT", reportId, SignActionCode, input.SignedAt, ct);

            var resultJson = JsonSerializer.Serialize(new ClinicalEventCloser.CloseResult(revision), ResidAppJson.Options);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.operaciones_idempotencia
                   SET estado = 'SUCCEEDED', recurso_resultado_id = @ReportId, resultado_json = @ResultJson, completado_en = @SignedAt
                 WHERE cuenta_id = @AccountId AND accion_codigo = @ActionCode
                   AND operacion_id = @OperationId AND hash_solicitud = @RequestHash AND estado = 'IN_PROGRESS'
                """, new
            {
                ReportId = reportId, ResultJson = resultJson, input.SignedAt,
                AccountId = input.AccountId.Value, ActionCode = SignActionCode, input.OperationId, RequestHash = requestHash,
            }, transaction, cancellationToken: ct));

            transaction.Commit();
            return revision;
        }
        catch
        {
            transaction.Rollback();
            var recovered = await ClinicalEventCloser.FindIdempotencyAsync(
                connection, SignActionCode, input.AccountId.Value, input.OperationId, requestHash, ct);
            if (recovered is not null)
            {
                return recovered.Revision;
            }
            throw;
        }
    }

    /// <summary>Sin informe firmado no hay llamada que registrar (REFERRAL_REPORT_REQUIRED).</summary>
    public static async Task<int> RecordCallAttemptAsync(
        SqlConnectionFactory connections, UrgentProtocolRule rule, RecordFamilyCallAttemptInput input, CancellationToken ct)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var occurredAt = DateTimeOffset.UtcNow;
        var attempt = input.Attempt;

        var updated = await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE dbo.eventos_asistenciales SET revision = revision + 1
             WHERE id = @EventId AND centro_id = @CenterId AND revision = @ExpectedRevision AND estado_codigo = '{rule.ProtocolState}'
            """, new { input.EventId, CenterId = input.CenterId.Value, input.ExpectedRevision }, transaction, cancellationToken: ct));
        if (updated != 1)
        {
            throw new DomainValidationException("CLINICAL_EVENT_REVISION_CONFLICT");
        }

        var attemptId = Guid.NewGuid();
        var inserted = await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.intentos_llamada_familia
                (id, informe_id, evento_id, contacto, llamado_en, resultado_codigo, nota, registrado_por_cuenta_id, registrado_en)
            SELECT @Id, d.id, d.evento_id, @Contact, @CalledAt, @ResultCode, @Note, @AccountId, @OccurredAt
              FROM dbo.informes_derivacion d WITH (FORCESEEK)
             WHERE d.evento_id = @EventId
            """, new
        {
            Id = attemptId, attempt.Contact, CalledAt = attempt.CalledAt.UtcDateTime, ResultCode = attempt.Result.ToCode(), attempt.Note,
            AccountId = input.AccountId.Value, OccurredAt = occurredAt, input.EventId,
        }, transaction, cancellationToken: ct));
        if (inserted != 1)
        {
            throw new DomainValidationException("REFERRAL_REPORT_REQUIRED");
        }

        var revision = await ClinicalEventAudit.RecordAsync(connection, transaction, input.AccountId, rule.ProfileCode, input.CenterId,
            input.EventId, "FAMILY_CALL_ATTEMPT", attemptId, "FAMILY_CALL_ATTEMPT", occurredAt, ct);
        transaction.Commit();
        return revision;
    }

    /// <summary>La firma se identifica por lo que se firma (la huella del contenido y lo que escribió el
    /// profesional), no por el PDF, que cambia en cada generación.</summary>
    private static string SignRequestHash(SignReferralReportInput input)
    {
        var canonical = JsonSerializer.Serialize(new object?[]
        {
            input.AccountId.Value, input.CenterId.Value, input.EventId, input.ExpectedRevision, input.OperationId,
            input.ContentHash, input.Report.Reason, input.Report.AdditionalInformation,
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
