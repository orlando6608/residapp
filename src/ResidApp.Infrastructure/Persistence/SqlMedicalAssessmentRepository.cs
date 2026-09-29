using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// MED-04 a MED-07: empezar y guardar la valoración médica y registrar indicaciones a Enfermería, con el mismo
/// patrón que SqlNursingAssessmentRepository: la revisión de dbo.eventos_asistenciales avanza en 1 en cada
/// operación (TR_ea_transition_guard lo garantiza también en BD), cada guardado deja una versión inmutable
/// del contenido y cada operación una fila en dbo.eventos_auditoria con el perfil MEDICINA.
/// </summary>
public sealed class SqlMedicalAssessmentRepository(SqlConnectionFactory connections) : IMedicalAssessmentRepository
{
    public async Task<int> StartAsync(StartMedicalAssessmentInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var occurredAt = DateTimeOffset.UtcNow;

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.eventos_asistenciales
               SET estado_codigo = 'EN_VALORACION_MEDICA', revision = revision + 1,
                   valoracion_medica_iniciada_por_cuenta_id = @AccountId, valoracion_medica_iniciada_en = @OccurredAt
             WHERE id = @EventId AND centro_id = @CenterId AND revision = @ExpectedRevision AND estado_codigo = 'ESCALADO_MEDICINA'
            """, new
        {
            AccountId = input.AccountId.Value, OccurredAt = occurredAt, input.EventId, CenterId = input.CenterId.Value, input.ExpectedRevision,
        }, transaction, cancellationToken: ct));
        if (updated != 1)
        {
            throw new DomainValidationException("CLINICAL_EVENT_REVISION_CONFLICT");
        }

        var revision = await ClinicalEventAudit.RecordAsync(connection, transaction, input.AccountId, "MEDICINA", input.CenterId,
            input.EventId, "CLINICAL_EVENT", input.EventId, "MEDICAL_ASSESSMENT_START", occurredAt, ct);
        transaction.Commit();
        return revision;
    }

    public async Task<int> SaveAsync(SaveMedicalAssessmentInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var occurredAt = DateTimeOffset.UtcNow;

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.eventos_asistenciales SET revision = revision + 1
             WHERE id = @EventId AND centro_id = @CenterId AND revision = @ExpectedRevision AND estado_codigo = 'EN_VALORACION_MEDICA'
            """, new { input.EventId, CenterId = input.CenterId.Value, input.ExpectedRevision }, transaction, cancellationToken: ct));
        if (updated != 1)
        {
            throw new DomainValidationException("CLINICAL_EVENT_REVISION_CONFLICT");
        }

        var content = input.Content;
        var vitals = content.Vitals;
        await connection.ExecuteAsync(new CommandDefinition("""
            IF EXISTS (SELECT 1 FROM dbo.valoraciones_medicas WITH (FORCESEEK) WHERE evento_id = @EventId)
                UPDATE v
                   SET hallazgos_exploracion = @Findings, valoracion = @Assessment, actuaciones = @Actions,
                       temperatura_celsius = @TemperatureCelsius, tension_sistolica_mmhg = @SystolicMmHg,
                       tension_diastolica_mmhg = @DiastolicMmHg, frecuencia_cardiaca_lpm = @HeartRateBpm,
                       frecuencia_respiratoria_rpm = @RespiratoryRateRpm, saturacion_o2_pct = @OxygenSaturationPct,
                       soporte_respiratorio_codigo = @RespiratorySupportCode, flujo_o2_lpm = @OxygenFlowLpm, glucemia_mg_dl = @GlucoseMgDl,
                       otra_constante_nombre = @OtherName, otra_constante_valor = @OtherValue, otra_constante_unidad = @OtherUnit,
                       actualizado_por_cuenta_id = @AccountId, actualizado_en = @OccurredAt
                  FROM dbo.valoraciones_medicas v WITH (FORCESEEK)
                 WHERE v.evento_id = @EventId AND v.estado_codigo = 'BORRADOR';
            ELSE
                INSERT INTO dbo.valoraciones_medicas
                    (id, evento_id, residente_id, centro_id, hallazgos_exploracion, valoracion, actuaciones,
                     temperatura_celsius, tension_sistolica_mmhg, tension_diastolica_mmhg, frecuencia_cardiaca_lpm,
                     frecuencia_respiratoria_rpm, saturacion_o2_pct, soporte_respiratorio_codigo, flujo_o2_lpm, glucemia_mg_dl,
                     otra_constante_nombre, otra_constante_valor, otra_constante_unidad,
                     creado_por_cuenta_id, creado_en, actualizado_por_cuenta_id, actualizado_en)
                SELECT @Id, ea.id, ea.residente_id, ea.centro_id, @Findings, @Assessment, @Actions,
                       @TemperatureCelsius, @SystolicMmHg, @DiastolicMmHg, @HeartRateBpm,
                       @RespiratoryRateRpm, @OxygenSaturationPct, @RespiratorySupportCode, @OxygenFlowLpm, @GlucoseMgDl,
                       @OtherName, @OtherValue, @OtherUnit, @AccountId, @OccurredAt, @AccountId, @OccurredAt
                  FROM dbo.eventos_asistenciales ea
                 WHERE ea.id = @EventId;
            """, new
        {
            Id = Guid.NewGuid(), input.EventId, AccountId = input.AccountId.Value, OccurredAt = occurredAt,
            Findings = content.FindingsAndExamination, content.Assessment, content.Actions,
            vitals.TemperatureCelsius, vitals.SystolicMmHg, vitals.DiastolicMmHg, vitals.HeartRateBpm, vitals.RespiratoryRateRpm,
            vitals.OxygenSaturationPct, RespiratorySupportCode = vitals.RespiratorySupport?.ToCode(), vitals.OxygenFlowLpm,
            vitals.GlucoseMgDl, vitals.OtherName, vitals.OtherValue, vitals.OtherUnit,
        }, transaction, cancellationToken: ct));

        var revision = await ClinicalEventAudit.RecordAsync(connection, transaction, input.AccountId, "MEDICINA", input.CenterId,
            input.EventId, "CLINICAL_EVENT", input.EventId, "MEDICAL_ASSESSMENT_SAVE", occurredAt, ct);

        // Histórico: el borrador se sobrescribe, pero cada guardado deja su copia inmutable.
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.valoraciones_medicas_versiones
                (id, valoracion_id, evento_id, revision_evento, hallazgos_exploracion, valoracion, actuaciones,
                 temperatura_celsius, tension_sistolica_mmhg, tension_diastolica_mmhg, frecuencia_cardiaca_lpm, frecuencia_respiratoria_rpm,
                 saturacion_o2_pct, soporte_respiratorio_codigo, flujo_o2_lpm, glucemia_mg_dl,
                 otra_constante_nombre, otra_constante_valor, otra_constante_unidad, guardado_por_cuenta_id, guardado_en)
            SELECT @Id, v.id, v.evento_id, @Revision, v.hallazgos_exploracion, v.valoracion, v.actuaciones,
                   v.temperatura_celsius, v.tension_sistolica_mmhg, v.tension_diastolica_mmhg, v.frecuencia_cardiaca_lpm, v.frecuencia_respiratoria_rpm,
                   v.saturacion_o2_pct, v.soporte_respiratorio_codigo, v.flujo_o2_lpm, v.glucemia_mg_dl,
                   v.otra_constante_nombre, v.otra_constante_valor, v.otra_constante_unidad, v.actualizado_por_cuenta_id, v.actualizado_en
              FROM dbo.valoraciones_medicas v WITH (FORCESEEK)
             WHERE v.evento_id = @EventId AND v.estado_codigo = 'BORRADOR'
            """, new { Id = Guid.NewGuid(), Revision = revision, input.EventId }, transaction, cancellationToken: ct));

        transaction.Commit();
        return revision;
    }

    /// <summary>MED-06/MED-07: la primera indicación pasa el evento de EN_VALORACION_MEDICA a
    /// CON_INDICACION_PENDIENTE; las siguientes lo dejan ahí. Desde EN_SEGUIMIENTO_MEDICO ("resolver" el
    /// seguimiento) también pasa a CON_INDICACION_PENDIENTE y el seguimiento termina. Exige la valoración
    /// médica guardada.</summary>
    public async Task<int> RegisterIndicationAsync(RegisterMedicalIndicationInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var occurredAt = DateTimeOffset.UtcNow;

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.eventos_asistenciales SET estado_codigo = 'CON_INDICACION_PENDIENTE', revision = revision + 1
             WHERE id = @EventId AND centro_id = @CenterId AND revision = @ExpectedRevision
               AND estado_codigo IN ('EN_VALORACION_MEDICA', 'CON_INDICACION_PENDIENTE', 'EN_SEGUIMIENTO_MEDICO')
            """, new { input.EventId, CenterId = input.CenterId.Value, input.ExpectedRevision }, transaction, cancellationToken: ct));
        if (updated != 1)
        {
            throw new DomainValidationException("CLINICAL_EVENT_REVISION_CONFLICT");
        }

        var indication = input.Indication;
        var indicationId = Guid.NewGuid();
        var inserted = await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.indicaciones_medicas
                (id, evento_id, residente_id, centro_id, texto, fecha_prevista, criterio, informacion_adicional, emitida_por_cuenta_id, emitida_en)
            SELECT @Id, ea.id, ea.residente_id, ea.centro_id, @Text, @DueDate, @Criterion, @AdditionalInformation, @AccountId, @OccurredAt
              FROM dbo.eventos_asistenciales ea
             WHERE ea.id = @EventId
               AND EXISTS (SELECT 1 FROM dbo.valoraciones_medicas v WITH (FORCESEEK) WHERE v.evento_id = ea.id AND v.estado_codigo = 'BORRADOR')
            """, new
        {
            Id = indicationId, indication.Text, indication.Plan.DueDate, indication.Plan.Criterion, indication.AdditionalInformation,
            AccountId = input.AccountId.Value, OccurredAt = occurredAt, input.EventId,
        }, transaction, cancellationToken: ct));
        if (inserted != 1)
        {
            throw new DomainValidationException("MEDICAL_ASSESSMENT_REQUIRED");
        }

        var revision = await ClinicalEventAudit.RecordAsync(connection, transaction, input.AccountId, "MEDICINA", input.CenterId,
            input.EventId, "MEDICAL_INDICATION", indicationId, "MEDICAL_INDICATION_ISSUE", occurredAt, ct);
        transaction.Commit();
        return revision;
    }

    /// <summary>MED-10: pasa el evento de EN_VALORACION_MEDICA o CON_INDICACION_PENDIENTE a
    /// EN_SEGUIMIENTO_MEDICO con su plan y objetivo. Un solo seguimiento médico por evento. La valoración
    /// médica sigue en borrador (se cierra al cerrar el evento), pero tiene que estar guardada.</summary>
    public async Task<int> StartFollowUpAsync(StartMedicalFollowUpInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var occurredAt = DateTimeOffset.UtcNow;

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.eventos_asistenciales SET estado_codigo = 'EN_SEGUIMIENTO_MEDICO', revision = revision + 1
             WHERE id = @EventId AND centro_id = @CenterId AND revision = @ExpectedRevision
               AND estado_codigo IN ('EN_VALORACION_MEDICA', 'CON_INDICACION_PENDIENTE')
            """, new { input.EventId, CenterId = input.CenterId.Value, input.ExpectedRevision }, transaction, cancellationToken: ct));
        if (updated != 1)
        {
            throw new DomainValidationException("CLINICAL_EVENT_REVISION_CONFLICT");
        }

        var alreadyStarted = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.seguimientos_medicos s WITH (FORCESEEK) WHERE s.evento_id = @EventId)
                        THEN 1 ELSE 0 END AS BIT)
            """, new { input.EventId }, transaction, cancellationToken: ct));
        if (alreadyStarted)
        {
            throw new DomainValidationException("MEDICAL_FOLLOW_UP_ALREADY_STARTED");
        }

        var followUpId = Guid.NewGuid();
        var inserted = await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.seguimientos_medicos
                (id, evento_id, residente_id, centro_id, fecha_prevista, criterio, objetivo, iniciado_por_cuenta_id, iniciado_en)
            SELECT @Id, ea.id, ea.residente_id, ea.centro_id, @DueDate, @Criterion, @Objective, @AccountId, @OccurredAt
              FROM dbo.eventos_asistenciales ea
             WHERE ea.id = @EventId
               AND EXISTS (SELECT 1 FROM dbo.valoraciones_medicas v WITH (FORCESEEK) WHERE v.evento_id = ea.id AND v.estado_codigo = 'BORRADOR')
            """, new
        {
            Id = followUpId, input.FollowUp.Plan.DueDate, input.FollowUp.Plan.Criterion, input.FollowUp.Objective,
            AccountId = input.AccountId.Value, OccurredAt = occurredAt, input.EventId,
        }, transaction, cancellationToken: ct));
        if (inserted != 1)
        {
            throw new DomainValidationException("MEDICAL_ASSESSMENT_REQUIRED");
        }

        var revision = await ClinicalEventAudit.RecordAsync(connection, transaction, input.AccountId, "MEDICINA", input.CenterId,
            input.EventId, "CLINICAL_EVENT", input.EventId, "MEDICAL_FOLLOW_UP_START", occurredAt, ct);
        transaction.Commit();
        return revision;
    }

    /// <summary>MED-11/MED-12: una acción sobre el seguimiento médico abierto, con su autoría. Una recepción
    /// solo se registra sobre una transferencia de este seguimiento que nadie haya confirmado todavía.</summary>
    public async Task<int> RecordFollowUpActionAsync(RecordMedicalFollowUpActionInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var occurredAt = DateTimeOffset.UtcNow;
        var action = input.Action;

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.eventos_asistenciales SET revision = revision + 1
             WHERE id = @EventId AND centro_id = @CenterId AND revision = @ExpectedRevision AND estado_codigo = 'EN_SEGUIMIENTO_MEDICO'
            """, new { input.EventId, CenterId = input.CenterId.Value, input.ExpectedRevision }, transaction, cancellationToken: ct));
        if (updated != 1)
        {
            throw new DomainValidationException("CLINICAL_EVENT_REVISION_CONFLICT");
        }

        var inserted = await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.seguimiento_medico_acciones
                (id, seguimiento_id, tipo_codigo, texto, fecha_prevista, criterio, equipo_entrante, transferencia_id,
                 registrado_por_cuenta_id, registrado_en)
            SELECT @Id, s.id, @TypeCode, @Text, @DueDate, @Criterion, @IncomingTeam, @TransferId, @AccountId, @OccurredAt
              FROM dbo.seguimientos_medicos s WITH (FORCESEEK)
             WHERE s.evento_id = @EventId
               AND (@TransferId IS NULL OR EXISTS (
                   SELECT 1 FROM dbo.seguimiento_medico_acciones t
                    WHERE t.id = @TransferId AND t.seguimiento_id = s.id AND t.tipo_codigo = 'TRANSFERENCIA'
                      AND NOT EXISTS (SELECT 1 FROM dbo.seguimiento_medico_acciones r WHERE r.transferencia_id = t.id)))
            """, new
        {
            Id = Guid.NewGuid(), TypeCode = action.Type.ToCode(), action.Text, action.Plan?.DueDate, action.Plan?.Criterion,
            action.IncomingTeam, action.TransferId, AccountId = input.AccountId.Value, OccurredAt = occurredAt, input.EventId,
        }, transaction, cancellationToken: ct));
        if (inserted != 1)
        {
            throw new DomainValidationException("FOLLOW_UP_TRANSFER_NOT_PENDING");
        }

        var actionCode = action.Type switch
        {
            FollowUpActionType.Actuacion => "MEDICAL_FOLLOW_UP_NOTE",
            FollowUpActionType.Reprogramacion => "MEDICAL_FOLLOW_UP_RESCHEDULE",
            FollowUpActionType.Transferencia => "MEDICAL_FOLLOW_UP_TRANSFER",
            FollowUpActionType.Conservacion => "MEDICAL_FOLLOW_UP_KEEP",
            _ => "MEDICAL_FOLLOW_UP_RECEIVE",
        };
        var revision = await ClinicalEventAudit.RecordAsync(connection, transaction, input.AccountId, "MEDICINA", input.CenterId,
            input.EventId, "CLINICAL_EVENT", input.EventId, actionCode, occurredAt, ct);
        transaction.Commit();
        return revision;
    }

    /// <summary>MED-15: desde EN_VALORACION_MEDICA, CON_INDICACION_PENDIENTE o EN_SEGUIMIENTO_MEDICO, cerrando la valoración médica.
    /// Las indicaciones aún pendientes no se tocan: siguen en la bandeja de Enfermería hasta resolverse.</summary>
    public Task<int> CloseAsync(CloseClinicalEventInput input, CancellationToken ct = default) =>
        ClinicalEventCloser.CloseAsync(connections, ClinicalEventCloseRule.Medicina, input, ct);

    /// <summary>MED-13: desde EN_VALORACION_MEDICA, CON_INDICACION_PENDIENTE o EN_SEGUIMIENTO_MEDICO a
    /// PROTOCOLO_URGENTE_MEDICO; las indicaciones emitidas no se tocan.</summary>
    public Task<int> ActivateUrgentProtocolAsync(ActivateUrgentProtocolInput input, CancellationToken ct = default) =>
        UrgentProtocolWriter.ActivateAsync(connections, UrgentProtocolRule.Medicina, input, ct);

    public Task<int> RecordUrgentProtocolEntryAsync(RecordUrgentProtocolEntryInput input, CancellationToken ct = default) =>
        UrgentProtocolWriter.RecordAsync(connections, UrgentProtocolRule.Medicina, input, ct);

    public Task<int> SignReferralReportAsync(SignReferralReportInput input, CancellationToken ct = default) =>
        ReferralWriter.SignAsync(connections, UrgentProtocolRule.Medicina, input, ct);

    public Task<int> RecordFamilyCallAttemptAsync(RecordFamilyCallAttemptInput input, CancellationToken ct = default) =>
        ReferralWriter.RecordCallAttemptAsync(connections, UrgentProtocolRule.Medicina, input, ct);
}

/// <summary>Fila de dbo.eventos_auditoria sobre un evento asistencial o algo que cuelga de él (valoración
/// médica, indicación), con el perfil activo que actúa. Devuelve la revisión actual del evento.</summary>
internal static class ClinicalEventAudit
{
    public static async Task<int> RecordAsync(
        SqlConnection connection, SqlTransaction transaction, AccountId accountId, string profileCode, CenterId centerId,
        Guid eventId, string resourceType, Guid resourceId, string actionCode, DateTimeOffset occurredAt, CancellationToken ct)
    {
        var current = await connection.QuerySingleAsync<EventRow>(new CommandDefinition("""
            SELECT residente_id AS ResidentId, unidad_id AS UnitId, revision AS Revision FROM dbo.eventos_asistenciales WHERE id = @EventId
            """, new { EventId = eventId }, transaction, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.eventos_auditoria
                (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (@Id, @AccountId, @ProfileCode, @CenterId, @UnitId, @ResidentId, @ResourceType, @ResourceId, @ActionCode, @OccurredAt)
            """, new
        {
            Id = Guid.NewGuid(), AccountId = accountId.Value, ProfileCode = profileCode, CenterId = centerId.Value, current.UnitId,
            current.ResidentId, ResourceType = resourceType, ResourceId = resourceId, ActionCode = actionCode, OccurredAt = occurredAt,
        }, transaction, cancellationToken: ct));
        return current.Revision;
    }

    private sealed record EventRow(Guid ResidentId, Guid UnitId, int Revision);
}
