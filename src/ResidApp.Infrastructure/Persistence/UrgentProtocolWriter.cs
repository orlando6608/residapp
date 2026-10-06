using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>Quién activa el protocolo urgente: perfil (dbo.protocolos_urgentes y auditoría), estados desde
/// los que puede activarlo, estado del protocolo y valoración que tiene que estar guardada; sin ella,
/// <see cref="AssessmentRequiredCode"/>. Valores fijos del código, nunca entrada del usuario, igual que
/// ClinicalEventCloseRule.</summary>
internal sealed record UrgentProtocolRule(
    string ProfileCode, string FromStatesSql, string ProtocolState, string AssessmentTable, string AssessmentRequiredCode)
{
    public static readonly UrgentProtocolRule Enfermeria = new(
        "ENFERMERIA", "'EN_VALORACION', 'EN_SEGUIMIENTO'", "PROTOCOLO_URGENTE",
        "dbo.valoraciones_enfermeria", "NURSING_ASSESSMENT_REQUIRED");

    public static readonly UrgentProtocolRule Medicina = new(
        "MEDICINA", "'EN_VALORACION_MEDICA', 'CON_INDICACION_PENDIENTE', 'EN_SEGUIMIENTO_MEDICO'", "PROTOCOLO_URGENTE_MEDICO",
        "dbo.valoraciones_medicas", "MEDICAL_ASSESSMENT_REQUIRED");
}

/// <summary>
/// ENF-11 y MED-13: activar el protocolo urgente y registrar dentro de él, común a Enfermería y Medicina (el
/// módulo es el mismo para los dos perfiles, DER-01). Mismo patrón que el seguimiento: cada operación exige
/// la revisión del evento y la avanza en 1 (CLINICAL_EVENT_REVISION_CONFLICT si otro lo cambió), y deja su
/// fila en dbo.eventos_auditoria con el perfil que actúa.
/// </summary>
internal static class UrgentProtocolWriter
{
    public static async Task<int> ActivateAsync(
        SqlConnectionFactory connections, UrgentProtocolRule rule, ActivateUrgentProtocolInput input, CancellationToken ct)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var occurredAt = DateTimeOffset.UtcNow;

        var updated = await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE dbo.eventos_asistenciales SET estado_codigo = '{rule.ProtocolState}', revision = revision + 1
             WHERE id = @EventId AND centro_id = @CenterId AND revision = @ExpectedRevision
               AND estado_codigo IN ({rule.FromStatesSql})
            """, new { input.EventId, CenterId = input.CenterId.Value, input.ExpectedRevision }, transaction, cancellationToken: ct));
        if (updated != 1)
        {
            throw new DomainValidationException("CLINICAL_EVENT_REVISION_CONFLICT");
        }

        var protocolId = Guid.NewGuid();
        var inserted = await connection.ExecuteAsync(new CommandDefinition($"""
            INSERT INTO dbo.protocolos_urgentes
                (id, evento_id, residente_id, centro_id, perfil_codigo, nota_activacion, activado_por_cuenta_id, activado_en)
            SELECT @Id, ea.id, ea.residente_id, ea.centro_id, @ProfileCode, @Note, @AccountId, @OccurredAt
              FROM dbo.eventos_asistenciales ea
             WHERE ea.id = @EventId
               AND EXISTS (SELECT 1 FROM {rule.AssessmentTable} v WITH (FORCESEEK) WHERE v.evento_id = ea.id AND v.estado_codigo = 'BORRADOR')
            """, new
        {
            Id = protocolId, rule.ProfileCode, input.Activation.Note, AccountId = input.AccountId.Value, OccurredAt = occurredAt,
            input.EventId,
        }, transaction, cancellationToken: ct));
        if (inserted != 1)
        {
            throw new DomainValidationException(rule.AssessmentRequiredCode);
        }

        var revision = await ClinicalEventAudit.RecordAsync(connection, transaction, input.AccountId, rule.ProfileCode, input.CenterId,
            input.EventId, "CLINICAL_EVENT", input.EventId, "URGENT_PROTOCOL_ACTIVATE", occurredAt, ct);
        transaction.Commit();
        return revision;
    }

    /// <summary>Solo sobre el protocolo activo del propio perfil: el estado del evento lo garantiza.</summary>
    public static async Task<int> RecordAsync(
        SqlConnectionFactory connections, UrgentProtocolRule rule, RecordUrgentProtocolEntryInput input, CancellationToken ct)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var occurredAt = DateTimeOffset.UtcNow;
        var entry = input.Entry;

        var updated = await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE dbo.eventos_asistenciales SET revision = revision + 1
             WHERE id = @EventId AND centro_id = @CenterId AND revision = @ExpectedRevision AND estado_codigo = '{rule.ProtocolState}'
            """, new { input.EventId, CenterId = input.CenterId.Value, input.ExpectedRevision }, transaction, cancellationToken: ct));
        if (updated != 1)
        {
            throw new DomainValidationException("CLINICAL_EVENT_REVISION_CONFLICT");
        }

        var entryId = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.protocolo_urgente_registros
                (id, centro_id, protocolo_id, tipo_codigo, texto, servicio_contactado, contactado_en, registrado_por_cuenta_id, registrado_en)
            SELECT @Id, p.centro_id, p.id, @TypeCode, @Text, @Service, @ContactedAt, @AccountId, @OccurredAt
              FROM dbo.protocolos_urgentes p WITH (FORCESEEK)
             WHERE p.evento_id = @EventId
            """, new
        {
            Id = entryId, TypeCode = entry.Type.ToCode(), entry.Text, entry.Service, ContactedAt = entry.ContactedAt?.UtcDateTime,
            AccountId = input.AccountId.Value, OccurredAt = occurredAt, input.EventId,
        }, transaction, cancellationToken: ct));

        var actionCode = entry.Type switch
        {
            UrgentProtocolEntryType.Actuacion => "URGENT_PROTOCOL_ACTION",
            UrgentProtocolEntryType.Evolucion => "URGENT_PROTOCOL_EVOLUTION",
            _ => "URGENT_PROTOCOL_CONTACT",
        };
        var revision = await ClinicalEventAudit.RecordAsync(connection, transaction, input.AccountId, rule.ProfileCode, input.CenterId,
            input.EventId, "CLINICAL_EVENT", input.EventId, actionCode, occurredAt, ct);
        transaction.Commit();
        return revision;
    }
}
