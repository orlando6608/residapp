using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Contra la instancia real de SQL Server. ENF-17: a diferencia de SqlAssignedResidentDirectory
/// (Auxiliar, que exige siempre una asignación explícita), Enfermería sigue la regla de "ámbito por
/// defecto o restringido" — el mismo criterio que SqlAuthorizationEvidenceProvider aplica para un único
/// residente.</summary>
public class SqlEnfermeriaResidentDirectoryTests
{
    private readonly SqlEnfermeriaResidentDirectory _directory = new(TestDatabase.ConnectionFactory);
    private readonly SqlResidentRepository _residents = new(TestDatabase.ConnectionFactory);

    [Fact]
    public async Task ListAsync_WithoutRestrictions_ReturnsAllResidentsInGrantedUnit()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var enfermeriaSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, adminSeed.CenterId, adminSeed.UnitId);

        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Ámbito Enfermería", new DateOnly(1939, 1, 1), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));

        var result = await _directory.ListAsync(enfermeriaSeed.ProfileScopeId, adminSeed.CenterId);

        Assert.Single(result);
        Assert.Equal(resident.ResidentId, result[0].ResidentId);
        Assert.False(result[0].TieneBasalVigente);
    }

    [Fact]
    public async Task ListAsync_WhenResidentLocatedOutsideGrantedUnit_IsExcluded()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var enfermeriaSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, adminSeed.CenterId, adminSeed.UnitId);
        var otherUnitId = await CreateUnitAsync(adminSeed.CenterId);

        await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, otherUnitId,
            "Residente En Otra Unidad Enfermería", new DateOnly(1940, 2, 2), DocumentedSexCode.NoConsta, null, null, null, null, null, Guid.NewGuid()));

        var result = await _directory.ListAsync(enfermeriaSeed.ProfileScopeId, adminSeed.CenterId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListAsync_WithExplicitRestriction_ReturnsOnlyGrantedResident()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var enfermeriaSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, adminSeed.CenterId, adminSeed.UnitId);

        var granted = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Restringido Concedido", new DateOnly(1941, 3, 3), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));
        var notGranted = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Restringido No Concedido", new DateOnly(1942, 4, 4), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));
        _ = notGranted;

        await GrantAsync(enfermeriaSeed.ProfileScopeId, adminSeed.CenterId, granted.ResidentId, adminSeed.AccountId);

        var result = await _directory.ListAsync(enfermeriaSeed.ProfileScopeId, adminSeed.CenterId);

        Assert.Single(result);
        Assert.Equal(granted.ResidentId, result[0].ResidentId);
    }

    private static async Task<UnitId> CreateUnitAsync(CenterId centerId)
    {
        var unitId = UnitId.New();
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@id, @centerId, @code, @code, 'ACTIVE', SYSUTCDATETIME())",
            new { id = unitId.Value, centerId = centerId.Value, code = $"OTRA-{Guid.NewGuid():N}"[..12] });
        return unitId;
    }

    private static async Task GrantAsync(Guid profileScopeId, CenterId centerId, ResidentId residentId, AccountId accountId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.ambitos_perfil_residente (id, ambito_perfil_id, centro_id, residente_id, concedido_en, concedido_por_cuenta_id)
            VALUES (@Id, @ProfileScopeId, @CenterId, @ResidentId, SYSUTCDATETIME(), @AccountId)
            """, new
        {
            Id = Guid.NewGuid(), ProfileScopeId = profileScopeId, CenterId = centerId.Value,
            ResidentId = residentId.Value, AccountId = accountId.Value,
        });
    }
}
