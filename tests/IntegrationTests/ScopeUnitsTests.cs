using Dapper;
using ResidApp.Infrastructure.Authorization;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Selector de unidad del alta de residente: SqlProfileScopeDirectoryProvider.ListUnitsAsync da las unidades
/// concedidas y activas del ámbito, con las mismas condiciones con las que se autoriza el alta.</summary>
public class ScopeUnitsTests
{
    private static async Task<Guid> AddUnitAsync(SeededProfile seed, string name, string status = "ACTIVE", bool grant = true, bool revoked = false)
    {
        var unitId = Guid.NewGuid();
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@unitId, @centerId, @code, @name, @status, SYSUTCDATETIME())",
            new { unitId, centerId = seed.CenterId.Value, code = $"TEST-UNIT-{unitId:N}"[..30], name, status });
        if (grant)
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id,
                                                       revocado_en, revocado_por_cuenta_id)
                VALUES (NEWID(), @profileScopeId, @centerId, @unitId, SYSUTCDATETIME(), @accountId,
                        CASE WHEN @revoked = 1 THEN SYSUTCDATETIME() END, CASE WHEN @revoked = 1 THEN @accountId END)
                """, new { profileScopeId = seed.ProfileScopeId, centerId = seed.CenterId.Value, unitId, accountId = seed.AccountId.Value, revoked });
        }

        return unitId;
    }

    [Fact]
    public async Task Unidades_SoloLasConcedidasYActivasDelAmbito_OrdenadasPorNombre()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var planta = await AddUnitAsync(admin, "AAA Planta concedida");
        await AddUnitAsync(admin, "AAA Revocada", revoked: true);
        await AddUnitAsync(admin, "AAA Inactiva", status: "INACTIVE");
        await AddUnitAsync(admin, "AAA Sin conceder", grant: false);
        var provider = new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory);

        var units = await provider.ListUnitsAsync(admin.ExternalSubject, admin.ProfileScopeId, admin.CenterId);

        Assert.Equal([planta, admin.UnitId.Value], units.Select(u => u.UnitId.Value));
        Assert.Equal("AAA Planta concedida", units[0].Name);
    }

    [Fact]
    public async Task Unidades_DeUnAmbitoAjenoOConOtroCentro_SaleVacia()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var other = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        var provider = new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory);

        Assert.Empty(await provider.ListUnitsAsync(other.ExternalSubject, admin.ProfileScopeId, admin.CenterId));
        Assert.Empty(await provider.ListUnitsAsync(admin.ExternalSubject, admin.ProfileScopeId, other.CenterId));
    }
}
