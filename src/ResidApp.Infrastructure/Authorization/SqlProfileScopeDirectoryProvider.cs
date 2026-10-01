using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Infrastructure.Persistence;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Authorization;

/// <summary>Lee los ambitos_perfil ACTIVE de una cuenta (dbo.cuentas ⋈ dbo.ambitos_perfil ⋈ dbo.centros),
/// sin unir con unidades/residentes: esta pantalla solo resuelve el par (ProfileScopeId, CenterId) que
/// alimenta AuthorizationSelection, no el ámbito completo de autorización. ListUnitsAsync sí une con las unidades del
/// ámbito, para el selector de unidad del alta de residente.</summary>
public sealed class SqlProfileScopeDirectoryProvider(SqlConnectionFactory connections) : IProfileScopeDirectoryProvider
{
    public async Task<IReadOnlyList<ActiveProfileScope>> ListActiveAsync(string externalSubject, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition("""
            SELECT profile.id AS ProfileScopeId, account.id AS AccountId, profile.centro_id AS CenterId,
                   center.nombre_visible AS CenterName, profile.perfil_codigo AS Profile, account.nombre_visible AS AccountDisplayName
              FROM dbo.cuentas account
              JOIN dbo.ambitos_perfil profile ON profile.cuenta_id = account.id
                  AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
              JOIN dbo.centros center ON center.id = profile.centro_id AND center.estado = 'ACTIVE'
             WHERE account.sujeto_externo = @ExternalSubject AND account.estado = 'ACTIVE'
             ORDER BY center.nombre_visible, profile.perfil_codigo
            """, new { ExternalSubject = externalSubject }, cancellationToken: ct));

        return rows
            .Select(row => new ActiveProfileScope(
                row.ProfileScopeId, AccountId.From(row.AccountId), CenterId.From(row.CenterId), row.CenterName,
                EnumCode.ParseCode<SystemProfile>(row.Profile), row.AccountDisplayName))
            .ToList();
    }

    public async Task<IReadOnlyList<ScopeUnit>> ListUnitsAsync(
        string externalSubject, Guid profileScopeId, CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<UnitRow>(new CommandDefinition("""
            SELECT unit.id AS UnitId, unit.nombre_visible AS Name
              FROM dbo.cuentas account
              JOIN dbo.ambitos_perfil profile ON profile.cuenta_id = account.id
                  AND profile.id = @ProfileScopeId AND profile.centro_id = @CenterId
                  AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
              JOIN dbo.centros center ON center.id = profile.centro_id AND center.estado = 'ACTIVE'
              JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = profile.id
                  AND unit_scope.centro_id = profile.centro_id AND unit_scope.revocado_en IS NULL
              JOIN dbo.unidades unit ON unit.id = unit_scope.unidad_id AND unit.centro_id = profile.centro_id AND unit.estado = 'ACTIVE'
             WHERE account.sujeto_externo = @ExternalSubject AND account.estado = 'ACTIVE'
             ORDER BY unit.nombre_visible
            """, new { ExternalSubject = externalSubject, ProfileScopeId = profileScopeId, CenterId = centerId.Value },
            cancellationToken: ct));

        return rows.Select(row => new ScopeUnit(UnitId.From(row.UnitId), row.Name)).ToList();
    }

    private sealed record Row(Guid ProfileScopeId, Guid AccountId, Guid CenterId, string CenterName, string Profile, string? AccountDisplayName);

    private sealed record UnitRow(Guid UnitId, string Name);
}
