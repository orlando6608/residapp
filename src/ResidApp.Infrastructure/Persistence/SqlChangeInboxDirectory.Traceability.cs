using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>DIR-14: trazabilidad clínica de un residente para Dirección Clínica, leída de la auditoría (de solo inserción). No autoriza
/// nada: solo se llama tras la lectura auditada del residente (ReadDirectionBaseline); aun así repite en SQL el ámbito de Dirección
/// activo del centro y, en los hitos con unidad, que la unidad esté concedida a ese ámbito. Solo las acciones de
/// ClinicalTraceability.ActionCodes, y nunca la justificación de las lecturas ni ningún texto.</summary>
public sealed partial class SqlChangeInboxDirectory
{
    public async Task<ClinicalTraceabilityPage> ListDirectionTraceabilityAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = (await connection.QueryAsync<TraceabilityRow>(new CommandDefinition($"""
            SELECT TOP ({ClinicalTraceabilityPage.MaxEntries + 1})
                   ev.ocurrido_en AS OccurredAt, actor.nombre_visible AS ActorName, ev.perfil_activo AS ActorProfileCode,
                   unit.nombre_visible AS UnitName, ev.accion_codigo AS Action, ev.tipo_recurso AS ResourceType,
                   ev.proposito_codigo AS Purpose
              FROM dbo.eventos_auditoria ev
              JOIN dbo.ambitos_perfil profile ON profile.id = @ProfileScopeId AND profile.centro_id = @CenterId
                   AND profile.perfil_codigo = 'DIRECCION_CLINICA' AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
              JOIN dbo.cuentas actor ON actor.id = ev.cuenta_id
              LEFT JOIN dbo.unidades unit ON unit.centro_id = ev.centro_id AND unit.id = ev.unidad_id
             WHERE ev.centro_id = @CenterId AND ev.residente_id = @ResidentId AND ev.accion_codigo IN @Actions
               AND (ev.unidad_id IS NULL OR EXISTS (
                        SELECT 1 FROM dbo.ambitos_perfil_unidad unit_scope
                         WHERE unit_scope.ambito_perfil_id = profile.id AND unit_scope.centro_id = profile.centro_id
                           AND unit_scope.unidad_id = ev.unidad_id AND unit_scope.revocado_en IS NULL))
             ORDER BY ev.ocurrido_en DESC, ev.id
            """, new
        {
            ProfileScopeId = profileScopeId, CenterId = centerId.Value, ResidentId = residentId.Value,
            Actions = ClinicalTraceability.ActionCodes,
        }, cancellationToken: ct))).ToList();

        return new ClinicalTraceabilityPage(
            rows.Take(ClinicalTraceabilityPage.MaxEntries).Select(row => new ClinicalTraceabilityEntry(
                new DateTimeOffset(DateTime.SpecifyKind(row.OccurredAt, DateTimeKind.Utc)), row.ActorName,
                EnumCode.ParseCode<SystemProfile>(row.ActorProfileCode), row.UnitName, row.Action, row.ResourceType, row.Purpose)).ToList(),
            rows.Count > ClinicalTraceabilityPage.MaxEntries);
    }

    private sealed record TraceabilityRow(
        DateTime OccurredAt, string? ActorName, string ActorProfileCode, string? UnitName, string Action, string ResourceType, string? Purpose);
}
