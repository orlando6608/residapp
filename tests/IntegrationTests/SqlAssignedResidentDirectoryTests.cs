using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Contra la instancia real de SQL Server. El predicado de ListAsync (AUX-01) debe replicar
/// exactamente el mismo criterio residente+unidad que la evidencia de autorización de un único residente
/// (SqlAuthorizationEvidenceProvider), para que "aparece en mi lista" y "puedo abrirlo" sean siempre la
/// misma cosa.</summary>
public class SqlAssignedResidentDirectoryTests
{
    private readonly SqlAssignedResidentDirectory _directory = new(TestDatabase.ConnectionFactory);
    private readonly SqlResidentRepository _residents = new(TestDatabase.ConnectionFactory);

    [Fact]
    public async Task ListAsync_ReturnsOnlyResidentsWithActiveAssignmentInGrantedUnit()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var auxiliarSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, adminSeed.CenterId, adminSeed.UnitId);

        var assigned = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Asignado", new DateOnly(1940, 1, 1), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));
        var notAssigned = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Sin Asignar", new DateOnly(1941, 1, 1), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));
        _ = notAssigned;

        await AssignAsync(auxiliarSeed.ProfileScopeId, adminSeed.CenterId, assigned.ResidentId, adminSeed.AccountId);

        var result = await _directory.ListAsync(auxiliarSeed.ProfileScopeId, adminSeed.CenterId);

        Assert.Single(result);
        Assert.Equal(assigned.ResidentId, result[0].ResidentId);
        Assert.False(result[0].TieneBasalVigente);
    }

    [Fact]
    public async Task ListAsync_WhenResidentLocatedOutsideGrantedUnit_IsExcluded()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var auxiliarSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, adminSeed.CenterId, adminSeed.UnitId);
        var otherUnitId = await CreateUnitAsync(adminSeed.CenterId);

        var elsewhere = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, otherUnitId,
            "Residente En Otra Unidad", new DateOnly(1943, 1, 1), DocumentedSexCode.NoConsta, null, null, null, null, null, Guid.NewGuid()));

        await AssignAsync(auxiliarSeed.ProfileScopeId, adminSeed.CenterId, elsewhere.ResidentId, adminSeed.AccountId);

        var result = await _directory.ListAsync(auxiliarSeed.ProfileScopeId, adminSeed.CenterId);

        Assert.Empty(result);
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

    private static async Task AssignAsync(Guid profileScopeId, CenterId centerId, ResidentId residentId, AccountId accountId)
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
