using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>Traduce las bandejas ENF-02/ENF-03 y el detalle ENF-04 sobre dbo.eventos_asistenciales, que
/// reúne los cambios de Auxiliar y los eventos propios de Enfermería. Mismo predicado de ámbito "por
/// defecto o restringido" que SqlEnfermeriaResidentDirectory: solo eventos de unidades concedidas y de
/// residentes visibles para este ámbito. La observación original se lee siempre de su tabla de origen.</summary>
public sealed class SqlChangeInboxDirectory(SqlConnectionFactory connections) : IChangeInboxDirectory
{
    private const string ScopedEventsFrom = """
          FROM dbo.eventos_asistenciales ea
          JOIN dbo.ambitos_perfil profile ON profile.id = @ProfileScopeId AND profile.centro_id = @CenterId
               AND profile.perfil_codigo = 'ENFERMERIA' AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
          JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = profile.id
               AND unit_scope.centro_id = profile.centro_id AND unit_scope.unidad_id = ea.unidad_id AND unit_scope.revocado_en IS NULL
          JOIN dbo.unidades unit ON unit.id = ea.unidad_id AND unit.centro_id = profile.centro_id
          JOIN dbo.residentes resident ON resident.id = ea.residente_id AND resident.centro_id = profile.centro_id
          LEFT JOIN dbo.ambitos_perfil_residente resident_scope ON resident_scope.ambito_perfil_id = profile.id
               AND resident_scope.centro_id = profile.centro_id AND resident_scope.residente_id = ea.residente_id
               AND resident_scope.revocado_en IS NULL
          LEFT JOIN dbo.cierres_cotidianos_residente closure ON closure.id = ea.cierre_id
          LEFT JOIN dbo.eventos_clinicos clinical ON clinical.id = ea.evento_clinico_id
         WHERE ea.centro_id = @CenterId
           AND (resident_scope.id IS NOT NULL OR NOT EXISTS (
               SELECT 1 FROM dbo.ambitos_perfil_residente restriction
                WHERE restriction.ambito_perfil_id = profile.id AND restriction.centro_id = profile.centro_id))
        """;

    public async Task<IReadOnlyList<PendingChangeSummary>> ListAsync(
        Guid profileScopeId, CenterId centerId, DailyChangeClassification classification, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);

        var events = (await connection.QueryAsync<EventRow>(new CommandDefinition($"""
            SELECT ea.id AS EventId, ea.origen_codigo AS OriginCode, ea.residente_id AS ResidentId, resident.nombre_visible AS ResidentDisplayName,
                   ea.unidad_id AS UnitId, unit.nombre_visible AS UnitName,
                   COALESCE(closure.registrado_por_perfil, clinical.registrado_por_perfil) AS AuthorProfileCode,
                   ea.recibido_en AS OccurredAt, closure.motivo_prioritario_codigo AS PriorityReasonCode,
                   closure.aviso_directo_documentado AS DirectNoticeNotes, clinical.observacion AS Observation,
                   ea.estado_codigo AS StatusCode
            {ScopedEventsFrom}
               AND ea.clasificacion_codigo = @ClassificationCode
             ORDER BY ea.recibido_en ASC
            """, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value, ClassificationCode = classification.ToCode() },
            cancellationToken: ct))).ToList();
        if (events.Count == 0)
        {
            return [];
        }

        // Solo los cambios de Auxiliar tienen áreas; su id de evento es el mismo que el del cierre.
        var closureIds = events.Where(e => e.OriginCode == "CAMBIO_AUXILIAR").Select(e => e.EventId).ToList();
        IEnumerable<AreaCodeRow> areaRows = closureIds.Count == 0 ? [] : await connection.QueryAsync<AreaCodeRow>(new CommandDefinition("""
            SELECT cierre_id AS ClosureId, area_codigo AS AreaCode FROM dbo.cierres_cotidianos_cambio_areas WHERE cierre_id IN @ClosureIds
            """, new { ClosureIds = closureIds }, cancellationToken: ct));
        var areasByClosureId = areaRows
            .GroupBy(a => a.ClosureId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<DailyChangeAreaCode>)g.Select(a => EnumCode.ParseCode<DailyChangeAreaCode>(a.AreaCode)).ToList());

        return events.Select(e => new PendingChangeSummary(
            e.EventId, EnumCode.ParseCode<ClinicalEventOrigin>(e.OriginCode), ResidentId.From(e.ResidentId), e.ResidentDisplayName,
            UnitId.From(e.UnitId), e.UnitName, areasByClosureId.GetValueOrDefault(e.EventId, []), e.Observation,
            EnumCode.ParseCode<SystemProfile>(e.AuthorProfileCode), new DateTimeOffset(e.OccurredAt, TimeSpan.Zero),
            e.PriorityReasonCode is null ? null : EnumCode.ParseCode<DailyChangePriorityReason>(e.PriorityReasonCode),
            e.DirectNoticeNotes, EnumCode.ParseCode<ClinicalEventStatus>(e.StatusCode))).ToList();
    }

    public async Task<PendingChangeDetail?> FindAsync(
        Guid profileScopeId, CenterId centerId, Guid eventId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);

        var row = await connection.QuerySingleOrDefaultAsync<EventDetailRow>(new CommandDefinition($"""
            SELECT ea.id AS EventId, ea.origen_codigo AS OriginCode, ea.residente_id AS ResidentId, resident.nombre_visible AS ResidentDisplayName,
                   ea.unidad_id AS UnitId, unit.nombre_visible AS UnitName, ea.clasificacion_codigo AS ClassificationCode,
                   closure.temperatura_celsius AS TemperatureCelsius, clinical.observacion AS Observation,
                   clinical.datos_clinicos_pertinentes AS ClinicalData,
                   COALESCE(closure.registrado_por_perfil, clinical.registrado_por_perfil) AS AuthorProfileCode,
                   closure.motivo_prioritario_codigo AS PriorityReasonCode, closure.aviso_directo_documentado AS DirectNoticeNotes,
                   ea.recibido_en AS OccurredAt, ea.estado_codigo AS StatusCode, ea.revision AS Revision,
                   CASE WHEN ea.valoracion_iniciada_por_cuenta_id IS NULL THEN NULL
                        WHEN ea.valoracion_iniciada_por_cuenta_id = profile.cuenta_id THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END
                       AS AssessmentStartedByCurrentAccount,
                   ea.valoracion_iniciada_en AS AssessmentStartedAt
            {ScopedEventsFrom}
               AND ea.id = @EventId
            """, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value, EventId = eventId }, cancellationToken: ct));
        if (row is null)
        {
            return null;
        }

        var areaOptionRows = await connection.QueryAsync<AreaOptionRow>(new CommandDefinition("""
            SELECT area.id AS AreaId, area.area_codigo AS AreaCode, area.texto_libre AS [FreeText], opt.opcion_codigo AS OptionCode
              FROM dbo.cierres_cotidianos_cambio_areas area
              LEFT JOIN dbo.cierres_cotidianos_cambio_area_opciones opt ON opt.area_id = area.id
             WHERE area.cierre_id = @EventId
             ORDER BY area.area_codigo
            """, new { EventId = eventId }, cancellationToken: ct));
        var areas = areaOptionRows
            .GroupBy(r => (r.AreaId, r.AreaCode, r.FreeText))
            .Select(g => new PendingChangeAreaSummary(
                EnumCode.ParseCode<DailyChangeAreaCode>(g.Key.AreaCode),
                g.Where(r => r.OptionCode is not null).Select(r => EnumCode.ParseCode<DailyChangeAreaOptionCode>(r.OptionCode!)).ToList(),
                g.Key.FreeText))
            .ToList();

        var assessment = await connection.QuerySingleOrDefaultAsync<AssessmentRow>(new CommandDefinition("""
            SELECT v.hallazgos AS Findings, v.valoracion AS Assessment, v.actuaciones AS Actions, v.comunicaciones AS Communications,
                   v.resultado AS Outcome, v.temperatura_celsius AS TemperatureCelsius, v.tension_sistolica_mmhg AS SystolicMmHg,
                   v.tension_diastolica_mmhg AS DiastolicMmHg, v.frecuencia_cardiaca_lpm AS HeartRateBpm,
                   v.frecuencia_respiratoria_rpm AS RespiratoryRateRpm, v.saturacion_o2_pct AS OxygenSaturationPct,
                   v.soporte_respiratorio_codigo AS RespiratorySupportCode, v.flujo_o2_lpm AS OxygenFlowLpm, v.glucemia_mg_dl AS GlucoseMgDl,
                   v.otra_constante_nombre AS OtherName, v.otra_constante_valor AS OtherValue, v.otra_constante_unidad AS OtherUnit,
                   CAST(CASE WHEN v.actualizado_por_cuenta_id = profile.cuenta_id THEN 1 ELSE 0 END AS BIT) AS LastUpdatedByCurrentAccount,
                   v.actualizado_en AS LastUpdatedAt
              FROM dbo.valoraciones_enfermeria v
              JOIN dbo.ambitos_perfil profile ON profile.id = @ProfileScopeId
             WHERE v.evento_id = @EventId AND v.estado_codigo = 'BORRADOR'
            """, new { ProfileScopeId = profileScopeId, EventId = eventId }, cancellationToken: ct));

        var ranges = (await connection.QueryAsync<RangeRow>(new CommandDefinition("""
            SELECT constante_codigo AS Code, minimo AS Min, maximo AS Max
              FROM dbo.rangos_referencia_constantes WHERE centro_id = @CenterId
            """, new { CenterId = centerId.Value }, cancellationToken: ct)))
            .Select(r => new VitalSignRange(EnumCode.ParseCode<VitalSignCode>(r.Code), r.Min, r.Max))
            .ToList();

        return new PendingChangeDetail(
            row.EventId, EnumCode.ParseCode<ClinicalEventOrigin>(row.OriginCode), ResidentId.From(row.ResidentId), row.ResidentDisplayName,
            UnitId.From(row.UnitId), row.UnitName, EnumCode.ParseCode<DailyChangeClassification>(row.ClassificationCode), areas,
            row.TemperatureCelsius, row.Observation, row.ClinicalData, EnumCode.ParseCode<SystemProfile>(row.AuthorProfileCode),
            row.PriorityReasonCode is null ? null : EnumCode.ParseCode<DailyChangePriorityReason>(row.PriorityReasonCode),
            row.DirectNoticeNotes, new DateTimeOffset(row.OccurredAt, TimeSpan.Zero), EnumCode.ParseCode<ClinicalEventStatus>(row.StatusCode),
            row.Revision, row.AssessmentStartedByCurrentAccount,
            row.AssessmentStartedAt is null ? null : new DateTimeOffset(row.AssessmentStartedAt.Value, TimeSpan.Zero),
            assessment is null ? null : new NursingAssessmentDraft(
                new NursingAssessmentContent(
                    assessment.Findings, assessment.Assessment, assessment.Actions, assessment.Communications, assessment.Outcome,
                    new VitalSigns(
                        assessment.TemperatureCelsius, assessment.SystolicMmHg, assessment.DiastolicMmHg, assessment.HeartRateBpm,
                        assessment.RespiratoryRateRpm, assessment.OxygenSaturationPct,
                        assessment.RespiratorySupportCode is null ? null : EnumCode.ParseCode<RespiratorySupportCode>(assessment.RespiratorySupportCode),
                        assessment.OxygenFlowLpm, assessment.GlucoseMgDl, assessment.OtherName, assessment.OtherValue, assessment.OtherUnit)),
                assessment.LastUpdatedByCurrentAccount, new DateTimeOffset(assessment.LastUpdatedAt, TimeSpan.Zero)),
            ranges);
    }

    private sealed record RangeRow(string Code, decimal? Min, decimal? Max);

    /// <summary>OccurredAt es DateTime, no DateTimeOffset: Dapper 2.1.79 no materializa DateTimeOffset en
    /// constructores de record leídos de DATETIME2 (ver SqlBaselineRepository, mismo hallazgo). Se
    /// convierte explícitamente al construir el record de puerto.</summary>
    private sealed record EventRow(
        Guid EventId, string OriginCode, Guid ResidentId, string ResidentDisplayName, Guid UnitId, string? UnitName,
        string AuthorProfileCode, DateTime OccurredAt, string? PriorityReasonCode, string? DirectNoticeNotes, string? Observation,
        string StatusCode);

    private sealed record EventDetailRow(
        Guid EventId, string OriginCode, Guid ResidentId, string ResidentDisplayName, Guid UnitId, string? UnitName, string ClassificationCode,
        decimal? TemperatureCelsius, string? Observation, string? ClinicalData, string AuthorProfileCode, string? PriorityReasonCode,
        string? DirectNoticeNotes, DateTime OccurredAt, string StatusCode, int Revision, bool? AssessmentStartedByCurrentAccount,
        DateTime? AssessmentStartedAt);

    private sealed record AssessmentRow(
        string? Findings, string? Assessment, string? Actions, string? Communications, string? Outcome,
        decimal? TemperatureCelsius, short? SystolicMmHg, short? DiastolicMmHg, short? HeartRateBpm, short? RespiratoryRateRpm,
        short? OxygenSaturationPct, string? RespiratorySupportCode, decimal? OxygenFlowLpm, short? GlucoseMgDl,
        string? OtherName, string? OtherValue, string? OtherUnit, bool LastUpdatedByCurrentAccount, DateTime LastUpdatedAt);

    private sealed record AreaCodeRow(Guid ClosureId, string AreaCode);

    private sealed record AreaOptionRow(Guid AreaId, string AreaCode, string? FreeText, string? OptionCode);
}
