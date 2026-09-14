using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Traduce el listado "residentes del ámbito" del vertical Enfermería (ENF-17). El predicado de residente
/// replica exactamente el mismo criterio de "ámbito por defecto o restringido" que
/// SqlAuthorizationEvidenceProvider aplica para un único residente: sin ninguna fila en
/// ambitos_perfil_residente para este ámbito, todos los residentes ubicados en unidades concedidas son
/// visibles; si existe alguna, solo esos residentes lo son.
/// </summary>
public sealed class SqlEnfermeriaResidentDirectory(SqlConnectionFactory connections) : IEnfermeriaResidentDirectory
{
    public async Task<IReadOnlyList<ScopeResidentSummary>> ListAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition("""
            SELECT resident.id AS ResidentId, resident.nombre_visible AS DisplayName, unit.id AS UnitId, unit.nombre_visible AS UnitName,
                   CAST(CASE WHEN current_baseline.residente_id IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS TieneBasalVigente
              FROM dbo.ambitos_perfil profile
              JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = profile.id
                   AND unit_scope.centro_id = profile.centro_id AND unit_scope.revocado_en IS NULL
              JOIN dbo.unidades unit ON unit.id = unit_scope.unidad_id AND unit.centro_id = profile.centro_id AND unit.estado = 'ACTIVE'
              JOIN dbo.residentes resident ON resident.centro_id = profile.centro_id AND resident.estado = 'ACTIVE'
              JOIN dbo.intervalos_ubicacion_residente location ON location.residente_id = resident.id
                   AND location.centro_id = profile.centro_id AND location.unidad_id = unit.id AND location.vigente_hasta IS NULL
              LEFT JOIN dbo.ambitos_perfil_residente resident_scope ON resident_scope.ambito_perfil_id = profile.id
                   AND resident_scope.centro_id = profile.centro_id AND resident_scope.residente_id = resident.id
                   AND resident_scope.revocado_en IS NULL
              LEFT JOIN dbo.basales_vigentes_residente current_baseline ON current_baseline.residente_id = resident.id
                   AND current_baseline.centro_id = profile.centro_id
             WHERE profile.id = @ProfileScopeId AND profile.centro_id = @CenterId
               AND profile.perfil_codigo = 'ENFERMERIA' AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
               AND (resident_scope.id IS NOT NULL OR NOT EXISTS (
                   SELECT 1 FROM dbo.ambitos_perfil_residente restriction
                    WHERE restriction.ambito_perfil_id = profile.id AND restriction.centro_id = profile.centro_id))
             ORDER BY resident.nombre_visible
            """, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value }, cancellationToken: ct));

        return rows
            .Select(row => new ScopeResidentSummary(
                ResidentId.From(row.ResidentId), row.DisplayName, UnitId.From(row.UnitId), row.UnitName, row.TieneBasalVigente))
            .ToList();
    }

    private sealed record Row(Guid ResidentId, string DisplayName, Guid UnitId, string? UnitName, bool TieneBasalVigente);
}
