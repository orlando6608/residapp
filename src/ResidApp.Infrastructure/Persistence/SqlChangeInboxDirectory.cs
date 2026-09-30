using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>Traduce las bandejas ENF-02/ENF-03 y el detalle ENF-04 sobre dbo.eventos_asistenciales, que
/// reúne los cambios de Auxiliar y los eventos propios de Enfermería. Mismo predicado de ámbito "por
/// defecto o restringido" que SqlEnfermeriaResidentDirectory: solo eventos de unidades concedidas y de
/// residentes visibles para este ámbito. La observación original se lee siempre de su tabla de origen.
/// Un ámbito de Medicina ve con el mismo predicado solo los eventos escalados a Medicina (MED-02/MED-03) y
/// los eventos propios de Medicina (MED-18);
/// cada caso de uso comprueba antes el perfil del ámbito, así que ninguno de Enfermería llega aquí con uno
/// de Medicina ni al revés.</summary>
public sealed partial class SqlChangeInboxDirectory(SqlConnectionFactory connections) : IChangeInboxDirectory
{
    internal const string ScopedEventsFrom = """
          FROM dbo.eventos_asistenciales ea
          JOIN dbo.ambitos_perfil profile ON profile.id = @ProfileScopeId AND profile.centro_id = @CenterId
               AND profile.perfil_codigo IN ('ENFERMERIA', 'MEDICINA') AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
          JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = profile.id
               AND unit_scope.centro_id = profile.centro_id AND unit_scope.unidad_id = ea.unidad_id AND unit_scope.revocado_en IS NULL
          JOIN dbo.unidades unit ON unit.id = ea.unidad_id AND unit.centro_id = profile.centro_id
          JOIN dbo.residentes resident ON resident.id = ea.residente_id AND resident.centro_id = profile.centro_id
          LEFT JOIN dbo.ambitos_perfil_residente resident_scope ON resident_scope.ambito_perfil_id = profile.id
               AND resident_scope.centro_id = profile.centro_id AND resident_scope.residente_id = ea.residente_id
               AND resident_scope.revocado_en IS NULL
          LEFT JOIN dbo.cierres_cotidianos_residente closure ON closure.id = ea.cierre_id
          LEFT JOIN dbo.eventos_clinicos clinical ON clinical.id = ea.evento_clinico_id
          LEFT JOIN dbo.comunicaciones_familiares family ON family.evento_id = ea.id
          LEFT JOIN dbo.escalados_medicina escalation ON escalation.evento_id = ea.id
         WHERE ea.centro_id = @CenterId
           AND (profile.perfil_codigo = 'ENFERMERIA' OR escalation.id IS NOT NULL OR ea.origen_codigo = 'EVENTO_MEDICINA')
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
               AND ea.estado_codigo IN ('PENDIENTE', 'EN_VALORACION')
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
                   ea.valoracion_iniciada_en AS AssessmentStartedAt,
                   CAST(CASE WHEN ea.cerrado_por_cuenta_id = profile.cuenta_id THEN 1 ELSE 0 END AS BIT) AS ClosedByCurrentAccount,
                   ea.cerrado_en AS ClosedAt, ea.comunicacion_familiar_codigo AS FamilyCommunicationDecisionCode,
                   family.tipo_codigo AS FamilyCommunicationTypeCode, family.texto AS FamilyCommunicationText,
                   family.preparado_en AS FamilyCommunicationPreparedAt, escalation.motivo AS EscalationReason,
                   CAST(CASE WHEN escalation.escalado_por_cuenta_id = profile.cuenta_id THEN 1 ELSE 0 END AS BIT) AS EscalatedByCurrentAccount,
                   escalation.escalado_en AS EscalatedAt,
                   CASE WHEN ea.valoracion_medica_iniciada_por_cuenta_id IS NULL THEN NULL
                        WHEN ea.valoracion_medica_iniciada_por_cuenta_id = profile.cuenta_id THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END
                       AS MedicalStartedByCurrentAccount,
                   ea.valoracion_medica_iniciada_en AS MedicalStartedAt
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
             ORDER BY area.area_codigo, opt.opcion_codigo
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
             WHERE v.evento_id = @EventId AND v.estado_codigo IN ('BORRADOR', 'CERRADA')
            """, new { ProfileScopeId = profileScopeId, EventId = eventId }, cancellationToken: ct));

        var ranges = (await connection.QueryAsync<RangeRow>(new CommandDefinition("""
            SELECT constante_codigo AS Code, minimo AS Min, maximo AS Max
              FROM dbo.rangos_referencia_constantes WHERE centro_id = @CenterId
            """, new { CenterId = centerId.Value }, cancellationToken: ct)))
            .Select(r => new VitalSignRange(EnumCode.ParseCode<VitalSignCode>(r.Code), r.Min, r.Max))
            .ToList();

        var followUp = await connection.QuerySingleOrDefaultAsync<FollowUpRow>(new CommandDefinition("""
            SELECT s.id AS Id, s.fecha_prevista AS DueDate, s.criterio AS Criterion, s.indicaciones_continuidad AS ContinuityNotes,
                   CAST(CASE WHEN s.iniciado_por_cuenta_id = profile.cuenta_id THEN 1 ELSE 0 END AS BIT) AS StartedByCurrentAccount,
                   s.iniciado_en AS StartedAt
              FROM dbo.seguimientos s
              JOIN dbo.ambitos_perfil profile ON profile.id = @ProfileScopeId
             WHERE s.evento_id = @EventId
            """, new { ProfileScopeId = profileScopeId, EventId = eventId }, cancellationToken: ct));
        var followUpActions = followUp is null ? [] : (await connection.QueryAsync<FollowUpActionRow>(new CommandDefinition("""
            SELECT a.id AS Id, a.tipo_codigo AS TypeCode, a.texto AS [Text], a.fecha_prevista AS DueDate, a.criterio AS Criterion,
                   a.equipo_entrante AS IncomingTeam, a.transferencia_id AS TransferId,
                   CAST(CASE WHEN a.registrado_por_cuenta_id = profile.cuenta_id THEN 1 ELSE 0 END AS BIT) AS ByCurrentAccount,
                   a.registrado_en AS RecordedAt
              FROM dbo.seguimiento_acciones a
              JOIN dbo.ambitos_perfil profile ON profile.id = @ProfileScopeId
             WHERE a.seguimiento_id = @FollowUpId
             ORDER BY a.registrado_en ASC
            """, new { ProfileScopeId = profileScopeId, FollowUpId = followUp.Id }, cancellationToken: ct)))
            .Select(ToSummary)
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
                assessment.LastUpdatedByCurrentAccount, new DateTimeOffset(assessment.LastUpdatedAt, TimeSpan.Zero),
                await FindAmendmentsAsync(connection, profileScopeId, eventId, medical: false, ct)),
            ranges,
            row.ClosedAt is null ? null : new ClinicalEventClosure(
                row.ClosedByCurrentAccount, new DateTimeOffset(row.ClosedAt.Value, TimeSpan.Zero),
                EnumCode.ParseCode<FamilyCommunicationDecision>(row.FamilyCommunicationDecisionCode!),
                row.FamilyCommunicationTypeCode is null ? null : new PreparedFamilyCommunication(
                    EnumCode.ParseCode<FamilyCommunicationType>(row.FamilyCommunicationTypeCode), row.FamilyCommunicationText!,
                    new DateTimeOffset(row.FamilyCommunicationPreparedAt!.Value, TimeSpan.Zero))),
            followUp is null ? null : new FollowUpDetail(
                followUp.DueDate is null ? null : DateOnly.FromDateTime(followUp.DueDate.Value), followUp.Criterion,
                followUp.ContinuityNotes, followUp.StartedByCurrentAccount, new DateTimeOffset(followUp.StartedAt, TimeSpan.Zero),
                followUpActions),
            row.EscalatedAt is null ? null : new ClinicalEventEscalation(
                row.EscalationReason!, row.EscalatedByCurrentAccount, new DateTimeOffset(row.EscalatedAt.Value, TimeSpan.Zero)),
            new MedicalDetail(
                row.MedicalStartedByCurrentAccount,
                row.MedicalStartedAt is null ? null : new DateTimeOffset(row.MedicalStartedAt.Value, TimeSpan.Zero),
                await FindMedicalAssessmentAsync(connection, profileScopeId, eventId, ct),
                (await QueryIndicationsAsync(connection, profileScopeId, "WHERE i.evento_id = @EventId", new { ProfileScopeId = profileScopeId, EventId = eventId }, ct))
                    .Select(r => r.Summary).ToList(),
                await FindMedicalFollowUpAsync(connection, profileScopeId, eventId, ct)),
            await FindUrgentProtocolAsync(connection, profileScopeId, eventId, ct),
            await FindReferralAsync(connection, profileScopeId, eventId, ct),
            (await FindContextsAsync(connection, [eventId], ct)).GetValueOrDefault(eventId));
    }

    public async Task<IReadOnlyList<ClosedEventSummary>> ListClosedEventsAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);

        var events = (await connection.QueryAsync<ClosedEventRow>(new CommandDefinition($"""
            SELECT ea.id AS EventId, ea.origen_codigo AS OriginCode, ea.clasificacion_codigo AS ClassificationCode,
                   clinical.observacion AS Observation,
                   COALESCE(closure.registrado_por_perfil, clinical.registrado_por_perfil) AS AuthorProfileCode,
                   ea.recibido_en AS OccurredAt, ea.cerrado_en AS ClosedAt,
                   CAST(CASE WHEN escalation.id IS NULL THEN 0 ELSE 1 END AS BIT) AS Escalated
            {ScopedEventsFrom}
               AND ea.residente_id = @ResidentId
               AND ea.estado_codigo = 'CERRADO'
             ORDER BY ea.cerrado_en DESC
            """, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value, ResidentId = residentId.Value },
            cancellationToken: ct))).ToList();
        if (events.Count == 0)
        {
            return [];
        }

        var closureIds = events.Where(e => e.OriginCode == "CAMBIO_AUXILIAR").Select(e => e.EventId).ToList();
        IEnumerable<AreaCodeRow> areaRows = closureIds.Count == 0 ? [] : await connection.QueryAsync<AreaCodeRow>(new CommandDefinition("""
            SELECT cierre_id AS ClosureId, area_codigo AS AreaCode FROM dbo.cierres_cotidianos_cambio_areas WHERE cierre_id IN @ClosureIds
            """, new { ClosureIds = closureIds }, cancellationToken: ct));
        var areasByClosureId = areaRows
            .GroupBy(a => a.ClosureId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<DailyChangeAreaCode>)g.Select(a => EnumCode.ParseCode<DailyChangeAreaCode>(a.AreaCode)).ToList());

        var contexts = await FindContextsAsync(connection, events.Select(e => e.EventId).ToList(), ct);

        return events.Select(e => new ClosedEventSummary(
            e.EventId, EnumCode.ParseCode<ClinicalEventOrigin>(e.OriginCode), EnumCode.ParseCode<DailyChangeClassification>(e.ClassificationCode),
            areasByClosureId.GetValueOrDefault(e.EventId, []), e.Observation, EnumCode.ParseCode<SystemProfile>(e.AuthorProfileCode),
            new DateTimeOffset(e.OccurredAt, TimeSpan.Zero), new DateTimeOffset(e.ClosedAt, TimeSpan.Zero), e.Escalated,
            contexts.GetValueOrDefault(e.EventId))).ToList();
    }

    /// <summary>HIS-03: la instantánea de cada evento (dbo.eventos_contexto); un evento sin fila no aparece.</summary>
    private static async Task<Dictionary<Guid, ClinicalEventContext>> FindContextsAsync(
        System.Data.IDbConnection connection, IReadOnlyList<Guid> eventIds, CancellationToken ct)
    {
        var rows = await connection.QueryAsync<ContextRow>(new CommandDefinition("""
            SELECT ctx.evento_id AS EventId, baseline.numero_version AS BaselineVersionNumber, baseline.firmado_en AS BaselineSignedAt,
                   unit.nombre_visible AS UnitName
              FROM dbo.eventos_contexto ctx
              LEFT JOIN dbo.basales_version baseline ON baseline.id = ctx.version_basal_id
              LEFT JOIN dbo.intervalos_ubicacion_residente location ON location.id = ctx.intervalo_ubicacion_id
              LEFT JOIN dbo.unidades unit ON unit.id = location.unidad_id
             WHERE ctx.evento_id IN @EventIds
            """, new { EventIds = eventIds }, cancellationToken: ct));
        return rows.ToDictionary(r => r.EventId, r => new ClinicalEventContext(
            r.BaselineVersionNumber, r.BaselineSignedAt is null ? null : new DateTimeOffset(r.BaselineSignedAt.Value, TimeSpan.Zero),
            r.UnitName));
    }

    /// <summary>ENF-12/MED-14: el informe de derivación firmado y los intentos de llamada, de cualquiera de los
    /// dos perfiles (el PDF no se lee aquí: se descarga aparte, auditado).</summary>
    private static async Task<ReferralDetail?> FindReferralAsync(
        System.Data.IDbConnection connection, Guid profileScopeId, Guid eventId, CancellationToken ct)
    {
        var report = await connection.QuerySingleOrDefaultAsync<ReferralRow>(new CommandDefinition("""
            SELECT d.perfil_codigo AS ProfileCode, d.motivo AS Reason,
                   CAST(CASE WHEN d.firmado_por_cuenta_id = profile.cuenta_id THEN 1 ELSE 0 END AS BIT) AS SignedByCurrentAccount,
                   d.firmado_en AS SignedAt, d.huella_contenido AS ContentHash
              FROM dbo.informes_derivacion d
              JOIN dbo.ambitos_perfil profile ON profile.id = @ProfileScopeId
             WHERE d.evento_id = @EventId
            """, new { ProfileScopeId = profileScopeId, EventId = eventId }, cancellationToken: ct));
        if (report is null)
        {
            return null;
        }

        var attempts = (await connection.QueryAsync<FamilyCallAttemptRow>(new CommandDefinition("""
            SELECT c.contacto AS Contact, c.llamado_en AS CalledAt, c.resultado_codigo AS ResultCode, c.nota AS Note,
                   CAST(CASE WHEN c.registrado_por_cuenta_id = profile.cuenta_id THEN 1 ELSE 0 END AS BIT) AS ByCurrentAccount,
                   c.registrado_en AS RecordedAt
              FROM dbo.intentos_llamada_familia c
              JOIN dbo.ambitos_perfil profile ON profile.id = @ProfileScopeId
             WHERE c.evento_id = @EventId
             ORDER BY c.registrado_en ASC
            """, new { ProfileScopeId = profileScopeId, EventId = eventId }, cancellationToken: ct)))
            .Select(r => new FamilyCallAttemptSummary(
                r.Contact, new DateTimeOffset(r.CalledAt, TimeSpan.Zero), EnumCode.ParseCode<FamilyCallResult>(r.ResultCode), r.Note,
                r.ByCurrentAccount, new DateTimeOffset(r.RecordedAt, TimeSpan.Zero)))
            .ToList();

        return new ReferralDetail(
            EnumCode.ParseCode<SystemProfile>(report.ProfileCode), report.Reason, report.SignedByCurrentAccount,
            new DateTimeOffset(report.SignedAt, TimeSpan.Zero), report.ContentHash, attempts);
    }

    private sealed record ReferralRow(string ProfileCode, string Reason, bool SignedByCurrentAccount, DateTime SignedAt, string ContentHash);

    private sealed record FamilyCallAttemptRow(
        string Contact, DateTime CalledAt, string ResultCode, string? Note, bool ByCurrentAccount, DateTime RecordedAt);

    public async Task<ResidentIdentification?> FindResidentIdentificationAsync(
        Guid profileScopeId, CenterId centerId, Guid eventId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);

        var row = await connection.QuerySingleOrDefaultAsync<ResidentIdentificationRow>(new CommandDefinition($"""
            SELECT resident.nombre_visible AS DisplayName, resident.fecha_nacimiento AS BirthDate,
                   resident.sexo_documentado_codigo AS DocumentedSexCode,
                   (SELECT c.nombre_visible FROM dbo.centros c WHERE c.id = ea.centro_id) AS CenterName, unit.nombre_visible AS UnitName
            {ScopedEventsFrom}
               AND ea.id = @EventId
            """, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value, EventId = eventId }, cancellationToken: ct));
        return row is null ? null : new ResidentIdentification(
            row.DisplayName, DateOnly.FromDateTime(row.BirthDate), EnumCode.ParseCode<DocumentedSexCode>(row.DocumentedSexCode),
            row.CenterName, row.UnitName);
    }

    private sealed record ResidentIdentificationRow(
        string DisplayName, DateTime BirthDate, string DocumentedSexCode, string CenterName, string? UnitName);

    /// <summary>ENF-11/MED-13: el protocolo urgente del evento con sus registros, de cualquiera de los dos
    /// perfiles (el otro perfil lo ve en solo lectura).</summary>
    private static async Task<UrgentProtocolDetail?> FindUrgentProtocolAsync(
        System.Data.IDbConnection connection, Guid profileScopeId, Guid eventId, CancellationToken ct)
    {
        var protocol = await connection.QuerySingleOrDefaultAsync<UrgentProtocolRow>(new CommandDefinition("""
            SELECT p.id AS Id, p.perfil_codigo AS ProfileCode, p.nota_activacion AS ActivationNote,
                   CAST(CASE WHEN p.activado_por_cuenta_id = profile.cuenta_id THEN 1 ELSE 0 END AS BIT) AS ActivatedByCurrentAccount,
                   p.activado_en AS ActivatedAt
              FROM dbo.protocolos_urgentes p
              JOIN dbo.ambitos_perfil profile ON profile.id = @ProfileScopeId
             WHERE p.evento_id = @EventId
            """, new { ProfileScopeId = profileScopeId, EventId = eventId }, cancellationToken: ct));
        if (protocol is null)
        {
            return null;
        }

        var entries = (await connection.QueryAsync<UrgentProtocolEntryRow>(new CommandDefinition("""
            SELECT r.tipo_codigo AS TypeCode, r.texto AS [Text], r.servicio_contactado AS Service, r.contactado_en AS ContactedAt,
                   CAST(CASE WHEN r.registrado_por_cuenta_id = profile.cuenta_id THEN 1 ELSE 0 END AS BIT) AS ByCurrentAccount,
                   r.registrado_en AS RecordedAt
              FROM dbo.protocolo_urgente_registros r
              JOIN dbo.ambitos_perfil profile ON profile.id = @ProfileScopeId
             WHERE r.protocolo_id = @ProtocolId
             ORDER BY r.registrado_en ASC
            """, new { ProfileScopeId = profileScopeId, ProtocolId = protocol.Id }, cancellationToken: ct)))
            .Select(r => new UrgentProtocolEntrySummary(
                EnumCode.ParseCode<UrgentProtocolEntryType>(r.TypeCode), r.Text, r.Service,
                r.ContactedAt is null ? null : new DateTimeOffset(r.ContactedAt.Value, TimeSpan.Zero),
                r.ByCurrentAccount, new DateTimeOffset(r.RecordedAt, TimeSpan.Zero)))
            .ToList();

        return new UrgentProtocolDetail(
            EnumCode.ParseCode<SystemProfile>(protocol.ProfileCode), protocol.ActivationNote, protocol.ActivatedByCurrentAccount,
            new DateTimeOffset(protocol.ActivatedAt, TimeSpan.Zero), entries);
    }

    /// <summary>ENF-11/MED-13: protocolos urgentes activos del ámbito, del más antiguo al más reciente, con su
    /// último registro. Con un ámbito de Medicina, solo los de eventos escalados (ScopedEventsFrom).</summary>
    public async Task<IReadOnlyList<UrgentProtocolSummary>> ListUrgentProtocolsAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);

        var rows = await connection.QueryAsync<UrgentProtocolSummaryRow>(new CommandDefinition($"""
            SELECT scoped.EventId, scoped.ResidentId, scoped.ResidentDisplayName, scoped.UnitName, scoped.StatusCode,
                   p.activado_en AS ActivatedAt, last_entry.tipo_codigo AS LastEntryTypeCode, last_entry.registrado_en AS LastEntryAt
              FROM (SELECT ea.id AS EventId, ea.residente_id AS ResidentId, resident.nombre_visible AS ResidentDisplayName,
                           unit.nombre_visible AS UnitName, ea.estado_codigo AS StatusCode
                    {ScopedEventsFrom}
                       AND ea.estado_codigo IN ('PROTOCOLO_URGENTE', 'PROTOCOLO_URGENTE_MEDICO')) scoped
              JOIN dbo.protocolos_urgentes p ON p.evento_id = scoped.EventId
              OUTER APPLY (SELECT TOP 1 r.tipo_codigo, r.registrado_en FROM dbo.protocolo_urgente_registros r
                            WHERE r.protocolo_id = p.id ORDER BY r.registrado_en DESC) last_entry
             ORDER BY p.activado_en ASC
            """, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value }, cancellationToken: ct));

        return rows.Select(r => new UrgentProtocolSummary(
            r.EventId, ResidentId.From(r.ResidentId), r.ResidentDisplayName, r.UnitName, EnumCode.ParseCode<ClinicalEventStatus>(r.StatusCode),
            new DateTimeOffset(r.ActivatedAt, TimeSpan.Zero),
            r.LastEntryTypeCode is null ? null : EnumCode.ParseCode<UrgentProtocolEntryType>(r.LastEntryTypeCode),
            r.LastEntryAt is null ? null : new DateTimeOffset(r.LastEntryAt.Value, TimeSpan.Zero))).ToList();
    }

    private sealed record UrgentProtocolRow(Guid Id, string ProfileCode, string? ActivationNote, bool ActivatedByCurrentAccount, DateTime ActivatedAt);

    private sealed record UrgentProtocolEntryRow(
        string TypeCode, string? Text, string? Service, DateTime? ContactedAt, bool ByCurrentAccount, DateTime RecordedAt);

    private sealed record UrgentProtocolSummaryRow(
        Guid EventId, Guid ResidentId, string ResidentDisplayName, string? UnitName, string StatusCode, DateTime ActivatedAt,
        string? LastEntryTypeCode, DateTime? LastEntryAt);

    /// <summary>MED-10 a MED-12: el seguimiento médico del evento con todas sus acciones, igual que el de
    /// Enfermería pero con su objetivo y sin indicaciones de continuidad.</summary>
    private static async Task<MedicalFollowUpDetail?> FindMedicalFollowUpAsync(
        System.Data.IDbConnection connection, Guid profileScopeId, Guid eventId, CancellationToken ct)
    {
        var followUp = await connection.QuerySingleOrDefaultAsync<MedicalFollowUpRow>(new CommandDefinition("""
            SELECT s.id AS Id, s.fecha_prevista AS DueDate, s.criterio AS Criterion, s.objetivo AS Objective,
                   CAST(CASE WHEN s.iniciado_por_cuenta_id = profile.cuenta_id THEN 1 ELSE 0 END AS BIT) AS StartedByCurrentAccount,
                   s.iniciado_en AS StartedAt
              FROM dbo.seguimientos_medicos s
              JOIN dbo.ambitos_perfil profile ON profile.id = @ProfileScopeId
             WHERE s.evento_id = @EventId
            """, new { ProfileScopeId = profileScopeId, EventId = eventId }, cancellationToken: ct));
        if (followUp is null)
        {
            return null;
        }

        var actions = (await connection.QueryAsync<FollowUpActionRow>(new CommandDefinition("""
            SELECT a.id AS Id, a.tipo_codigo AS TypeCode, a.texto AS [Text], a.fecha_prevista AS DueDate, a.criterio AS Criterion,
                   a.equipo_entrante AS IncomingTeam, a.transferencia_id AS TransferId,
                   CAST(CASE WHEN a.registrado_por_cuenta_id = profile.cuenta_id THEN 1 ELSE 0 END AS BIT) AS ByCurrentAccount,
                   a.registrado_en AS RecordedAt
              FROM dbo.seguimiento_medico_acciones a
              JOIN dbo.ambitos_perfil profile ON profile.id = @ProfileScopeId
             WHERE a.seguimiento_id = @FollowUpId
             ORDER BY a.registrado_en ASC
            """, new { ProfileScopeId = profileScopeId, FollowUpId = followUp.Id }, cancellationToken: ct)))
            .Select(ToSummary)
            .ToList();

        return new MedicalFollowUpDetail(followUp.Objective, new FollowUpDetail(
            followUp.DueDate is null ? null : DateOnly.FromDateTime(followUp.DueDate.Value), followUp.Criterion, null,
            followUp.StartedByCurrentAccount, new DateTimeOffset(followUp.StartedAt, TimeSpan.Zero), actions));
    }

    private static FollowUpActionSummary ToSummary(FollowUpActionRow a) => new(
        a.Id, EnumCode.ParseCode<FollowUpActionType>(a.TypeCode), a.Text,
        a.DueDate is null ? null : DateOnly.FromDateTime(a.DueDate.Value), a.Criterion, a.IncomingTeam, a.TransferId,
        a.ByCurrentAccount, new DateTimeOffset(a.RecordedAt, TimeSpan.Zero));

    private static async Task<MedicalAssessmentDraft?> FindMedicalAssessmentAsync(
        System.Data.IDbConnection connection, Guid profileScopeId, Guid eventId, CancellationToken ct)
    {
        var assessment = await connection.QuerySingleOrDefaultAsync<MedicalAssessmentRow>(new CommandDefinition("""
            SELECT v.hallazgos_exploracion AS Findings, v.valoracion AS Assessment, v.actuaciones AS Actions,
                   v.temperatura_celsius AS TemperatureCelsius, v.tension_sistolica_mmhg AS SystolicMmHg,
                   v.tension_diastolica_mmhg AS DiastolicMmHg, v.frecuencia_cardiaca_lpm AS HeartRateBpm,
                   v.frecuencia_respiratoria_rpm AS RespiratoryRateRpm, v.saturacion_o2_pct AS OxygenSaturationPct,
                   v.soporte_respiratorio_codigo AS RespiratorySupportCode, v.flujo_o2_lpm AS OxygenFlowLpm, v.glucemia_mg_dl AS GlucoseMgDl,
                   v.otra_constante_nombre AS OtherName, v.otra_constante_valor AS OtherValue, v.otra_constante_unidad AS OtherUnit,
                   CAST(CASE WHEN v.actualizado_por_cuenta_id = profile.cuenta_id THEN 1 ELSE 0 END AS BIT) AS LastUpdatedByCurrentAccount,
                   v.actualizado_en AS LastUpdatedAt
              FROM dbo.valoraciones_medicas v
              JOIN dbo.ambitos_perfil profile ON profile.id = @ProfileScopeId
             WHERE v.evento_id = @EventId
            """, new { ProfileScopeId = profileScopeId, EventId = eventId }, cancellationToken: ct));
        return assessment is null ? null : new MedicalAssessmentDraft(
            new MedicalAssessmentContent(
                assessment.Findings, assessment.Assessment, assessment.Actions,
                new VitalSigns(
                    assessment.TemperatureCelsius, assessment.SystolicMmHg, assessment.DiastolicMmHg, assessment.HeartRateBpm,
                    assessment.RespiratoryRateRpm, assessment.OxygenSaturationPct,
                    assessment.RespiratorySupportCode is null ? null : EnumCode.ParseCode<RespiratorySupportCode>(assessment.RespiratorySupportCode),
                    assessment.OxygenFlowLpm, assessment.GlucoseMgDl, assessment.OtherName, assessment.OtherValue, assessment.OtherUnit)),
            assessment.LastUpdatedByCurrentAccount, new DateTimeOffset(assessment.LastUpdatedAt, TimeSpan.Zero),
            await FindAmendmentsAsync(connection, profileScopeId, eventId, medical: true, ct));
    }

    /// <summary>ENF-10/MED-08: indicaciones de los eventos visibles para el ámbito (mismo predicado que las
    /// bandejas), de la más antigua a la más reciente.</summary>
    public async Task<IReadOnlyList<MedicalIndicationListItem>> ListIndicationsAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);

        var scoped = (await connection.QueryAsync<IndicationEventRow>(new CommandDefinition($"""
            SELECT ea.id AS EventId, ea.residente_id AS ResidentId, resident.nombre_visible AS ResidentDisplayName,
                   unit.nombre_visible AS UnitName, ea.estado_codigo AS StatusCode, ea.cerrado_en AS ClosedAt
            {ScopedEventsFrom}
               AND EXISTS (SELECT 1 FROM dbo.indicaciones_medicas i WHERE i.evento_id = ea.id)
            """, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value }, cancellationToken: ct))).ToDictionary(e => e.EventId);
        if (scoped.Count == 0)
        {
            return [];
        }

        var indications = await QueryIndicationsAsync(connection, profileScopeId, "WHERE i.evento_id IN @EventIds",
            new { ProfileScopeId = profileScopeId, EventIds = scoped.Keys.ToList() }, ct);
        return indications.Select(i =>
        {
            var e = scoped[i.EventId];
            return new MedicalIndicationListItem(
                e.EventId, ResidentId.From(e.ResidentId), e.ResidentDisplayName, e.UnitName,
                EnumCode.ParseCode<ClinicalEventStatus>(e.StatusCode),
                e.ClosedAt is null ? null : new DateTimeOffset(e.ClosedAt.Value, TimeSpan.Zero), i.Summary);
        }).ToList();
    }

    private static async Task<IReadOnlyList<(Guid EventId, MedicalIndicationSummary Summary)>> QueryIndicationsAsync(
        System.Data.IDbConnection connection, Guid profileScopeId, string where, object parameters, CancellationToken ct)
    {
        var rows = await connection.QueryAsync<IndicationRow>(new CommandDefinition($"""
            SELECT i.id AS Id, i.evento_id AS EventId, i.texto AS [Text], i.fecha_prevista AS DueDate, i.criterio AS Criterion,
                   i.informacion_adicional AS AdditionalInformation,
                   CAST(CASE WHEN i.emitida_por_cuenta_id = profile.cuenta_id THEN 1 ELSE 0 END AS BIT) AS IssuedByCurrentAccount,
                   i.emitida_en AS IssuedAt, i.estado_codigo AS StatusCode, i.revision AS Revision, i.leida_en AS ReadAt,
                   i.resuelta_en AS ResolvedAt, i.incidencia AS Incident
              FROM dbo.indicaciones_medicas i
              JOIN dbo.ambitos_perfil profile ON profile.id = @ProfileScopeId
             {where}
             ORDER BY i.emitida_en ASC
            """, parameters, cancellationToken: ct));
        return rows.Select(r => (r.EventId, new MedicalIndicationSummary(
            r.Id, r.Text, r.DueDate is null ? null : DateOnly.FromDateTime(r.DueDate.Value), r.Criterion, r.AdditionalInformation,
            r.IssuedByCurrentAccount, new DateTimeOffset(r.IssuedAt, TimeSpan.Zero), EnumCode.ParseCode<MedicalIndicationStatus>(r.StatusCode),
            r.Revision, r.ReadAt is null ? null : new DateTimeOffset(r.ReadAt.Value, TimeSpan.Zero),
            r.ResolvedAt is null ? null : new DateTimeOffset(r.ResolvedAt.Value, TimeSpan.Zero), r.Incident))).ToList();
    }

    private sealed record IndicationEventRow(
        Guid EventId, Guid ResidentId, string ResidentDisplayName, string? UnitName, string StatusCode, DateTime? ClosedAt);

    private sealed record IndicationRow(
        Guid Id, Guid EventId, string Text, DateTime? DueDate, string? Criterion, string? AdditionalInformation,
        bool IssuedByCurrentAccount, DateTime IssuedAt, string StatusCode, int Revision, DateTime? ReadAt, DateTime? ResolvedAt,
        string? Incident);

    private sealed record MedicalAssessmentRow(
        string? Findings, string? Assessment, string? Actions,
        decimal? TemperatureCelsius, short? SystolicMmHg, short? DiastolicMmHg, short? HeartRateBpm, short? RespiratoryRateRpm,
        short? OxygenSaturationPct, string? RespiratorySupportCode, decimal? OxygenFlowLpm, short? GlucoseMgDl,
        string? OtherName, string? OtherValue, string? OtherUnit, bool LastUpdatedByCurrentAccount, DateTime LastUpdatedAt);

    /// <summary>MED-02: escalados del ámbito de Medicina, del más antiguo al más reciente, con las constantes
    /// y las actuaciones de la valoración de Enfermería (cerrada al escalar). Incluye los eventos propios de
    /// Medicina en valoración (MED-18), con su observación y la hora de su registro.</summary>
    public async Task<IReadOnlyList<EscalationSummary>> ListEscalationsAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);

        var rows = await connection.QueryAsync<EscalationSummaryRow>(new CommandDefinition($"""
            SELECT scoped.EventId, scoped.ResidentId, scoped.ResidentDisplayName, scoped.UnitName, scoped.Reason, scoped.Observation,
                   scoped.ReceivedAt,
                   v.actuaciones AS Actions, v.temperatura_celsius AS TemperatureCelsius, v.tension_sistolica_mmhg AS SystolicMmHg,
                   v.tension_diastolica_mmhg AS DiastolicMmHg, v.frecuencia_cardiaca_lpm AS HeartRateBpm,
                   v.frecuencia_respiratoria_rpm AS RespiratoryRateRpm, v.saturacion_o2_pct AS OxygenSaturationPct,
                   v.soporte_respiratorio_codigo AS RespiratorySupportCode, v.flujo_o2_lpm AS OxygenFlowLpm, v.glucemia_mg_dl AS GlucoseMgDl,
                   v.otra_constante_nombre AS OtherName, v.otra_constante_valor AS OtherValue, v.otra_constante_unidad AS OtherUnit,
                   scoped.StatusCode
              FROM (SELECT ea.id AS EventId, ea.residente_id AS ResidentId, resident.nombre_visible AS ResidentDisplayName,
                           unit.nombre_visible AS UnitName, escalation.motivo AS Reason,
                           CASE WHEN ea.origen_codigo = 'EVENTO_MEDICINA' THEN clinical.observacion END AS Observation,
                           COALESCE(escalation.escalado_en, ea.recibido_en) AS ReceivedAt, ea.estado_codigo AS StatusCode
                    {ScopedEventsFrom}
                       AND ea.estado_codigo IN ('ESCALADO_MEDICINA', 'EN_VALORACION_MEDICA')) scoped
              LEFT JOIN dbo.valoraciones_enfermeria v ON v.evento_id = scoped.EventId AND v.estado_codigo = 'CERRADA'
            """, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value }, cancellationToken: ct));

        return rows.Select(r => new EscalationSummary(
            r.EventId, ResidentId.From(r.ResidentId), r.ResidentDisplayName, r.UnitName, r.Reason, r.Observation,
            new DateTimeOffset(r.ReceivedAt, TimeSpan.Zero),
            new VitalSigns(
                r.TemperatureCelsius, r.SystolicMmHg, r.DiastolicMmHg, r.HeartRateBpm, r.RespiratoryRateRpm, r.OxygenSaturationPct,
                r.RespiratorySupportCode is null ? null : EnumCode.ParseCode<RespiratorySupportCode>(r.RespiratorySupportCode),
                r.OxygenFlowLpm, r.GlucoseMgDl, r.OtherName, r.OtherValue, r.OtherUnit),
            r.Actions, EnumCode.ParseCode<ClinicalEventStatus>(r.StatusCode)))
            .OrderBy(e => e.ReceivedAt)
            .ToList();
    }

    /// <summary>Escalados abiertos del ámbito de Enfermería (la misma regla de unidades y residentes que sus bandejas):
    /// eventos con escalado a Medicina que todavía no están cerrados, del escalado más antiguo al más reciente.</summary>
    public async Task<IReadOnlyList<OpenEscalationSummary>> ListOpenEscalationsAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);

        var rows = await connection.QueryAsync<OpenEscalationRow>(new CommandDefinition($"""
            SELECT ea.id AS EventId, ea.residente_id AS ResidentId, resident.nombre_visible AS ResidentDisplayName,
                   unit.nombre_visible AS UnitName, escalation.motivo AS Reason, escalation.escalado_en AS EscalatedAt,
                   CAST(CASE WHEN escalation.escalado_por_cuenta_id = profile.cuenta_id THEN 1 ELSE 0 END AS BIT) AS EscalatedByCurrentAccount,
                   ea.estado_codigo AS StatusCode
            {ScopedEventsFrom}
               AND escalation.id IS NOT NULL AND ea.estado_codigo <> 'CERRADO'
             ORDER BY escalation.escalado_en
            """, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value }, cancellationToken: ct));

        return rows.Select(r => new OpenEscalationSummary(
            r.EventId, ResidentId.From(r.ResidentId), r.ResidentDisplayName, r.UnitName, r.Reason,
            new DateTimeOffset(r.EscalatedAt, TimeSpan.Zero), r.EscalatedByCurrentAccount,
            EnumCode.ParseCode<ClinicalEventStatus>(r.StatusCode))).ToList();
    }

    private sealed record OpenEscalationRow(
        Guid EventId, Guid ResidentId, string ResidentDisplayName, string? UnitName, string Reason, DateTime EscalatedAt,
        bool EscalatedByCurrentAccount, string StatusCode);

    private sealed record EscalationSummaryRow(
        Guid EventId, Guid ResidentId, string ResidentDisplayName, string? UnitName, string? Reason, string? Observation, DateTime ReceivedAt,
        string? Actions, decimal? TemperatureCelsius, short? SystolicMmHg, short? DiastolicMmHg, short? HeartRateBpm,
        short? RespiratoryRateRpm, short? OxygenSaturationPct, string? RespiratorySupportCode, decimal? OxygenFlowLpm,
        short? GlucoseMgDl, string? OtherName, string? OtherValue, string? OtherUnit, string StatusCode);

    /// <summary>ENF-08: seguimientos abiertos del ámbito. El plan vigente es el de la última reprogramación,
    /// si la hay; primero los que tienen fecha (de la más próxima o vencida a la más lejana), después los que
    /// solo tienen criterio.</summary>
    public async Task<IReadOnlyList<FollowUpSummary>> ListFollowUpsAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);

        var rows = await connection.QueryAsync<FollowUpSummaryRow>(new CommandDefinition($"""
            SELECT scoped.EventId, scoped.ResidentId, scoped.ResidentDisplayName, scoped.UnitName,
                   CASE WHEN reschedule.id IS NULL THEN seg.fecha_prevista ELSE reschedule.fecha_prevista END AS DueDate,
                   CASE WHEN reschedule.id IS NULL THEN seg.criterio ELSE reschedule.criterio END AS Criterion,
                   seg.iniciado_en AS StartedAt, last_action.tipo_codigo AS LastActionTypeCode, last_action.registrado_en AS LastActionAt,
                   CAST(CASE WHEN last_transfer.id IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM dbo.seguimiento_acciones r WHERE r.transferencia_id = last_transfer.id) THEN 1 ELSE 0 END AS BIT)
                       AS TransferPending
              FROM (SELECT ea.id AS EventId, ea.residente_id AS ResidentId, resident.nombre_visible AS ResidentDisplayName,
                           unit.nombre_visible AS UnitName
                    {ScopedEventsFrom}
                       AND ea.estado_codigo = 'EN_SEGUIMIENTO') scoped
              JOIN dbo.seguimientos seg ON seg.evento_id = scoped.EventId
              OUTER APPLY (SELECT TOP 1 a.id, a.fecha_prevista, a.criterio FROM dbo.seguimiento_acciones a
                            WHERE a.seguimiento_id = seg.id AND a.tipo_codigo = 'REPROGRAMACION' ORDER BY a.registrado_en DESC) reschedule
              OUTER APPLY (SELECT TOP 1 a.tipo_codigo, a.registrado_en FROM dbo.seguimiento_acciones a
                            WHERE a.seguimiento_id = seg.id ORDER BY a.registrado_en DESC) last_action
              OUTER APPLY (SELECT TOP 1 a.id FROM dbo.seguimiento_acciones a
                            WHERE a.seguimiento_id = seg.id AND a.tipo_codigo = 'TRANSFERENCIA' ORDER BY a.registrado_en DESC) last_transfer
            """, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value }, cancellationToken: ct));

        return rows
            .Select(r => new FollowUpSummary(
                r.EventId, ResidentId.From(r.ResidentId), r.ResidentDisplayName, r.UnitName,
                r.DueDate is null ? null : DateOnly.FromDateTime(r.DueDate.Value), r.Criterion,
                new DateTimeOffset(r.StartedAt, TimeSpan.Zero),
                r.LastActionTypeCode is null ? null : EnumCode.ParseCode<FollowUpActionType>(r.LastActionTypeCode),
                r.LastActionAt is null ? null : new DateTimeOffset(r.LastActionAt.Value, TimeSpan.Zero), r.TransferPending))
            .OrderBy(f => f.DueDate is null)
            .ThenBy(f => f.DueDate)
            .ThenBy(f => f.StartedAt)
            .ToList();
    }

    /// <summary>MED-11: seguimientos médicos abiertos del ámbito, con el mismo plan vigente y el mismo orden que
    /// ListFollowUpsAsync, más el objetivo y la última decisión de continuidad (transferir o conservar).</summary>
    public async Task<IReadOnlyList<MedicalFollowUpSummary>> ListMedicalFollowUpsAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);

        var rows = await connection.QueryAsync<MedicalFollowUpSummaryRow>(new CommandDefinition($"""
            SELECT scoped.EventId, scoped.ResidentId, scoped.ResidentDisplayName, scoped.UnitName, seg.objetivo AS Objective,
                   CASE WHEN reschedule.id IS NULL THEN seg.fecha_prevista ELSE reschedule.fecha_prevista END AS DueDate,
                   CASE WHEN reschedule.id IS NULL THEN seg.criterio ELSE reschedule.criterio END AS Criterion,
                   seg.iniciado_en AS StartedAt, last_action.tipo_codigo AS LastActionTypeCode, last_action.registrado_en AS LastActionAt,
                   continuity.tipo_codigo AS LastContinuityCode, continuity.equipo_entrante AS LastContinuityTeam,
                   CAST(CASE WHEN continuity.registrado_por_cuenta_id = scoped.AccountId THEN 1 ELSE 0 END AS BIT)
                       AS LastContinuityByCurrentAccount,
                   CAST(CASE WHEN last_transfer.id IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM dbo.seguimiento_medico_acciones r WHERE r.transferencia_id = last_transfer.id) THEN 1 ELSE 0 END AS BIT)
                       AS TransferPending
              FROM (SELECT ea.id AS EventId, ea.residente_id AS ResidentId, resident.nombre_visible AS ResidentDisplayName,
                           unit.nombre_visible AS UnitName, profile.cuenta_id AS AccountId
                    {ScopedEventsFrom}
                       AND ea.estado_codigo = 'EN_SEGUIMIENTO_MEDICO') scoped
              JOIN dbo.seguimientos_medicos seg ON seg.evento_id = scoped.EventId
              OUTER APPLY (SELECT TOP 1 a.id, a.fecha_prevista, a.criterio FROM dbo.seguimiento_medico_acciones a
                            WHERE a.seguimiento_id = seg.id AND a.tipo_codigo = 'REPROGRAMACION' ORDER BY a.registrado_en DESC) reschedule
              OUTER APPLY (SELECT TOP 1 a.tipo_codigo, a.registrado_en FROM dbo.seguimiento_medico_acciones a
                            WHERE a.seguimiento_id = seg.id ORDER BY a.registrado_en DESC) last_action
              OUTER APPLY (SELECT TOP 1 a.tipo_codigo, a.equipo_entrante, a.registrado_por_cuenta_id FROM dbo.seguimiento_medico_acciones a
                            WHERE a.seguimiento_id = seg.id AND a.tipo_codigo IN ('TRANSFERENCIA', 'CONSERVACION')
                            ORDER BY a.registrado_en DESC) continuity
              OUTER APPLY (SELECT TOP 1 a.id FROM dbo.seguimiento_medico_acciones a
                            WHERE a.seguimiento_id = seg.id AND a.tipo_codigo = 'TRANSFERENCIA' ORDER BY a.registrado_en DESC) last_transfer
            """, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value }, cancellationToken: ct));

        return rows
            .Select(r => new MedicalFollowUpSummary(
                r.EventId, ResidentId.From(r.ResidentId), r.ResidentDisplayName, r.UnitName, r.Objective,
                r.DueDate is null ? null : DateOnly.FromDateTime(r.DueDate.Value), r.Criterion,
                new DateTimeOffset(r.StartedAt, TimeSpan.Zero),
                r.LastActionTypeCode is null ? null : EnumCode.ParseCode<FollowUpActionType>(r.LastActionTypeCode),
                r.LastActionAt is null ? null : new DateTimeOffset(r.LastActionAt.Value, TimeSpan.Zero),
                r.LastContinuityCode is null ? null : EnumCode.ParseCode<FollowUpActionType>(r.LastContinuityCode),
                r.LastContinuityTeam, r.LastContinuityByCurrentAccount, r.TransferPending))
            .OrderBy(f => f.DueDate is null)
            .ThenBy(f => f.DueDate)
            .ThenBy(f => f.StartedAt)
            .ToList();
    }

    private sealed record FollowUpRow(
        Guid Id, DateTime? DueDate, string? Criterion, string? ContinuityNotes, bool StartedByCurrentAccount, DateTime StartedAt);

    private sealed record MedicalFollowUpRow(
        Guid Id, DateTime? DueDate, string? Criterion, string Objective, bool StartedByCurrentAccount, DateTime StartedAt);

    private sealed record MedicalFollowUpSummaryRow(
        Guid EventId, Guid ResidentId, string ResidentDisplayName, string? UnitName, string Objective, DateTime? DueDate,
        string? Criterion, DateTime StartedAt, string? LastActionTypeCode, DateTime? LastActionAt, string? LastContinuityCode,
        string? LastContinuityTeam, bool LastContinuityByCurrentAccount, bool TransferPending);

    private sealed record FollowUpActionRow(
        Guid Id, string TypeCode, string? Text, DateTime? DueDate, string? Criterion, string? IncomingTeam, Guid? TransferId,
        bool ByCurrentAccount, DateTime RecordedAt);

    private sealed record FollowUpSummaryRow(
        Guid EventId, Guid ResidentId, string ResidentDisplayName, string? UnitName, DateTime? DueDate, string? Criterion,
        DateTime StartedAt, string? LastActionTypeCode, DateTime? LastActionAt, bool TransferPending);

    public async Task<IReadOnlyList<PendingFamilyCommunicationSummary>> ListPendingFamilyCommunicationsAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);

        var rows = await connection.QueryAsync<FamilyCommunicationRow>(new CommandDefinition($"""
            SELECT ea.id AS EventId, ea.residente_id AS ResidentId, resident.nombre_visible AS ResidentDisplayName,
                   unit.nombre_visible AS UnitName, family.tipo_codigo AS TypeCode, family.texto AS Text, family.preparado_en AS PreparedAt
            {ScopedEventsFrom}
               AND family.estado_codigo = 'PENDIENTE_APROBACION'
             ORDER BY family.preparado_en ASC
            """, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value }, cancellationToken: ct));

        return rows.Select(r => new PendingFamilyCommunicationSummary(
            r.EventId, ResidentId.From(r.ResidentId), r.ResidentDisplayName, r.UnitName,
            new PreparedFamilyCommunication(
                EnumCode.ParseCode<FamilyCommunicationType>(r.TypeCode), r.Text, new DateTimeOffset(r.PreparedAt, TimeSpan.Zero)))).ToList();
    }

    private sealed record FamilyCommunicationRow(
        Guid EventId, Guid ResidentId, string ResidentDisplayName, string? UnitName, string TypeCode, string Text, DateTime PreparedAt);

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
        DateTime? AssessmentStartedAt, bool ClosedByCurrentAccount, DateTime? ClosedAt, string? FamilyCommunicationDecisionCode,
        string? FamilyCommunicationTypeCode, string? FamilyCommunicationText, DateTime? FamilyCommunicationPreparedAt,
        string? EscalationReason, bool EscalatedByCurrentAccount, DateTime? EscalatedAt, bool? MedicalStartedByCurrentAccount,
        DateTime? MedicalStartedAt);

    private sealed record AssessmentRow(
        string? Findings, string? Assessment, string? Actions, string? Communications, string? Outcome,
        decimal? TemperatureCelsius, short? SystolicMmHg, short? DiastolicMmHg, short? HeartRateBpm, short? RespiratoryRateRpm,
        short? OxygenSaturationPct, string? RespiratorySupportCode, decimal? OxygenFlowLpm, short? GlucoseMgDl,
        string? OtherName, string? OtherValue, string? OtherUnit, bool LastUpdatedByCurrentAccount, DateTime LastUpdatedAt);

    private sealed record AreaCodeRow(Guid ClosureId, string AreaCode);

    private sealed record ClosedEventRow(
        Guid EventId, string OriginCode, string ClassificationCode, string? Observation, string AuthorProfileCode,
        DateTime OccurredAt, DateTime ClosedAt, bool Escalated);

    private sealed record ContextRow(Guid EventId, int? BaselineVersionNumber, DateTime? BaselineSignedAt, string? UnitName);

    private sealed record AreaOptionRow(Guid AreaId, string AreaCode, string? FreeText, string? OptionCode);
}
