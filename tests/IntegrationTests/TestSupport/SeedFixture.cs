using Dapper;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests.TestSupport;

internal sealed record SeededProfile(string ExternalSubject, AccountId AccountId, CenterId CenterId, UnitId UnitId, Guid ProfileScopeId);

/// <summary>
/// Crea, para cada test, cuentas/centros/unidades/perfiles ficticios y aislados (códigos con sufijo
/// aleatorio) insertando directamente contra la base de datos real — sin pasar por los casos de uso, para
/// no acoplar el fixture a lo que se está probando. No hay limpieza posterior: es una base de desarrollo
/// con datos exclusivamente ficticios (ver README.md).
/// </summary>
internal static class SeedFixture
{
    public static Task<SeededProfile> CreateProfileAsync(SystemProfile profile, IEnumerable<string>? permissionCodes = null) =>
        CreateInternalAsync(profile, centerId: null, unitId: null, permissionCodes);

    /// <summary>Añade un perfil (cuenta + profile_scope + profile_unit_scope) a un centro/unidad ya
    /// existentes, para probar la interacción entre dos perfiles sobre el mismo residente.</summary>
    public static Task<SeededProfile> AddProfileToCenterAsync(
        SystemProfile profile, CenterId centerId, UnitId unitId, IEnumerable<string>? permissionCodes = null) =>
        CreateInternalAsync(profile, centerId, unitId, permissionCodes);

    private static async Task<SeededProfile> CreateInternalAsync(
        SystemProfile profile, CenterId? centerId, UnitId? unitId, IEnumerable<string>? permissionCodes)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var accountId = Guid.NewGuid();
        var resolvedCenterId = centerId?.Value ?? Guid.NewGuid();
        var resolvedUnitId = unitId?.Value ?? Guid.NewGuid();
        var profileScopeId = Guid.NewGuid();
        var externalSubject = $"test-{suffix}";
        var now = DateTimeOffset.UtcNow.UtcDateTime;

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        await connection.ExecuteAsync(
            "INSERT INTO dbo.accounts (id, external_subject, status, created_at) VALUES (@accountId, @externalSubject, 'ACTIVE', @now)",
            new { accountId, externalSubject, now });

        if (centerId is null)
        {
            await connection.ExecuteAsync(
                "INSERT INTO dbo.centers (id, code, display_name, status, created_at) VALUES (@id, @code, @code, 'ACTIVE', @now)",
                new { id = resolvedCenterId, code = $"TEST-CENTER-{suffix}", now });
        }
        if (unitId is null)
        {
            await connection.ExecuteAsync(
                "INSERT INTO dbo.units (id, center_id, code, display_name, status, created_at) VALUES (@id, @centerId, @code, @code, 'ACTIVE', @now)",
                new { id = resolvedUnitId, centerId = resolvedCenterId, code = $"TEST-UNIT-{suffix}", now });
        }

        await connection.ExecuteAsync("""
            INSERT INTO dbo.profile_scopes (id, account_id, center_id, profile_code, status, granted_at, granted_by_account_id)
            VALUES (@profileScopeId, @accountId, @centerId, @profileCode, 'ACTIVE', @now, @accountId)
            """, new { profileScopeId, accountId, centerId = resolvedCenterId, profileCode = profile.ToCode(), now });

        await connection.ExecuteAsync("""
            INSERT INTO dbo.profile_unit_scopes (id, profile_scope_id, center_id, unit_id, granted_at, granted_by_account_id)
            VALUES (@id, @profileScopeId, @centerId, @unitId, @now, @accountId)
            """, new { id = Guid.NewGuid(), profileScopeId, centerId = resolvedCenterId, unitId = resolvedUnitId, now, accountId });

        foreach (var permissionCode in permissionCodes ?? [])
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.profile_permissions (id, profile_scope_id, center_id, permission_code, granted_at, granted_by_account_id)
                VALUES (@id, @profileScopeId, @centerId, @permissionCode, @now, @accountId)
                """, new { id = Guid.NewGuid(), profileScopeId, centerId = resolvedCenterId, permissionCode, now, accountId });
        }

        return new SeededProfile(
            externalSubject, AccountId.From(accountId), CenterId.From(resolvedCenterId), UnitId.From(resolvedUnitId), profileScopeId);
    }
}
