using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>DIR-01 a DIR-04 y DIR-17: supervisión operativa de Dirección Clínica. Solo lee columnas de identificación,
/// estado y fecha: nunca observaciones, valoraciones, indicaciones ni ningún otro texto clínico. La regla de ámbito va
/// en la propia consulta (ámbito activo de DIRECCION_CLINICA del centro, sus unidades y, si las restringe, sus
/// residentes), así que un ámbito de otro perfil o un episodio ajeno devuelve vacío. Es el gemelo de
/// SqlChangeInboxDirectory.ScopedEventsFrom para Dirección, que no puede reutilizarlo porque fija Enfermería y Medicina.</summary>
public sealed class SqlSupervisionDirectory(SqlConnectionFactory connections) : ISupervisionDirectory
{
    private const string ScopedEpisodesSelect = """
        SELECT ea.id AS EventId, ea.residente_id AS ResidentId, resident.nombre_visible AS ResidentDisplayName,
               ea.unidad_id AS UnitId, unit.nombre_visible AS UnitName, ea.origen_codigo AS OriginCode,
               ea.clasificacion_codigo AS ClassificationCode, ea.estado_codigo AS StatusCode, ea.recibido_en AS ReceivedAt,
               CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.escalados_medicina escalation WHERE escalation.evento_id = ea.id)
                    THEN 1 ELSE 0 END AS BIT) AS Escalated,
               CAST(CASE WHEN ea.estado_codigo IN ('EN_SEGUIMIENTO', 'EN_SEGUIMIENTO_MEDICO') THEN 1 ELSE 0 END AS BIT) AS HasFollowUp,
               COALESCE(nurse_follow_up.DueDate, medical_follow_up.DueDate) AS FollowUpDue,
               CAST(CASE WHEN ea.estado_codigo IN ('PROTOCOLO_URGENTE', 'PROTOCOLO_URGENTE_MEDICO') THEN 1 ELSE 0 END AS BIT)
                   AS HasUrgentProtocol,
               (SELECT COUNT(*) FROM dbo.indicaciones_medicas i
                 WHERE i.evento_id = ea.id AND i.estado_codigo IN ('PENDIENTE_LECTURA', 'LEIDA')) AS IndicationsPending,
               (SELECT COUNT(*) FROM dbo.indicaciones_medicas i
                 WHERE i.evento_id = ea.id AND i.estado_codigo = 'NO_REALIZADA') AS IndicationsNotDone
          FROM dbo.eventos_asistenciales ea
          JOIN dbo.ambitos_perfil profile ON profile.id = @ProfileScopeId AND profile.centro_id = @CenterId
               AND profile.perfil_codigo = 'DIRECCION_CLINICA' AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
          JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = profile.id
               AND unit_scope.centro_id = profile.centro_id AND unit_scope.unidad_id = ea.unidad_id AND unit_scope.revocado_en IS NULL
          JOIN dbo.unidades unit ON unit.id = ea.unidad_id AND unit.centro_id = profile.centro_id
          JOIN dbo.residentes resident ON resident.id = ea.residente_id AND resident.centro_id = profile.centro_id
          LEFT JOIN dbo.ambitos_perfil_residente resident_scope ON resident_scope.ambito_perfil_id = profile.id
               AND resident_scope.centro_id = profile.centro_id AND resident_scope.residente_id = ea.residente_id
               AND resident_scope.revocado_en IS NULL
          OUTER APPLY (SELECT CASE WHEN reschedule.id IS NULL THEN seg.fecha_prevista ELSE reschedule.fecha_prevista END AS DueDate
                         FROM dbo.seguimientos seg
                         OUTER APPLY (SELECT TOP 1 a.id, a.fecha_prevista FROM dbo.seguimiento_acciones a
                                       WHERE a.seguimiento_id = seg.id AND a.tipo_codigo = 'REPROGRAMACION'
                                       ORDER BY a.registrado_en DESC) reschedule
                        WHERE seg.evento_id = ea.id AND ea.estado_codigo = 'EN_SEGUIMIENTO') nurse_follow_up
          OUTER APPLY (SELECT CASE WHEN reschedule.id IS NULL THEN seg.fecha_prevista ELSE reschedule.fecha_prevista END AS DueDate
                         FROM dbo.seguimientos_medicos seg
                         OUTER APPLY (SELECT TOP 1 a.id, a.fecha_prevista FROM dbo.seguimiento_medico_acciones a
                                       WHERE a.seguimiento_id = seg.id AND a.tipo_codigo = 'REPROGRAMACION'
                                       ORDER BY a.registrado_en DESC) reschedule
                        WHERE seg.evento_id = ea.id AND ea.estado_codigo = 'EN_SEGUIMIENTO_MEDICO') medical_follow_up
         WHERE ea.centro_id = @CenterId AND ea.estado_codigo <> 'CERRADO'
           AND (resident_scope.id IS NOT NULL OR NOT EXISTS (
               SELECT 1 FROM dbo.ambitos_perfil_residente restriction
                WHERE restriction.ambito_perfil_id = profile.id AND restriction.centro_id = profile.centro_id))
        """;

    public async Task<IReadOnlyList<SupervisionEpisode>> ListOpenEpisodesAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<EpisodeRow>(new CommandDefinition(
            ScopedEpisodesSelect, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value }, cancellationToken: ct));
        return rows.Select(ToEpisode).OrderBy(e => e.ReceivedAt).ToList();
    }

    public async Task<SupervisionEpisodeDetail?> FindEpisodeAsync(
        Guid profileScopeId, CenterId centerId, Guid eventId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var row = (await connection.QueryAsync<EpisodeRow>(new CommandDefinition(
            ScopedEpisodesSelect + " AND ea.id = @EventId",
            new { ProfileScopeId = profileScopeId, CenterId = centerId.Value, EventId = eventId }, cancellationToken: ct)))
            .SingleOrDefault();
        if (row is null)
        {
            return null;
        }

        // El ámbito ya está comprobado con la consulta anterior; desde aquí solo se leen hitos de ese evento.
        var parameters = new { EventId = eventId };
        var milestones = new List<SupervisionMilestone>();
        var origin = EnumCode.ParseCode<ClinicalEventOrigin>(row.OriginCode);

        var started = await connection.QuerySingleAsync<StartRow>(new CommandDefinition("""
            SELECT valoracion_iniciada_en AS AssessmentStartedAt, valoracion_medica_iniciada_en AS MedicalStartedAt
              FROM dbo.eventos_asistenciales WHERE id = @EventId
            """, parameters, cancellationToken: ct));
        milestones.Add(new(SupervisionMilestoneKind.Registrado, origin switch
        {
            ClinicalEventOrigin.CambioAuxiliar => SystemProfile.Auxiliar,
            ClinicalEventOrigin.EventoEnfermeria => SystemProfile.Enfermeria,
            _ => SystemProfile.Medicina,
        }, Utc(row.ReceivedAt)));
        if (started.AssessmentStartedAt is { } assessmentStarted)
        {
            milestones.Add(new(SupervisionMilestoneKind.ValoracionIniciada, SystemProfile.Enfermeria, Utc(assessmentStarted)));
        }
        if (started.MedicalStartedAt is { } medicalStarted)
        {
            milestones.Add(new(SupervisionMilestoneKind.ValoracionMedicaIniciada, SystemProfile.Medicina, Utc(medicalStarted)));
        }

        foreach (var (kind, profile, at) in await QueryMilestonesAsync(connection, parameters, ct))
        {
            milestones.Add(new(kind, profile, Utc(at)));
        }

        return new SupervisionEpisodeDetail(ToEpisode(row), milestones.OrderBy(m => m.At).ToList());
    }

    private static async Task<IReadOnlyList<(SupervisionMilestoneKind Kind, SystemProfile? Profile, DateTime At)>> QueryMilestonesAsync(
        Microsoft.Data.SqlClient.SqlConnection connection, object parameters, CancellationToken ct)
    {
        var rows = await connection.QueryAsync<MilestoneRow>(new CommandDefinition("""
            SELECT 'ESCALADO' AS Kind, 'ENFERMERIA' AS ProfileCode, escalado_en AS At FROM dbo.escalados_medicina WHERE evento_id = @EventId
            UNION ALL
            SELECT 'INDICACION', 'MEDICINA', emitida_en FROM dbo.indicaciones_medicas WHERE evento_id = @EventId
            UNION ALL
            SELECT 'SEGUIMIENTO', 'ENFERMERIA', iniciado_en FROM dbo.seguimientos WHERE evento_id = @EventId
            UNION ALL
            SELECT 'SEGUIMIENTO', 'MEDICINA', iniciado_en FROM dbo.seguimientos_medicos WHERE evento_id = @EventId
            UNION ALL
            SELECT 'PROTOCOLO', perfil_codigo, activado_en FROM dbo.protocolos_urgentes WHERE evento_id = @EventId
            UNION ALL
            SELECT 'INFORME', perfil_codigo, firmado_en FROM dbo.informes_derivacion WHERE evento_id = @EventId
            """, parameters, cancellationToken: ct));
        return rows.Select(r => (
            r.Kind switch
            {
                "ESCALADO" => SupervisionMilestoneKind.Escalado,
                "INDICACION" => SupervisionMilestoneKind.IndicacionEmitida,
                "SEGUIMIENTO" => SupervisionMilestoneKind.SeguimientoIniciado,
                "PROTOCOLO" => SupervisionMilestoneKind.ProtocoloUrgenteActivado,
                _ => SupervisionMilestoneKind.InformeDerivacionFirmado,
            },
            (SystemProfile?)EnumCode.ParseCode<SystemProfile>(r.ProfileCode), r.At)).ToList();
    }

    public async Task<SupervisionScopeInfo?> FindScopeAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var parameters = new { ProfileScopeId = profileScopeId, CenterId = centerId.Value };
        var center = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
            SELECT center.nombre_visible
              FROM dbo.ambitos_perfil profile JOIN dbo.centros center ON center.id = profile.centro_id
             WHERE profile.id = @ProfileScopeId AND profile.centro_id = @CenterId AND profile.perfil_codigo = 'DIRECCION_CLINICA'
               AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
            """, parameters, cancellationToken: ct));
        if (center is null)
        {
            return null;
        }

        var units = (await connection.QueryAsync<UnitRow>(new CommandDefinition("""
            SELECT unit.id AS Id, unit.nombre_visible AS Name
              FROM dbo.ambitos_perfil_unidad unit_scope
              JOIN dbo.unidades unit ON unit.id = unit_scope.unidad_id AND unit.centro_id = unit_scope.centro_id
             WHERE unit_scope.ambito_perfil_id = @ProfileScopeId AND unit_scope.centro_id = @CenterId AND unit_scope.revocado_en IS NULL
             ORDER BY unit.nombre_visible
            """, parameters, cancellationToken: ct))).Select(u => (UnitId.From(u.Id), u.Name)).ToList();
        var restricted = await connection.QuerySingleAsync<bool>(new CommandDefinition("""
            SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.ambitos_perfil_residente WHERE ambito_perfil_id = @ProfileScopeId
                AND centro_id = @CenterId AND revocado_en IS NULL) THEN 1 ELSE 0 END AS BIT)
            """, parameters, cancellationToken: ct));
        var permissions = (await connection.QueryAsync<string>(new CommandDefinition("""
            SELECT permiso_codigo FROM dbo.permisos_perfil
             WHERE ambito_perfil_id = @ProfileScopeId AND centro_id = @CenterId AND revocado_en IS NULL ORDER BY permiso_codigo
            """, parameters, cancellationToken: ct))).ToList();
        return new SupervisionScopeInfo(center, units, restricted, permissions);
    }

    private static SupervisionEpisode ToEpisode(EpisodeRow r) => new(
        r.EventId, ResidentId.From(r.ResidentId), r.ResidentDisplayName, UnitId.From(r.UnitId), r.UnitName,
        EnumCode.ParseCode<ClinicalEventOrigin>(r.OriginCode), EnumCode.ParseCode<DailyChangeClassification>(r.ClassificationCode),
        EnumCode.ParseCode<ClinicalEventStatus>(r.StatusCode), Utc(r.ReceivedAt), r.Escalated, r.HasFollowUp,
        r.FollowUpDue is null ? null : DateOnly.FromDateTime(r.FollowUpDue.Value), r.HasUrgentProtocol,
        r.IndicationsPending, r.IndicationsNotDone);

    private static DateTimeOffset Utc(DateTime value) => new(value, TimeSpan.Zero);

    private sealed record EpisodeRow(
        Guid EventId, Guid ResidentId, string ResidentDisplayName, Guid UnitId, string UnitName, string OriginCode,
        string ClassificationCode, string StatusCode, DateTime ReceivedAt, bool Escalated, bool HasFollowUp, DateTime? FollowUpDue,
        bool HasUrgentProtocol, int IndicationsPending, int IndicationsNotDone);

    private sealed record StartRow(DateTime? AssessmentStartedAt, DateTime? MedicalStartedAt);

    private sealed record MilestoneRow(string Kind, string ProfileCode, DateTime At);

    private sealed record UnitRow(Guid Id, string Name);
}
