using System.Data;
using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Supervision;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// DIR-11: las siete consultas de los hitos del proceso, compartidas por Dirección (todo su ámbito) y por Enfermería y Medicina (los avisos
/// de su equipo). Cada una trabaja sobre los eventos de un ámbito: <c>scopedEvents</c> es el fragmento «FROM … WHERE …» de ese ámbito con los
/// alias ea, unit y resident, que ya trae la regla de ámbito. Solo leen fechas, la unidad, el residente y si el evento es prioritario.
/// Parámetros: @ProfileScopeId, @CenterId, @From y @To (UTC, [From, To)).
/// </summary>
internal static class ProcessMilestoneQueries
{
    public static async Task<IReadOnlyList<MilestoneFact>> ListAsync(
        IDbConnection connection, string scopedEvents, Guid profileScopeId, CenterId centerId, DateTime from, DateTime toExclusive,
        CancellationToken ct)
    {
        var parameters = new { ProfileScopeId = profileScopeId, CenterId = centerId.Value, From = from, To = toExclusive };
        var scoped = $"""
            (SELECT DISTINCT ea.id AS EventId, ea.residente_id AS ResidentId, resident.nombre_visible AS ResidentName, ea.unidad_id AS UnitId,
                    unit.nombre_visible AS UnitName, CAST(CASE WHEN ea.clasificacion_codigo = 'PRIORITARIO' THEN 1 ELSE 0 END AS BIT) AS Priority,
                    CAST(CASE WHEN ea.estado_codigo = 'CERRADO' THEN 1 ELSE 0 END AS BIT) AS EpisodeClosed,
                    ea.origen_codigo AS OriginCode, ea.recibido_en AS ReceivedAt, ea.valoracion_iniciada_en AS AssessmentStartedAt,
                    ea.valoracion_medica_iniciada_en AS MedicalStartedAt
             {scopedEvents}) s
            """;
        const string columns = "s.EventId, s.ResidentId, s.ResidentName, s.UnitId, s.UnitName, s.Priority, s.EpisodeClosed,";
        var queries = new (ProcessMilestone Milestone, string Sql)[]
        {
            (ProcessMilestone.ValoracionEnfermeria, $"""
                SELECT {columns} s.ReceivedAt AS StartedAt, s.AssessmentStartedAt AS EndedAt, 'ENFERMERIA' AS ResponsibleCode
                  FROM {scoped}
                 WHERE s.OriginCode = 'CAMBIO_AUXILIAR' AND s.ReceivedAt >= @From AND s.ReceivedAt < @To
                """),
            (ProcessMilestone.ValoracionMedica, $"""
                SELECT {columns} esc.At AS StartedAt, s.MedicalStartedAt AS EndedAt, 'MEDICINA' AS ResponsibleCode
                  FROM {scoped}
                 CROSS APPLY (SELECT MIN(x.escalado_en) AS At FROM dbo.escalados_medicina x WHERE x.evento_id = s.EventId) esc
                 WHERE esc.At >= @From AND esc.At < @To
                """),
            (ProcessMilestone.RecepcionTransferencia, $"""
                SELECT {columns} transfer.registrado_en AS StartedAt,
                       (SELECT MIN(r.registrado_en) FROM dbo.seguimiento_acciones r WHERE r.transferencia_id = transfer.id) AS EndedAt,
                       'ENFERMERIA' AS ResponsibleCode
                  FROM {scoped}
                  JOIN dbo.seguimientos follow_up ON follow_up.evento_id = s.EventId
                  JOIN dbo.seguimiento_acciones transfer ON transfer.seguimiento_id = follow_up.id AND transfer.tipo_codigo = 'TRANSFERENCIA'
                 WHERE transfer.registrado_en >= @From AND transfer.registrado_en < @To
                """),
            (ProcessMilestone.InformeDerivacion, $"""
                SELECT {columns} protocol.activado_en AS StartedAt, report.firmado_en AS EndedAt, protocol.perfil_codigo AS ResponsibleCode
                  FROM {scoped}
                  JOIN dbo.protocolos_urgentes protocol ON protocol.evento_id = s.EventId
                  LEFT JOIN dbo.informes_derivacion report ON report.evento_id = s.EventId
                 WHERE protocol.activado_en >= @From AND protocol.activado_en < @To
                """),
            (ProcessMilestone.LlamadaFamilia, $"""
                SELECT {columns} protocol.activado_en AS StartedAt,
                       (SELECT MIN(c.llamado_en) FROM dbo.intentos_llamada_familia c WHERE c.evento_id = s.EventId) AS EndedAt,
                       protocol.perfil_codigo AS ResponsibleCode
                  FROM {scoped}
                  JOIN dbo.protocolos_urgentes protocol ON protocol.evento_id = s.EventId
                 WHERE protocol.activado_en >= @From AND protocol.activado_en < @To
                """),
            (ProcessMilestone.LecturaIndicacion, $"""
                SELECT {columns} indication.emitida_en AS StartedAt, indication.leida_en AS EndedAt, 'ENFERMERIA' AS ResponsibleCode
                  FROM {scoped}
                  JOIN dbo.indicaciones_medicas indication ON indication.evento_id = s.EventId
                 WHERE indication.emitida_en >= @From AND indication.emitida_en < @To
                """),
            (ProcessMilestone.RealizacionIndicacion, $"""
                SELECT {columns} indication.emitida_en AS StartedAt, indication.resuelta_en AS EndedAt, 'ENFERMERIA' AS ResponsibleCode
                  FROM {scoped}
                  JOIN dbo.indicaciones_medicas indication ON indication.evento_id = s.EventId
                 WHERE indication.emitida_en >= @From AND indication.emitida_en < @To
                """),
        };
        var facts = new List<MilestoneFact>();
        foreach (var (milestone, sql) in queries)
        {
            var rows = await connection.QueryAsync<Row>(new CommandDefinition(sql, parameters, cancellationToken: ct));
            facts.AddRange(rows.Select(r => new MilestoneFact(
                milestone, r.EventId, ResidentId.From(r.ResidentId), r.ResidentName, UnitId.From(r.UnitId), r.UnitName, r.Priority,
                r.StartedAt, r.EndedAt, r.EpisodeClosed, EnumCode.ParseCode<SystemProfile>(r.ResponsibleCode))));
        }

        return facts;
    }

    private sealed record Row(
        Guid EventId, Guid ResidentId, string ResidentName, Guid UnitId, string UnitName, bool Priority, bool EpisodeClosed, DateTime StartedAt,
        DateTime? EndedAt, string ResponsibleCode);
}
