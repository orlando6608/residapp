using Dapper;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Recorre la misma cadena que ResidentBaselineApplicationServiceTests, pero para el vertical
/// Auxiliar (grupo A1): identidad de sesión -> IProfileScopeDirectoryProvider/IAssignedResidentDirectory
/// -> AuxiliarApplicationService.</summary>
public class AuxiliarApplicationServiceTests
{
    private static AuxiliarApplicationService BuildService(string externalSubject)
    {
        var scopes = new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory);
        var directory = new SqlAssignedResidentDirectory(TestDatabase.ConnectionFactory);
        var evidenceProvider = new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory);
        var baselines = new SqlBaselineRepository(TestDatabase.ConnectionFactory);
        var session = new FixedSessionIdentityProvider(externalSubject);

        var listAssignedResidents = new ListAssignedResidents(scopes, directory, session);
        return new AuxiliarApplicationService(
            listAssignedResidents,
            new FindAssignedResident(listAssignedResidents),
            new ReadCurrentBaseline(evidenceProvider, session, baselines));
    }

    [Fact]
    public async Task ListAssignedResidentsAsync_WithNonAuxiliarProfile_ReturnsAccessDenied()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        var service = BuildService(seed.ExternalSubject);

        var result = await service.ListAssignedResidentsAsync(new ListAssignedResidentsCommand(seed.ProfileScopeId, seed.CenterId));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
    }

    [Fact]
    public async Task ListAssignedResidentsAsync_WithAuxiliarProfile_ReturnsOnlyAssignedResidents()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var auxiliarSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, adminSeed.CenterId, adminSeed.UnitId);
        var residents = new SqlResidentRepository(TestDatabase.ConnectionFactory);
        var assigned = await residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente De Servicio", new DateOnly(1944, 4, 4), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));
        await AssignAsync(auxiliarSeed.ProfileScopeId, adminSeed.CenterId, assigned.ResidentId, adminSeed.AccountId);

        // La cuenta autenticada para este ámbito de perfil Auxiliar es la del propio ámbito sembrado, no la de Administración.
        var service = BuildService(auxiliarSeed.ExternalSubject);

        var result = await service.ListAssignedResidentsAsync(new ListAssignedResidentsCommand(auxiliarSeed.ProfileScopeId, auxiliarSeed.CenterId));

        Assert.True(result.Ok);
        Assert.Single(result.Value!);
        Assert.Equal(assigned.ResidentId, result.Value![0].ResidentId);
    }

    [Fact]
    public async Task FindAssignedResidentAsync_WhenResidentNotAssigned_ReturnsNullWithoutError()
    {
        var auxiliarSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Auxiliar);
        var service = BuildService(auxiliarSeed.ExternalSubject);

        var result = await service.FindAssignedResidentAsync(
            new FindAssignedResidentCommand(auxiliarSeed.ProfileScopeId, auxiliarSeed.CenterId, ResidentId.New()));

        Assert.True(result.Ok);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ReadCurrentBaselineAsync_WithAuxiliarProfile_IsAuthorizedAndReturnsNullWhenNoBaseline()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var auxiliarSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, adminSeed.CenterId, adminSeed.UnitId);
        var residents = new SqlResidentRepository(TestDatabase.ConnectionFactory);
        var resident = await residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Basal Auxiliar", new DateOnly(1945, 5, 5), DocumentedSexCode.OtraCategoriaDocumentada, null, null, null, null, null, Guid.NewGuid()));
        await AssignAsync(auxiliarSeed.ProfileScopeId, adminSeed.CenterId, resident.ResidentId, adminSeed.AccountId);

        var service = BuildService(auxiliarSeed.ExternalSubject);

        var result = await service.ReadCurrentBaselineAsync(
            new ReadCurrentBaselineCommand(auxiliarSeed.ProfileScopeId, auxiliarSeed.CenterId, resident.ResidentId));

        Assert.True(result.Ok);
        Assert.Null(result.Value);
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

file sealed class FixedSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
