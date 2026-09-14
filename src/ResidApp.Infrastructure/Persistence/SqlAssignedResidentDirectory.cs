using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Traduce el listado de "residentes asignados" del vertical Auxiliar (AUX-01). El predicado replica
/// exactamente el mismo criterio residente+ubicación+unidad que SqlAuthorizationEvidenceProvider aplica
/// para un único residente (ambitos_perfil_residente vigente + ubicación actual dentro de una unidad
/// concedida al ámbito), para que un residente nunca aparezca en esta lista sin poder abrirse luego en
/// AUX-02/AUX-03, ni al revés.
/// </summary>
public sealed class SqlAssignedResidentDirectory(SqlConnectionFactory connections) : IAssignedResidentDirectory
{
    public async Task<IReadOnlyList<AssignedResidentSummary>> ListAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition("""
            SELECT resident.id AS ResidentId, resident.nombre_visible AS DisplayName, unit.nombre_visible AS UnitName,
                   CAST(CASE WHEN current_baseline.residente_id IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS TieneBasalVigente
              FROM dbo.ambitos_perfil profile
              JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = profile.id
                   AND unit_scope.centro_id = profile.centro_id AND unit_scope.revocado_en IS NULL
              JOIN dbo.unidades unit ON unit.id = unit_scope.unidad_id AND unit.centro_id = profile.centro_id AND unit.estado = 'ACTIVE'
              JOIN dbo.ambitos_perfil_residente resident_scope ON resident_scope.ambito_perfil_id = profile.id
                   AND resident_scope.centro_id = profile.centro_id AND resident_scope.revocado_en IS NULL
              JOIN dbo.residentes resident ON resident.id = resident_scope.residente_id
                   AND resident.centro_id = profile.centro_id AND resident.estado = 'ACTIVE'
              JOIN dbo.intervalos_ubicacion_residente location ON location.residente_id = resident.id
                   AND location.centro_id = profile.centro_id AND location.unidad_id = unit.id AND location.vigente_hasta IS NULL
              LEFT JOIN dbo.basales_vigentes_residente current_baseline ON current_baseline.residente_id = resident.id
                   AND current_baseline.centro_id = profile.centro_id
             WHERE profile.id = @ProfileScopeId AND profile.centro_id = @CenterId
               AND profile.perfil_codigo = 'AUXILIAR' AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
             ORDER BY resident.nombre_visible
            """, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value }, cancellationToken: ct));

        return rows
            .Select(row => new AssignedResidentSummary(
                ResidentId.From(row.ResidentId), row.DisplayName, row.UnitName, row.TieneBasalVigente))
            .ToList();
    }

    private sealed record Row(Guid ResidentId, string DisplayName, string? UnitName, bool TieneBasalVigente);
}
