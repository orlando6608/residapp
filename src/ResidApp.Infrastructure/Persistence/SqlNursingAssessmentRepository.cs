using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// ENF-03 a ENF-05: empezar y guardar la valoración de Enfermería. La concurrencia optimista vive en la
/// revisión de dbo.eventos_asistenciales: cada operación la avanza en 1 solo si coincide con la esperada
/// (TR_ea_transition_guard lo garantiza también en base de datos). Cada operación deja una fila en
/// dbo.eventos_auditoria, que conserva quién tocó la valoración y cuándo aunque el borrador se sobrescriba.
/// </summary>
public sealed class SqlNursingAssessmentRepository(SqlConnectionFactory connections) : INursingAssessmentRepository
{
    public async Task<int> StartAsync(StartNursingAssessmentInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var occurredAt = DateTimeOffset.UtcNow;

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.eventos_asistenciales
               SET estado_codigo = 'EN_VALORACION', revision = revision + 1,
                   valoracion_iniciada_por_cuenta_id = @AccountId, valoracion_iniciada_en = @OccurredAt
             WHERE id = @EventId AND centro_id = @CenterId AND revision = @ExpectedRevision AND estado_codigo = 'PENDIENTE'
            """, new
        {
            AccountId = input.AccountId.Value, OccurredAt = occurredAt, input.EventId, CenterId = input.CenterId.Value, input.ExpectedRevision,
        }, transaction, cancellationToken: ct));
        if (updated != 1)
        {
            throw new DomainValidationException("CLINICAL_EVENT_REVISION_CONFLICT");
        }

        var revision = await AuditAsync(connection, transaction, input.AccountId, input.CenterId, input.EventId,
            "NURSING_ASSESSMENT_START", occurredAt, ct);
        transaction.Commit();
        return revision;
    }

    public async Task<int> SaveAsync(SaveNursingAssessmentInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var occurredAt = DateTimeOffset.UtcNow;

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.eventos_asistenciales SET revision = revision + 1
             WHERE id = @EventId AND centro_id = @CenterId AND revision = @ExpectedRevision AND estado_codigo = 'EN_VALORACION'
            """, new { input.EventId, CenterId = input.CenterId.Value, input.ExpectedRevision }, transaction, cancellationToken: ct));
        if (updated != 1)
        {
            throw new DomainValidationException("CLINICAL_EVENT_REVISION_CONFLICT");
        }

        var content = input.Content;
        var vitals = content.Vitals;
        await connection.ExecuteAsync(new CommandDefinition("""
            IF EXISTS (SELECT 1 FROM dbo.valoraciones_enfermeria WHERE evento_id = @EventId AND estado_codigo = 'BORRADOR')
                UPDATE dbo.valoraciones_enfermeria
                   SET hallazgos = @Findings, valoracion = @Assessment, actuaciones = @Actions, comunicaciones = @Communications,
                       resultado = @Outcome, temperatura_celsius = @TemperatureCelsius, tension_sistolica_mmhg = @SystolicMmHg,
                       tension_diastolica_mmhg = @DiastolicMmHg, frecuencia_cardiaca_lpm = @HeartRateBpm,
                       frecuencia_respiratoria_rpm = @RespiratoryRateRpm, saturacion_o2_pct = @OxygenSaturationPct,
                       soporte_respiratorio_codigo = @RespiratorySupportCode, flujo_o2_lpm = @OxygenFlowLpm, glucemia_mg_dl = @GlucoseMgDl,
                       otra_constante_nombre = @OtherName, otra_constante_valor = @OtherValue, otra_constante_unidad = @OtherUnit,
                       actualizado_por_cuenta_id = @AccountId, actualizado_en = @OccurredAt
                 WHERE evento_id = @EventId AND estado_codigo = 'BORRADOR';
            ELSE
                INSERT INTO dbo.valoraciones_enfermeria
                    (id, evento_id, residente_id, centro_id, hallazgos, valoracion, actuaciones, comunicaciones, resultado,
                     temperatura_celsius, tension_sistolica_mmhg, tension_diastolica_mmhg, frecuencia_cardiaca_lpm,
                     frecuencia_respiratoria_rpm, saturacion_o2_pct, soporte_respiratorio_codigo, flujo_o2_lpm, glucemia_mg_dl,
                     otra_constante_nombre, otra_constante_valor, otra_constante_unidad,
                     creado_por_cuenta_id, creado_en, actualizado_por_cuenta_id, actualizado_en)
                SELECT @Id, ea.id, ea.residente_id, ea.centro_id, @Findings, @Assessment, @Actions, @Communications, @Outcome,
                       @TemperatureCelsius, @SystolicMmHg, @DiastolicMmHg, @HeartRateBpm,
                       @RespiratoryRateRpm, @OxygenSaturationPct, @RespiratorySupportCode, @OxygenFlowLpm, @GlucoseMgDl,
                       @OtherName, @OtherValue, @OtherUnit, @AccountId, @OccurredAt, @AccountId, @OccurredAt
                  FROM dbo.eventos_asistenciales ea
                 WHERE ea.id = @EventId;
            """, new
        {
            Id = Guid.NewGuid(), input.EventId, AccountId = input.AccountId.Value, OccurredAt = occurredAt,
            content.Findings, content.Assessment, content.Actions, content.Communications, content.Outcome,
            vitals.TemperatureCelsius, vitals.SystolicMmHg, vitals.DiastolicMmHg, vitals.HeartRateBpm, vitals.RespiratoryRateRpm,
            vitals.OxygenSaturationPct, RespiratorySupportCode = vitals.RespiratorySupport?.ToCode(), vitals.OxygenFlowLpm,
            vitals.GlucoseMgDl, vitals.OtherName, vitals.OtherValue, vitals.OtherUnit,
        }, transaction, cancellationToken: ct));

        var revision = await AuditAsync(connection, transaction, input.AccountId, input.CenterId, input.EventId,
            "NURSING_ASSESSMENT_SAVE", occurredAt, ct);
        transaction.Commit();
        return revision;
    }

    /// <summary>Registra la acción en dbo.eventos_auditoria y devuelve la revisión ya avanzada del evento.</summary>
    private static async Task<int> AuditAsync(
        SqlConnection connection, SqlTransaction transaction, AccountId accountId, CenterId centerId, Guid eventId,
        string actionCode, DateTimeOffset occurredAt, CancellationToken ct)
    {
        var current = await connection.QuerySingleAsync<EventRow>(new CommandDefinition("""
            SELECT residente_id AS ResidentId, unidad_id AS UnitId, revision AS Revision FROM dbo.eventos_asistenciales WHERE id = @EventId
            """, new { EventId = eventId }, transaction, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.eventos_auditoria
                (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (@Id, @AccountId, 'ENFERMERIA', @CenterId, @UnitId, @ResidentId, 'CLINICAL_EVENT', @EventId, @ActionCode, @OccurredAt)
            """, new
        {
            Id = Guid.NewGuid(), AccountId = accountId.Value, CenterId = centerId.Value, current.UnitId, current.ResidentId,
            EventId = eventId, ActionCode = actionCode, OccurredAt = occurredAt,
        }, transaction, cancellationToken: ct));
        return current.Revision;
    }

    private sealed record EventRow(Guid ResidentId, Guid UnitId, int Revision);
}
