using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>HIS-02: línea temporal del residente. Una consulta pequeña por fuente, todas limitadas a los eventos
/// visibles para el ámbito (ScopedEventsFrom, abiertos y cerrados), que se unen y ordenan aquí. Todas las
/// fuentes son de solo inserción o guardan la hora de cada hito, así que la línea temporal no reconstruye nada.</summary>
public sealed partial class SqlChangeInboxDirectory
{
    public Task<IReadOnlyList<TimelineEntry>> ListTimelineAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default) =>
        ListTimelineAsync(ScopedEventsFrom, profileScopeId, centerId, residentId, false, ct);

    public Task<IReadOnlyList<TimelineEntry>> ListDirectionTimelineAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, bool includeAuthorNames = false, CancellationToken ct = default) =>
        ListTimelineAsync(DirectionScopedEventsFrom, profileScopeId, centerId, residentId, includeAuthorNames, ct);

    private async Task<IReadOnlyList<TimelineEntry>> ListTimelineAsync(
        string scopedEventsFrom, Guid profileScopeId, CenterId centerId, ResidentId residentId, bool includeAuthorNames, CancellationToken ct)
    {
        // Solo la lectura auditada de Dirección (DIR-15) pide el nombre visible de quien guardó, corrigió o rectificó; la línea temporal
        // de Enfermería y Medicina sigue mostrando solo el perfil.
        string Author(string column) => includeAuthorNames
            ? $"(SELECT a.nombre_visible FROM dbo.cuentas a WHERE a.id = {column})"
            : "CAST(NULL AS NVARCHAR(200))";
        using var connection = await connections.OpenAsync(ct);
        var entries = new List<TimelineEntry>();

        var events = (await connection.QueryAsync<TimelineEventRow>(new CommandDefinition($"""
            SELECT ea.id AS EventId, ea.origen_codigo AS OriginCode, ea.clasificacion_codigo AS ClassificationCode,
                   clinical.observacion AS Observation,
                   COALESCE(closure.registrado_por_perfil, clinical.registrado_por_perfil) AS AuthorProfileCode,
                   ea.recibido_en AS OccurredAt, ea.cerrado_en AS ClosedAt, ea.comunicacion_familiar_codigo AS DecisionCode,
                   escalation.motivo AS EscalationReason, escalation.escalado_en AS EscalatedAt,
                   family.tipo_codigo AS FamilyTypeCode, family.texto AS FamilyText, family.preparado_en AS FamilyPreparedAt
            {scopedEventsFrom}
               AND ea.residente_id = @ResidentId
            """, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value, ResidentId = residentId.Value },
            cancellationToken: ct))).ToList();
        var ids = events.Select(e => e.EventId).ToList();

        if (ids.Count > 0)
        {
            var closureIds = events.Where(e => e.OriginCode == "CAMBIO_AUXILIAR").Select(e => e.EventId).ToList();
            var areas = (closureIds.Count == 0 ? [] : await connection.QueryAsync<AreaCodeRow>(new CommandDefinition("""
                SELECT cierre_id AS ClosureId, area_codigo AS AreaCode FROM dbo.cierres_cotidianos_cambio_areas WHERE cierre_id IN @ClosureIds
                """, new { ClosureIds = closureIds }, cancellationToken: ct)))
                .GroupBy(a => a.ClosureId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<DailyChangeAreaCode>)g.Select(a => EnumCode.ParseCode<DailyChangeAreaCode>(a.AreaCode)).ToList());

            foreach (var e in events)
            {
                entries.Add(new TimelineEntry.EventRegistered(
                    Utc(e.OccurredAt), e.EventId, EnumCode.ParseCode<SystemProfile>(e.AuthorProfileCode),
                    EnumCode.ParseCode<ClinicalEventOrigin>(e.OriginCode), EnumCode.ParseCode<DailyChangeClassification>(e.ClassificationCode),
                    e.Observation, areas.GetValueOrDefault(e.EventId, [])));
                // Un escalado solo lo cierra Medicina y un evento propio de Medicina nunca pasa por Enfermería
                // (la misma deducción que Shared/_CierreEvento).
                var closedBy = e.EscalatedAt is not null || e.OriginCode == "EVENTO_MEDICINA" ? SystemProfile.Medicina : SystemProfile.Enfermeria;
                if (e.EscalatedAt is { } escalatedAt)
                {
                    entries.Add(new TimelineEntry.Escalated(Utc(escalatedAt), e.EventId, e.EscalationReason!));
                }
                if (e.FamilyPreparedAt is { } preparedAt)
                {
                    entries.Add(new TimelineEntry.FamilyCommunicationPrepared(
                        Utc(preparedAt), e.EventId, closedBy, EnumCode.ParseCode<FamilyCommunicationType>(e.FamilyTypeCode!), e.FamilyText!));
                }
                if (e.ClosedAt is { } closedAt)
                {
                    entries.Add(new TimelineEntry.EventClosed(
                        Utc(closedAt), e.EventId, closedBy, EnumCode.ParseCode<FamilyCommunicationDecision>(e.DecisionCode!)));
                }
            }

            var parameters = new { EventIds = ids };
            entries.AddRange((await connection.QueryAsync<AssessmentVersionRow>(new CommandDefinition($"""
                SELECT evento_id AS EventId, guardado_en AS SavedAt, hallazgos AS Findings, valoracion AS Assessment, actuaciones AS Actions,
                       comunicaciones AS Communications, resultado AS Outcome, {VitalSignColumns}, CAST(NULL AS NVARCHAR(500)) AS Reason, {Author("guardado_por_cuenta_id")} AS AuthorName
                  FROM dbo.valoraciones_enfermeria_versiones WHERE evento_id IN @EventIds
                """, parameters, cancellationToken: ct)))
                .Select(r => new TimelineEntry.NursingAssessmentSaved(Utc(r.SavedAt), r.EventId,
                    new NursingAssessmentContent(r.Findings, r.Assessment, r.Actions, r.Communications, r.Outcome, Vitals(r))) { AuthorName = r.AuthorName }));

            entries.AddRange((await connection.QueryAsync<AssessmentVersionRow>(new CommandDefinition($"""
                SELECT evento_id AS EventId, guardado_en AS SavedAt, hallazgos_exploracion AS Findings, valoracion AS Assessment,
                       actuaciones AS Actions, CAST(NULL AS NVARCHAR(2000)) AS Communications, CAST(NULL AS NVARCHAR(2000)) AS Outcome,
                       {VitalSignColumns}, CAST(NULL AS NVARCHAR(500)) AS Reason, {Author("guardado_por_cuenta_id")} AS AuthorName
                  FROM dbo.valoraciones_medicas_versiones WHERE evento_id IN @EventIds
                """, parameters, cancellationToken: ct)))
                .Select(r => new TimelineEntry.MedicalAssessmentSaved(Utc(r.SavedAt), r.EventId,
                    new MedicalAssessmentContent(r.Findings, r.Assessment, r.Actions, Vitals(r))) { AuthorName = r.AuthorName }));

            // COR-01/COR-02 (0020): las correcciones, con el contenido corregido y su motivo, y las rectificaciones.
            entries.AddRange((await connection.QueryAsync<AssessmentVersionRow>(new CommandDefinition($"""
                SELECT evento_id AS EventId, corregido_en AS SavedAt, hallazgos AS Findings, valoracion AS Assessment, actuaciones AS Actions,
                       comunicaciones AS Communications, resultado AS Outcome, {VitalSignColumns}, motivo AS Reason, {Author("corregido_por_cuenta_id")} AS AuthorName
                  FROM dbo.valoraciones_enfermeria_correcciones WHERE evento_id IN @EventIds
                """, parameters, cancellationToken: ct)))
                .Select(r => new TimelineEntry.NursingAssessmentCorrected(Utc(r.SavedAt), r.EventId,
                    new NursingAssessmentContent(r.Findings, r.Assessment, r.Actions, r.Communications, r.Outcome, Vitals(r)), r.Reason!) { AuthorName = r.AuthorName }));

            entries.AddRange((await connection.QueryAsync<AssessmentVersionRow>(new CommandDefinition($"""
                SELECT evento_id AS EventId, corregido_en AS SavedAt, hallazgos_exploracion AS Findings, valoracion AS Assessment,
                       actuaciones AS Actions, CAST(NULL AS NVARCHAR(2000)) AS Communications, CAST(NULL AS NVARCHAR(2000)) AS Outcome,
                       {VitalSignColumns}, motivo AS Reason, {Author("corregido_por_cuenta_id")} AS AuthorName
                  FROM dbo.valoraciones_medicas_correcciones WHERE evento_id IN @EventIds
                """, parameters, cancellationToken: ct)))
                .Select(r => new TimelineEntry.MedicalAssessmentCorrected(Utc(r.SavedAt), r.EventId,
                    new MedicalAssessmentContent(r.Findings, r.Assessment, r.Actions, Vitals(r)), r.Reason!) { AuthorName = r.AuthorName }));

            entries.AddRange((await connection.QueryAsync<TimelineRectificationRow>(new CommandDefinition($"""
                SELECT evento_id AS EventId, perfil_codigo AS ProfileCode, texto AS [Text], motivo AS Reason, registrado_en AS RecordedAt, {Author("registrado_por_cuenta_id")} AS AuthorName
                  FROM dbo.valoraciones_rectificaciones WHERE evento_id IN @EventIds
                """, parameters, cancellationToken: ct)))
                .Select(r => new TimelineEntry.AssessmentRectified(
                    Utc(r.RecordedAt), r.EventId, EnumCode.ParseCode<SystemProfile>(r.ProfileCode), r.Text, r.Reason) { AuthorName = r.AuthorName }));

            foreach (var i in await connection.QueryAsync<TimelineIndicationRow>(new CommandDefinition("""
                SELECT evento_id AS EventId, texto AS [Text], fecha_prevista AS DueDate, criterio AS Criterion,
                       informacion_adicional AS AdditionalInformation, emitida_en AS IssuedAt, leida_en AS ReadAt,
                       resuelta_en AS ResolvedAt, estado_codigo AS StatusCode, incidencia AS Incident
                  FROM dbo.indicaciones_medicas WHERE evento_id IN @EventIds
                """, parameters, cancellationToken: ct)))
            {
                entries.Add(new TimelineEntry.IndicationIssued(
                    Utc(i.IssuedAt), i.EventId, i.Text, i.DueDate is null ? null : DateOnly.FromDateTime(i.DueDate.Value), i.Criterion,
                    i.AdditionalInformation));
                if (i.ReadAt is { } readAt)
                {
                    entries.Add(new TimelineEntry.IndicationRead(Utc(readAt), i.EventId, i.Text));
                }
                if (i.ResolvedAt is { } resolvedAt)
                {
                    entries.Add(new TimelineEntry.IndicationResolved(
                        Utc(resolvedAt), i.EventId, i.Text, EnumCode.ParseCode<MedicalIndicationStatus>(i.StatusCode), i.Incident));
                }
            }

            entries.AddRange(await FollowUpEntriesAsync(connection, "seguimientos", "seguimiento_acciones", "indicaciones_continuidad",
                SystemProfile.Enfermeria, parameters, ct));
            entries.AddRange(await FollowUpEntriesAsync(connection, "seguimientos_medicos", "seguimiento_medico_acciones", "objetivo",
                SystemProfile.Medicina, parameters, ct));

            var protocols = (await connection.QueryAsync<TimelineProtocolRow>(new CommandDefinition("""
                SELECT id AS Id, evento_id AS EventId, perfil_codigo AS ProfileCode, nota_activacion AS Note, activado_en AS ActivatedAt
                  FROM dbo.protocolos_urgentes WHERE evento_id IN @EventIds
                """, parameters, cancellationToken: ct))).ToList();
            var protocolsById = protocols.ToDictionary(p => p.Id);
            entries.AddRange(protocols.Select(p => new TimelineEntry.UrgentProtocolActivated(
                Utc(p.ActivatedAt), p.EventId, EnumCode.ParseCode<SystemProfile>(p.ProfileCode), p.Note)));
            if (protocols.Count > 0)
            {
                entries.AddRange((await connection.QueryAsync<TimelineProtocolEntryRow>(new CommandDefinition("""
                    SELECT protocolo_id AS ProtocolId, tipo_codigo AS TypeCode, texto AS [Text], servicio_contactado AS Service,
                           contactado_en AS ContactedAt, registrado_en AS RecordedAt
                      FROM dbo.protocolo_urgente_registros WHERE protocolo_id IN @ProtocolIds
                    """, new { ProtocolIds = protocolsById.Keys.ToList() }, cancellationToken: ct)))
                    .Select(r =>
                    {
                        var protocol = protocolsById[r.ProtocolId];
                        return new TimelineEntry.UrgentProtocolEntryRecorded(
                            Utc(r.RecordedAt), protocol.EventId, EnumCode.ParseCode<SystemProfile>(protocol.ProfileCode),
                            EnumCode.ParseCode<UrgentProtocolEntryType>(r.TypeCode), r.Text, r.Service,
                            r.ContactedAt is null ? null : Utc(r.ContactedAt.Value));
                    }));
            }

            var referrals = (await connection.QueryAsync<TimelineReferralRow>(new CommandDefinition($"""
                SELECT evento_id AS EventId, perfil_codigo AS ProfileCode, motivo AS Reason, firmado_en AS SignedAt,
                       {Author("firmado_por_cuenta_id")} AS AuthorName
                  FROM dbo.informes_derivacion WHERE evento_id IN @EventIds
                """, parameters, cancellationToken: ct))).ToDictionary(r => r.EventId);
            entries.AddRange(referrals.Values.Select(r => new TimelineEntry.ReferralSigned(
                Utc(r.SignedAt), r.EventId, EnumCode.ParseCode<SystemProfile>(r.ProfileCode), r.Reason) { AuthorName = r.AuthorName }));
            entries.AddRange((await connection.QueryAsync<TimelineCallRow>(new CommandDefinition("""
                SELECT evento_id AS EventId, contacto AS Contact, llamado_en AS CalledAt, resultado_codigo AS ResultCode, nota AS Note,
                       registrado_en AS RecordedAt
                  FROM dbo.intentos_llamada_familia WHERE evento_id IN @EventIds
                """, parameters, cancellationToken: ct)))
                .Select(c => new TimelineEntry.FamilyCallAttempted(
                    Utc(c.RecordedAt), c.EventId, referrals.TryGetValue(c.EventId, out var r) ? EnumCode.ParseCode<SystemProfile>(r.ProfileCode) : null,
                    c.Contact, Utc(c.CalledAt), EnumCode.ParseCode<FamilyCallResult>(c.ResultCode), c.Note)));
        }

        var residentParameters = new { ResidentId = residentId.Value, CenterId = centerId.Value };
        entries.AddRange((await connection.QueryAsync<TimelineBaselineRow>(new CommandDefinition("""
            SELECT v.firmado_en AS SignedAt, v.firmado_por_perfil AS ProfileCode, v.numero_version AS VersionNumber,
                   v.motivo_codigo AS ReasonCode, b.puntuacion_total AS BarthelTotal
              FROM dbo.basales_version v
              JOIN dbo.basales_version_barthel b ON b.version_basal_id = v.id
             WHERE v.residente_id = @ResidentId AND v.centro_id = @CenterId
            """, residentParameters, cancellationToken: ct)))
            .Select(b => new TimelineEntry.BaselineSigned(
                Utc(b.SignedAt), EnumCode.ParseCode<SystemProfile>(b.ProfileCode), b.VersionNumber,
                EnumCode.ParseCode<BaselineReason>(b.ReasonCode), b.BarthelTotal)));
        entries.AddRange((await connection.QueryAsync<TimelineLocationRow>(new CommandDefinition("""
            SELECT i.vigente_desde AS StartedAt, unit.nombre_visible AS UnitName
              FROM dbo.intervalos_ubicacion_residente i
              JOIN dbo.unidades unit ON unit.id = i.unidad_id AND unit.centro_id = i.centro_id
             WHERE i.residente_id = @ResidentId AND i.centro_id = @CenterId
            """, residentParameters, cancellationToken: ct)))
            .Select(l => new TimelineEntry.LocationStarted(Utc(l.StartedAt), l.UnitName)));

        return entries.OrderByDescending(e => e.At).ToList();
    }

    /// <summary>Seguimiento de Enfermería o médico: su inicio y cada acción. Las tablas son las de 0010 y 0014,
    /// con la misma forma; notesColumn es indicaciones_continuidad o objetivo.</summary>
    private static async Task<IEnumerable<TimelineEntry>> FollowUpEntriesAsync(
        System.Data.IDbConnection connection, string followUpTable, string actionTable, string notesColumn, SystemProfile profile,
        object parameters, CancellationToken ct)
    {
        var starts = await connection.QueryAsync<TimelineFollowUpRow>(new CommandDefinition($"""
            SELECT evento_id AS EventId, fecha_prevista AS DueDate, criterio AS Criterion, {notesColumn} AS Notes, iniciado_en AS StartedAt
              FROM dbo.{followUpTable} WHERE evento_id IN @EventIds
            """, parameters, cancellationToken: ct));
        var actions = await connection.QueryAsync<TimelineFollowUpActionRow>(new CommandDefinition($"""
            SELECT s.evento_id AS EventId, a.tipo_codigo AS TypeCode, a.texto AS [Text], a.fecha_prevista AS DueDate,
                   a.criterio AS Criterion, a.equipo_entrante AS IncomingTeam, a.registrado_en AS RecordedAt
              FROM dbo.{actionTable} a
              JOIN dbo.{followUpTable} s ON s.id = a.seguimiento_id
             WHERE s.evento_id IN @EventIds
            """, parameters, cancellationToken: ct));
        return starts
            .Select(s => (TimelineEntry)new TimelineEntry.FollowUpStarted(
                Utc(s.StartedAt), s.EventId, profile, ToDate(s.DueDate), s.Criterion, s.Notes))
            .Concat(actions.Select(a => new TimelineEntry.FollowUpActionRecorded(
                Utc(a.RecordedAt), a.EventId, profile, EnumCode.ParseCode<FollowUpActionType>(a.TypeCode), a.Text, ToDate(a.DueDate),
                a.Criterion, a.IncomingTeam)));
    }

    private const string VitalSignColumns = """
        temperatura_celsius AS TemperatureCelsius, tension_sistolica_mmhg AS SystolicMmHg, tension_diastolica_mmhg AS DiastolicMmHg,
        frecuencia_cardiaca_lpm AS HeartRateBpm, frecuencia_respiratoria_rpm AS RespiratoryRateRpm, saturacion_o2_pct AS OxygenSaturationPct,
        soporte_respiratorio_codigo AS RespiratorySupportCode, flujo_o2_lpm AS OxygenFlowLpm, glucemia_mg_dl AS GlucoseMgDl,
        otra_constante_nombre AS OtherName, otra_constante_valor AS OtherValue, otra_constante_unidad AS OtherUnit
        """;

    private static VitalSigns Vitals(AssessmentVersionRow r) => new(
        r.TemperatureCelsius, r.SystolicMmHg, r.DiastolicMmHg, r.HeartRateBpm, r.RespiratoryRateRpm, r.OxygenSaturationPct,
        r.RespiratorySupportCode is null ? null : EnumCode.ParseCode<RespiratorySupportCode>(r.RespiratorySupportCode),
        r.OxygenFlowLpm, r.GlucoseMgDl, r.OtherName, r.OtherValue, r.OtherUnit);

    private static DateTimeOffset Utc(DateTime value) => new(value, TimeSpan.Zero);

    private static DateOnly? ToDate(DateTime? value) => value is null ? null : DateOnly.FromDateTime(value.Value);

    private sealed record TimelineEventRow(
        Guid EventId, string OriginCode, string ClassificationCode, string? Observation, string AuthorProfileCode, DateTime OccurredAt,
        DateTime? ClosedAt, string? DecisionCode, string? EscalationReason, DateTime? EscalatedAt, string? FamilyTypeCode,
        string? FamilyText, DateTime? FamilyPreparedAt);

    private sealed record AssessmentVersionRow(
        Guid EventId, DateTime SavedAt, string? Findings, string? Assessment, string? Actions, string? Communications, string? Outcome,
        decimal? TemperatureCelsius, short? SystolicMmHg, short? DiastolicMmHg, short? HeartRateBpm, short? RespiratoryRateRpm,
        short? OxygenSaturationPct, string? RespiratorySupportCode, decimal? OxygenFlowLpm, short? GlucoseMgDl,
        string? OtherName, string? OtherValue, string? OtherUnit, string? Reason, string? AuthorName);

    private sealed record TimelineRectificationRow(Guid EventId, string ProfileCode, string Text, string Reason, DateTime RecordedAt, string? AuthorName);

    private sealed record TimelineIndicationRow(
        Guid EventId, string Text, DateTime? DueDate, string? Criterion, string? AdditionalInformation, DateTime IssuedAt,
        DateTime? ReadAt, DateTime? ResolvedAt, string StatusCode, string? Incident);

    private sealed record TimelineFollowUpRow(Guid EventId, DateTime? DueDate, string? Criterion, string? Notes, DateTime StartedAt);

    private sealed record TimelineFollowUpActionRow(
        Guid EventId, string TypeCode, string? Text, DateTime? DueDate, string? Criterion, string? IncomingTeam, DateTime RecordedAt);

    private sealed record TimelineProtocolRow(Guid Id, Guid EventId, string ProfileCode, string? Note, DateTime ActivatedAt);

    private sealed record TimelineProtocolEntryRow(
        Guid ProtocolId, string TypeCode, string? Text, string? Service, DateTime? ContactedAt, DateTime RecordedAt);

    private sealed record TimelineReferralRow(Guid EventId, string ProfileCode, string Reason, DateTime SignedAt, string? AuthorName);

    private sealed record TimelineCallRow(Guid EventId, string Contact, DateTime CalledAt, string ResultCode, string? Note, DateTime RecordedAt);

    private sealed record TimelineBaselineRow(DateTime SignedAt, string ProfileCode, int VersionNumber, string ReasonCode, int BarthelTotal);

    private sealed record TimelineLocationRow(DateTime StartedAt, string? UnitName);
}
