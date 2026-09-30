using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// COR-01/COR-02 (script 0020). Cada operación bloquea la valoración (UPDLOCK) y, en la misma transacción,
/// comprueba que ya no se puede guardar de forma normal, que la cuenta es la autora de la última versión
/// ordinaria y la ventana. Además:
/// <list type="bullet">
/// <item>Una corrección queda en su tabla de correcciones y actualiza la valoración vigente con el mismo
/// contenido, autor y hora, que es lo que TR_ve_guard/TR_vm_guard exigen para cambiar una valoración CERRADA.</item>
/// <item>Una rectificación solo se añade.</item>
/// </list>
/// Ninguna toca la revisión del evento. Las dos dejan una fila en dbo.eventos_auditoria.
/// </summary>
public sealed class SqlAssessmentCorrectionRepository(SqlConnectionFactory connections) : IAssessmentCorrectionRepository
{
    private sealed record Rule(string ProfileCode, string EditingStatus, string AssessmentTable, string VersionsTable, string CorrectionsTable);

    private static readonly Rule Nursing = new(
        "ENFERMERIA", "EN_VALORACION", "dbo.valoraciones_enfermeria", "dbo.valoraciones_enfermeria_versiones",
        "dbo.valoraciones_enfermeria_correcciones");

    private static readonly Rule Medical = new(
        "MEDICINA", "EN_VALORACION_MEDICA", "dbo.valoraciones_medicas", "dbo.valoraciones_medicas_versiones",
        "dbo.valoraciones_medicas_correcciones");

    public async Task CorrectNursingAsync(CorrectNursingAssessmentInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var occurredAt = DateTimeOffset.UtcNow;
        var state = await LockAsync(connection, transaction, Nursing, input.CenterId, input.EventId, ct);
        Require(state, input.AccountId, occurredAt, input.Window, correction: true);
        if (state.Corrections != input.ExpectedCorrections)
        {
            throw new DomainValidationException("ASSESSMENT_CORRECTION_CONFLICT");
        }

        var content = input.Content;
        var vitals = content.Vitals;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.valoraciones_enfermeria_correcciones
                (id, valoracion_id, evento_id, hallazgos, valoracion, actuaciones, comunicaciones, resultado,
                 temperatura_celsius, tension_sistolica_mmhg, tension_diastolica_mmhg, frecuencia_cardiaca_lpm, frecuencia_respiratoria_rpm,
                 saturacion_o2_pct, soporte_respiratorio_codigo, flujo_o2_lpm, glucemia_mg_dl,
                 otra_constante_nombre, otra_constante_valor, otra_constante_unidad, motivo, corregido_por_cuenta_id, corregido_en)
            VALUES (@Id, @AssessmentId, @EventId, @Findings, @Assessment, @Actions, @Communications, @Outcome,
                    @TemperatureCelsius, @SystolicMmHg, @DiastolicMmHg, @HeartRateBpm, @RespiratoryRateRpm,
                    @OxygenSaturationPct, @RespiratorySupportCode, @OxygenFlowLpm, @GlucoseMgDl,
                    @OtherName, @OtherValue, @OtherUnit, @Reason, @AccountId, @OccurredAt);

            UPDATE dbo.valoraciones_enfermeria
               SET hallazgos = @Findings, valoracion = @Assessment, actuaciones = @Actions, comunicaciones = @Communications,
                   resultado = @Outcome, temperatura_celsius = @TemperatureCelsius, tension_sistolica_mmhg = @SystolicMmHg,
                   tension_diastolica_mmhg = @DiastolicMmHg, frecuencia_cardiaca_lpm = @HeartRateBpm,
                   frecuencia_respiratoria_rpm = @RespiratoryRateRpm, saturacion_o2_pct = @OxygenSaturationPct,
                   soporte_respiratorio_codigo = @RespiratorySupportCode, flujo_o2_lpm = @OxygenFlowLpm, glucemia_mg_dl = @GlucoseMgDl,
                   otra_constante_nombre = @OtherName, otra_constante_valor = @OtherValue, otra_constante_unidad = @OtherUnit,
                   actualizado_por_cuenta_id = @AccountId, actualizado_en = @OccurredAt
             WHERE id = @AssessmentId;
            """, new
        {
            Id = Guid.NewGuid(), state.AssessmentId, input.EventId,
            content.Findings, content.Assessment, content.Actions, content.Communications, content.Outcome,
            vitals.TemperatureCelsius, vitals.SystolicMmHg, vitals.DiastolicMmHg, vitals.HeartRateBpm, vitals.RespiratoryRateRpm,
            vitals.OxygenSaturationPct, RespiratorySupportCode = vitals.RespiratorySupport?.ToCode(), vitals.OxygenFlowLpm,
            vitals.GlucoseMgDl, vitals.OtherName, vitals.OtherValue, vitals.OtherUnit,
            Reason = input.Reason.Text, AccountId = input.AccountId.Value, OccurredAt = occurredAt,
        }, transaction, cancellationToken: ct));

        await ClinicalEventAudit.RecordAsync(connection, transaction, input.AccountId, Nursing.ProfileCode, input.CenterId,
            input.EventId, "CLINICAL_EVENT", input.EventId, "NURSING_ASSESSMENT_CORRECT", occurredAt, ct);
        transaction.Commit();
    }

    public async Task CorrectMedicalAsync(CorrectMedicalAssessmentInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var occurredAt = DateTimeOffset.UtcNow;
        var state = await LockAsync(connection, transaction, Medical, input.CenterId, input.EventId, ct);
        Require(state, input.AccountId, occurredAt, input.Window, correction: true);
        if (state.Corrections != input.ExpectedCorrections)
        {
            throw new DomainValidationException("ASSESSMENT_CORRECTION_CONFLICT");
        }

        var content = input.Content;
        var vitals = content.Vitals;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.valoraciones_medicas_correcciones
                (id, valoracion_id, evento_id, hallazgos_exploracion, valoracion, actuaciones,
                 temperatura_celsius, tension_sistolica_mmhg, tension_diastolica_mmhg, frecuencia_cardiaca_lpm, frecuencia_respiratoria_rpm,
                 saturacion_o2_pct, soporte_respiratorio_codigo, flujo_o2_lpm, glucemia_mg_dl,
                 otra_constante_nombre, otra_constante_valor, otra_constante_unidad, motivo, corregido_por_cuenta_id, corregido_en)
            VALUES (@Id, @AssessmentId, @EventId, @Findings, @Assessment, @Actions,
                    @TemperatureCelsius, @SystolicMmHg, @DiastolicMmHg, @HeartRateBpm, @RespiratoryRateRpm,
                    @OxygenSaturationPct, @RespiratorySupportCode, @OxygenFlowLpm, @GlucoseMgDl,
                    @OtherName, @OtherValue, @OtherUnit, @Reason, @AccountId, @OccurredAt);

            UPDATE dbo.valoraciones_medicas
               SET hallazgos_exploracion = @Findings, valoracion = @Assessment, actuaciones = @Actions,
                   temperatura_celsius = @TemperatureCelsius, tension_sistolica_mmhg = @SystolicMmHg,
                   tension_diastolica_mmhg = @DiastolicMmHg, frecuencia_cardiaca_lpm = @HeartRateBpm,
                   frecuencia_respiratoria_rpm = @RespiratoryRateRpm, saturacion_o2_pct = @OxygenSaturationPct,
                   soporte_respiratorio_codigo = @RespiratorySupportCode, flujo_o2_lpm = @OxygenFlowLpm, glucemia_mg_dl = @GlucoseMgDl,
                   otra_constante_nombre = @OtherName, otra_constante_valor = @OtherValue, otra_constante_unidad = @OtherUnit,
                   actualizado_por_cuenta_id = @AccountId, actualizado_en = @OccurredAt
             WHERE id = @AssessmentId;
            """, new
        {
            Id = Guid.NewGuid(), state.AssessmentId, input.EventId,
            Findings = content.FindingsAndExamination, content.Assessment, content.Actions,
            vitals.TemperatureCelsius, vitals.SystolicMmHg, vitals.DiastolicMmHg, vitals.HeartRateBpm, vitals.RespiratoryRateRpm,
            vitals.OxygenSaturationPct, RespiratorySupportCode = vitals.RespiratorySupport?.ToCode(), vitals.OxygenFlowLpm,
            vitals.GlucoseMgDl, vitals.OtherName, vitals.OtherValue, vitals.OtherUnit,
            Reason = input.Reason.Text, AccountId = input.AccountId.Value, OccurredAt = occurredAt,
        }, transaction, cancellationToken: ct));

        await ClinicalEventAudit.RecordAsync(connection, transaction, input.AccountId, Medical.ProfileCode, input.CenterId,
            input.EventId, "CLINICAL_EVENT", input.EventId, "MEDICAL_ASSESSMENT_CORRECT", occurredAt, ct);
        transaction.Commit();
    }

    public async Task RectifyAsync(RectifyAssessmentInput input, CancellationToken ct = default)
    {
        var rule = input.Profile switch
        {
            SystemProfile.Enfermeria => Nursing,
            SystemProfile.Medicina => Medical,
            _ => throw new AccessDeniedException(),
        };
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var occurredAt = DateTimeOffset.UtcNow;
        var state = await LockAsync(connection, transaction, rule, input.CenterId, input.EventId, ct);
        Require(state, input.AccountId, occurredAt, input.Window, correction: false);
        if (state.Rectifications != input.ExpectedRectifications)
        {
            throw new DomainValidationException("ASSESSMENT_CORRECTION_CONFLICT");
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.valoraciones_rectificaciones
                (id, evento_id, residente_id, centro_id, perfil_codigo, texto, motivo, registrado_por_cuenta_id, registrado_en)
            SELECT @Id, ea.id, ea.residente_id, ea.centro_id, @ProfileCode, @Text, @Reason, @AccountId, @OccurredAt
              FROM dbo.eventos_asistenciales ea
             WHERE ea.id = @EventId
            """, new
        {
            Id = Guid.NewGuid(), input.EventId, rule.ProfileCode, input.Rectification.Text, Reason = input.Rectification.Reason.Text,
            AccountId = input.AccountId.Value, OccurredAt = occurredAt,
        }, transaction, cancellationToken: ct));

        await ClinicalEventAudit.RecordAsync(connection, transaction, input.AccountId, rule.ProfileCode, input.CenterId,
            input.EventId, "CLINICAL_EVENT", input.EventId, "ASSESSMENT_RECTIFY", occurredAt, ct);
        transaction.Commit();
    }

    private static async Task<AssessmentState> LockAsync(
        SqlConnection connection, SqlTransaction transaction, Rule rule, CenterId centerId, Guid eventId, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<AssessmentRow>(new CommandDefinition($"""
            SELECT v.id AS AssessmentId, ea.estado_codigo AS EventStatus,
                   last.guardado_por_cuenta_id AS AuthorAccountId, last.guardado_en AS LastSavedAt,
                   (SELECT COUNT(*) FROM {rule.CorrectionsTable} c WHERE c.valoracion_id = v.id) AS Corrections,
                   (SELECT COUNT(*) FROM dbo.valoraciones_rectificaciones r
                     WHERE r.evento_id = ea.id AND r.perfil_codigo = @ProfileCode) AS Rectifications
              FROM {rule.AssessmentTable} v WITH (UPDLOCK, HOLDLOCK)
              JOIN dbo.eventos_asistenciales ea ON ea.id = v.evento_id
             CROSS APPLY (SELECT TOP 1 x.guardado_por_cuenta_id, x.guardado_en
                            FROM {rule.VersionsTable} x
                           WHERE x.valoracion_id = v.id
                           ORDER BY x.revision_evento DESC) last
             WHERE v.evento_id = @EventId AND ea.centro_id = @CenterId
            """, new { EventId = eventId, CenterId = centerId.Value, rule.ProfileCode }, transaction, cancellationToken: ct));
        if (row is null || row.EventStatus == rule.EditingStatus)
        {
            throw new DomainValidationException("ASSESSMENT_CORRECTION_NOT_AVAILABLE");
        }
        return new AssessmentState(
            row.AssessmentId, row.AuthorAccountId, new DateTimeOffset(row.LastSavedAt, TimeSpan.Zero), row.Corrections, row.Rectifications);
    }

    private static void Require(AssessmentState state, AccountId accountId, DateTimeOffset now, TimeSpan window, bool correction)
    {
        if (state.AuthorAccountId != accountId.Value)
        {
            throw new AccessDeniedException();
        }
        var open = AssessmentCorrectionWindow.IsOpen(state.LastSavedAt, now, window);
        if (correction && !open)
        {
            throw new DomainValidationException("ASSESSMENT_CORRECTION_WINDOW_EXPIRED");
        }
        if (!correction && open)
        {
            throw new DomainValidationException("ASSESSMENT_RECTIFICATION_WINDOW_OPEN");
        }
    }

    private sealed record AssessmentRow(
        Guid AssessmentId, string EventStatus, Guid AuthorAccountId, DateTime LastSavedAt, int Corrections, int Rectifications);

    private sealed record AssessmentState(
        Guid AssessmentId, Guid AuthorAccountId, DateTimeOffset LastSavedAt, int Corrections, int Rectifications);
}
