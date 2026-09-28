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
/// ENF-03 a ENF-05: empezar y guardar la valoración de Enfermería; ENF-06/ENF-07A: cerrar el evento. La
/// concurrencia optimista vive en la revisión de dbo.eventos_asistenciales: cada operación la avanza en 1
/// solo si coincide con la esperada (TR_ea_transition_guard lo garantiza también en base de datos). Cada
/// operación deja una fila en dbo.eventos_auditoria, y cada guardado una versión inmutable del contenido
/// en dbo.valoraciones_enfermeria_versiones.
/// </summary>
public sealed class SqlNursingAssessmentRepository(SqlConnectionFactory connections) : INursingAssessmentRepository
{
    private const string CloseActionCode = "CLINICAL_EVENT_CLOSE";

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

        // Histórico: el borrador se sobrescribe, pero cada guardado deja su copia inmutable.
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.valoraciones_enfermeria_versiones
                (id, valoracion_id, evento_id, revision_evento, hallazgos, valoracion, actuaciones, comunicaciones, resultado,
                 temperatura_celsius, tension_sistolica_mmhg, tension_diastolica_mmhg, frecuencia_cardiaca_lpm, frecuencia_respiratoria_rpm,
                 saturacion_o2_pct, soporte_respiratorio_codigo, flujo_o2_lpm, glucemia_mg_dl,
                 otra_constante_nombre, otra_constante_valor, otra_constante_unidad, guardado_por_cuenta_id, guardado_en)
            SELECT @Id, v.id, v.evento_id, @Revision, v.hallazgos, v.valoracion, v.actuaciones, v.comunicaciones, v.resultado,
                   v.temperatura_celsius, v.tension_sistolica_mmhg, v.tension_diastolica_mmhg, v.frecuencia_cardiaca_lpm, v.frecuencia_respiratoria_rpm,
                   v.saturacion_o2_pct, v.soporte_respiratorio_codigo, v.flujo_o2_lpm, v.glucemia_mg_dl,
                   v.otra_constante_nombre, v.otra_constante_valor, v.otra_constante_unidad, v.actualizado_por_cuenta_id, v.actualizado_en
              FROM dbo.valoraciones_enfermeria v
             WHERE v.evento_id = @EventId AND v.estado_codigo = 'BORRADOR'
            """, new { Id = Guid.NewGuid(), Revision = revision, input.EventId }, transaction, cancellationToken: ct));

        transaction.Commit();
        return revision;
    }

    /// <summary>ENF-07B: pasa el evento de EN_VALORACION a EN_SEGUIMIENTO con su plan. La valoración sigue en
    /// borrador (se cierra al cerrar el evento), pero tiene que estar guardada.</summary>
    public async Task<int> StartFollowUpAsync(StartFollowUpInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var occurredAt = DateTimeOffset.UtcNow;

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.eventos_asistenciales SET estado_codigo = 'EN_SEGUIMIENTO', revision = revision + 1
             WHERE id = @EventId AND centro_id = @CenterId AND revision = @ExpectedRevision AND estado_codigo = 'EN_VALORACION'
            """, new { input.EventId, CenterId = input.CenterId.Value, input.ExpectedRevision }, transaction, cancellationToken: ct));
        if (updated != 1)
        {
            throw new DomainValidationException("CLINICAL_EVENT_REVISION_CONFLICT");
        }

        var inserted = await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.seguimientos
                (id, evento_id, residente_id, centro_id, fecha_prevista, criterio, indicaciones_continuidad, iniciado_por_cuenta_id, iniciado_en)
            SELECT @Id, ea.id, ea.residente_id, ea.centro_id, @DueDate, @Criterion, @ContinuityNotes, @AccountId, @OccurredAt
              FROM dbo.eventos_asistenciales ea
             WHERE ea.id = @EventId
               AND EXISTS (SELECT 1 FROM dbo.valoraciones_enfermeria v WHERE v.evento_id = ea.id AND v.estado_codigo = 'BORRADOR')
            """, new
        {
            Id = Guid.NewGuid(), input.Plan.DueDate, input.Plan.Criterion, input.ContinuityNotes,
            AccountId = input.AccountId.Value, OccurredAt = occurredAt, input.EventId,
        }, transaction, cancellationToken: ct));
        if (inserted != 1)
        {
            throw new DomainValidationException("NURSING_ASSESSMENT_REQUIRED");
        }

        var revision = await AuditAsync(connection, transaction, input.AccountId, input.CenterId, input.EventId,
            "FOLLOW_UP_START", occurredAt, ct);
        transaction.Commit();
        return revision;
    }

    /// <summary>ENF-09/ENF-10: pasa el evento a ESCALADO_MEDICINA con el motivo de Enfermería y cierra su
    /// valoración, que Medicina recibe tal como se escaló. Escalar no cierra el evento.</summary>
    public async Task<int> EscalateAsync(EscalateClinicalEventInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var occurredAt = DateTimeOffset.UtcNow;

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.eventos_asistenciales SET estado_codigo = 'ESCALADO_MEDICINA', revision = revision + 1
             WHERE id = @EventId AND centro_id = @CenterId AND revision = @ExpectedRevision
               AND estado_codigo IN ('EN_VALORACION', 'EN_SEGUIMIENTO')
            """, new { input.EventId, CenterId = input.CenterId.Value, input.ExpectedRevision }, transaction, cancellationToken: ct));
        if (updated != 1)
        {
            throw new DomainValidationException("CLINICAL_EVENT_REVISION_CONFLICT");
        }

        var closedAssessments = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.valoraciones_enfermeria
               SET estado_codigo = 'CERRADA', actualizado_por_cuenta_id = @AccountId, actualizado_en = @OccurredAt
             WHERE evento_id = @EventId AND estado_codigo = 'BORRADOR'
            """, new { AccountId = input.AccountId.Value, OccurredAt = occurredAt, input.EventId }, transaction, cancellationToken: ct));
        if (closedAssessments != 1)
        {
            throw new DomainValidationException("NURSING_ASSESSMENT_REQUIRED");
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.escalados_medicina (id, evento_id, residente_id, centro_id, motivo, escalado_por_cuenta_id, escalado_en)
            SELECT @Id, ea.id, ea.residente_id, ea.centro_id, @Reason, @AccountId, @OccurredAt
              FROM dbo.eventos_asistenciales ea
             WHERE ea.id = @EventId
            """, new
        {
            Id = Guid.NewGuid(), Reason = input.Reason.Text, AccountId = input.AccountId.Value, OccurredAt = occurredAt, input.EventId,
        }, transaction, cancellationToken: ct));

        var revision = await AuditAsync(connection, transaction, input.AccountId, input.CenterId, input.EventId,
            "CLINICAL_EVENT_ESCALATE", occurredAt, ct);
        transaction.Commit();
        return revision;
    }

    /// <summary>ENF-08/ENF-09: una acción sobre el seguimiento abierto, con su autoría. Una recepción solo
    /// se registra sobre una transferencia de este seguimiento que nadie haya confirmado todavía.</summary>
    public async Task<int> RecordFollowUpActionAsync(RecordFollowUpActionInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var occurredAt = DateTimeOffset.UtcNow;
        var action = input.Action;

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.eventos_asistenciales SET revision = revision + 1
             WHERE id = @EventId AND centro_id = @CenterId AND revision = @ExpectedRevision AND estado_codigo = 'EN_SEGUIMIENTO'
            """, new { input.EventId, CenterId = input.CenterId.Value, input.ExpectedRevision }, transaction, cancellationToken: ct));
        if (updated != 1)
        {
            throw new DomainValidationException("CLINICAL_EVENT_REVISION_CONFLICT");
        }

        var inserted = await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.seguimiento_acciones
                (id, seguimiento_id, tipo_codigo, texto, fecha_prevista, criterio, equipo_entrante, transferencia_id,
                 registrado_por_cuenta_id, registrado_en)
            SELECT @Id, s.id, @TypeCode, @Text, @DueDate, @Criterion, @IncomingTeam, @TransferId, @AccountId, @OccurredAt
              FROM dbo.seguimientos s
             WHERE s.evento_id = @EventId
               AND (@TransferId IS NULL OR EXISTS (
                   SELECT 1 FROM dbo.seguimiento_acciones t
                    WHERE t.id = @TransferId AND t.seguimiento_id = s.id AND t.tipo_codigo = 'TRANSFERENCIA'
                      AND NOT EXISTS (SELECT 1 FROM dbo.seguimiento_acciones r WHERE r.transferencia_id = t.id)))
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
            FollowUpActionType.Actuacion => "FOLLOW_UP_NOTE",
            FollowUpActionType.Reprogramacion => "FOLLOW_UP_RESCHEDULE",
            FollowUpActionType.Transferencia => "FOLLOW_UP_TRANSFER",
            _ => "FOLLOW_UP_RECEIVE",
        };
        var revision = await AuditAsync(connection, transaction, input.AccountId, input.CenterId, input.EventId, actionCode, occurredAt, ct);
        transaction.Commit();
        return revision;
    }

    /// <summary>Idempotente con el mismo patrón que SqlClinicalEventRepository.RegisterAsync: hash de la
    /// petición y fila IN_PROGRESS/SUCCEEDED en dbo.operaciones_idempotencia dentro de la misma transacción.
    /// Un cierre distinto sobre un evento ya cerrado no encuentra EN_VALORACION y es un conflicto.</summary>
    public async Task<int> CloseAsync(CloseClinicalEventInput input, CancellationToken ct = default)
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

            var updated = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.eventos_asistenciales
                   SET estado_codigo = 'CERRADO', revision = revision + 1, cerrado_por_cuenta_id = @AccountId, cerrado_en = @OccurredAt,
                       comunicacion_familiar_codigo = @DecisionCode
                 WHERE id = @EventId AND centro_id = @CenterId AND revision = @ExpectedRevision
                   AND estado_codigo IN ('EN_VALORACION', 'EN_SEGUIMIENTO')
                """, new
            {
                AccountId = input.AccountId.Value, OccurredAt = occurredAt, DecisionCode = communication.Decision.ToCode(),
                input.EventId, CenterId = input.CenterId.Value, input.ExpectedRevision,
            }, transaction, cancellationToken: ct));
            if (updated != 1)
            {
                throw new DomainValidationException("CLINICAL_EVENT_REVISION_CONFLICT");
            }

            var closedAssessments = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.valoraciones_enfermeria
                   SET estado_codigo = 'CERRADA', actualizado_por_cuenta_id = @AccountId, actualizado_en = @OccurredAt
                 WHERE evento_id = @EventId AND estado_codigo = 'BORRADOR'
                """, new { AccountId = input.AccountId.Value, OccurredAt = occurredAt, input.EventId }, transaction, cancellationToken: ct));
            if (closedAssessments != 1)
            {
                throw new DomainValidationException("NURSING_ASSESSMENT_REQUIRED");
            }

            var revision = await AuditAsync(connection, transaction, input.AccountId, input.CenterId, input.EventId,
                CloseActionCode, occurredAt, ct);

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
                    SELECT @AuditId, @AccountId, 'ENFERMERIA', ea.centro_id, ea.unidad_id, ea.residente_id, 'FAMILY_COMMUNICATION', @Id,
                           'FAMILY_COMMUNICATION_PREPARE', @OccurredAt
                      FROM dbo.eventos_asistenciales ea
                     WHERE ea.id = @EventId;
                    """, new
                {
                    Id = communicationId, AuditId = Guid.NewGuid(), TypeCode = communication.Type!.Value.ToCode(), communication.Text,
                    AccountId = input.AccountId.Value, OccurredAt = occurredAt, input.EventId,
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
