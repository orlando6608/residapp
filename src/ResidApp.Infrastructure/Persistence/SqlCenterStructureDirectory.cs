using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>ADM-05: las unidades concedidas, sin revocar, al ámbito de Administración de quien gestiona. Una unidad del centro
/// que no esté concedida a ese ámbito no se ve (deny-by-default, como las listas de Usuarios).</summary>
public sealed class SqlCenterStructureDirectory(SqlConnectionFactory connections) : ICenterStructureDirectory
{
    public async Task<IReadOnlyList<StructureUnit>> ListUnitsAsync(AccountAdministrationAccess access, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<UnitRow>(new CommandDefinition("""
            SELECT unit.id AS UnitId, unit.codigo AS Code, unit.nombre_visible AS Name,
                   CAST(CASE WHEN unit.estado = 'ACTIVE' THEN 1 ELSE 0 END AS BIT) AS Active,
                   (SELECT COUNT(*) FROM dbo.intervalos_ubicacion_residente location
                     WHERE location.centro_id = unit.centro_id AND location.unidad_id = unit.id AND location.vigente_hasta IS NULL) AS CurrentResidents
              FROM dbo.ambitos_perfil profile
              JOIN dbo.centros center ON center.id = profile.centro_id AND center.estado = 'ACTIVE'
              JOIN dbo.cuentas account ON account.id = profile.cuenta_id AND account.estado = 'ACTIVE'
              JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = profile.id
                   AND unit_scope.centro_id = profile.centro_id AND unit_scope.revocado_en IS NULL
              JOIN dbo.unidades unit ON unit.id = unit_scope.unidad_id AND unit.centro_id = unit_scope.centro_id
             WHERE profile.id = @ProfileScopeId AND profile.centro_id = @CenterId AND profile.cuenta_id = @AccountId
               AND profile.perfil_codigo = 'ADMINISTRACION' AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
             ORDER BY unit.estado, unit.nombre_visible, unit.codigo
            """, new { access.ProfileScopeId, CenterId = access.CenterId.Value, AccountId = access.AccountId.Value }, cancellationToken: ct));
        return rows.Select(row => new StructureUnit(UnitId.From(row.UnitId), row.Code, row.Name, row.Active, row.CurrentResidents)).ToList();
    }

    private sealed record UnitRow(Guid UnitId, string Code, string Name, bool Active, int CurrentResidents);
}
