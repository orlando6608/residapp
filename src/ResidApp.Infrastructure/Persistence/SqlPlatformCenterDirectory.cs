using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>Los centros del sistema distintos del reservado, para el operador de plataforma. Solo datos del centro: código,
/// nombre, estado y número de unidades; nada de residentes.</summary>
public sealed class SqlPlatformCenterDirectory(SqlConnectionFactory connections) : IPlatformCenterDirectory
{
    public async Task<IReadOnlyList<PlatformCenterSummary>> ListAsync(PlatformAccess access, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlPlatformCenterRepository.EnsureOperatorAsync(connection, null, access, ct);
        var rows = await connection.QueryAsync<CenterRow>(new CommandDefinition("""
            SELECT center.id AS CenterId, center.codigo AS Code, center.nombre_visible AS Name,
                   CAST(CASE WHEN center.estado = 'ACTIVE' THEN 1 ELSE 0 END AS BIT) AS Active,
                   (SELECT COUNT(*) FROM dbo.unidades unit WHERE unit.centro_id = center.id) AS UnitCount,
                   center.creado_en AS CreatedAt
              FROM dbo.centros center
             WHERE center.id <> @PlatformCenterId
             ORDER BY center.nombre_visible, center.codigo
            """, new { PlatformCenterId = PlatformCenter.Id }, cancellationToken: ct));
        return rows.Select(row => new PlatformCenterSummary(
            CenterId.From(row.CenterId), row.Code, row.Name, row.Active, row.UnitCount,
            new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)))).ToList();
    }

    public async Task<PlatformCenterDetail?> FindAsync(PlatformAccess access, CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlPlatformCenterRepository.EnsureOperatorAsync(connection, null, access, ct);
        var center = await connection.QuerySingleOrDefaultAsync<CenterNameRow>(new CommandDefinition("""
            SELECT center.codigo AS Code, center.nombre_visible AS Name FROM dbo.centros center
             WHERE center.id = @CenterId AND center.id <> @PlatformCenterId
            """, new { CenterId = centerId.Value, PlatformCenterId = PlatformCenter.Id }, cancellationToken: ct));
        if (center is null)
        {
            return null;
        }

        var units = (await connection.QueryAsync<UnitNameRow>(new CommandDefinition("""
            SELECT id AS UnitId, nombre_visible AS Name FROM dbo.unidades WHERE centro_id = @CenterId AND estado = 'ACTIVE' ORDER BY nombre_visible
            """, new { CenterId = centerId.Value }, cancellationToken: ct))).Select(u => (UnitId.From(u.UnitId), u.Name)).ToList();
        var scopes = (await connection.QueryAsync<AdministrationRow>(new CommandDefinition("""
            SELECT profile.id AS ProfileScopeId, account.sujeto_externo AS Subject, account.nombre_visible AS AccountName,
                   CAST(CASE WHEN account.estado = 'ACTIVE' THEN 1 ELSE 0 END AS BIT) AS Active,
                   CAST(CASE WHEN (SELECT TOP (1) change.principal FROM dbo.administraciones_principales_cambios change
                                    WHERE change.ambito_perfil_id = profile.id ORDER BY change.numero DESC) = 1 THEN 1 ELSE 0 END AS BIT) AS IsPrincipal
              FROM dbo.ambitos_perfil profile JOIN dbo.cuentas account ON account.id = profile.cuenta_id
             WHERE profile.centro_id = @CenterId AND profile.perfil_codigo = 'ADMINISTRACION' AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
             ORDER BY COALESCE(account.nombre_visible, account.sujeto_externo)
            """, new { CenterId = centerId.Value }, cancellationToken: ct))).ToList();
        var administrations = new List<PlatformAdministrationScope>();
        foreach (var scope in scopes)
        {
            administrations.Add(new PlatformAdministrationScope(
                scope.ProfileScopeId, scope.Subject, scope.AccountName, scope.Active, scope.IsPrincipal,
                await SqlAdministrationScope.ListUnitsAsync(connection, null, centerId, scope.ProfileScopeId, ct)));
        }

        return new PlatformCenterDetail(centerId, center.Code, center.Name, units, administrations);
    }

    private sealed record CenterNameRow(string Code, string Name);

    private sealed record UnitNameRow(Guid UnitId, string Name);

    private sealed record AdministrationRow(Guid ProfileScopeId, string Subject, string? AccountName, bool Active, bool IsPrincipal);

    private sealed record CenterRow(Guid CenterId, string Code, string Name, bool Active, int UnitCount, DateTime CreatedAt);
}
