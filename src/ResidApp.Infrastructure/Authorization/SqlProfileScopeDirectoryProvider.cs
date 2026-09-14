using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Infrastructure.Persistence;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Authorization;

/// <summary>Lee los ambitos_perfil ACTIVE de una cuenta (dbo.cuentas ⋈ dbo.ambitos_perfil ⋈ dbo.centros),
/// sin unir con unidades/residentes: esta pantalla solo resuelve el par (ProfileScopeId, CenterId) que
/// alimenta AuthorizationSelection, no el ámbito completo de autorización.</summary>
public sealed class SqlProfileScopeDirectoryProvider(SqlConnectionFactory connections) : IProfileScopeDirectoryProvider
{
    public async Task<IReadOnlyList<ActiveProfileScope>> ListActiveAsync(string externalSubject, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition("""
            SELECT profile.id AS ProfileScopeId, account.id AS AccountId, profile.centro_id AS CenterId,
                   center.nombre_visible AS CenterName, profile.perfil_codigo AS Profile
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
                EnumCode.ParseCode<SystemProfile>(row.Profile)))
            .ToList();
    }

    private sealed record Row(Guid ProfileScopeId, Guid AccountId, Guid CenterId, string CenterName, string Profile);
}
