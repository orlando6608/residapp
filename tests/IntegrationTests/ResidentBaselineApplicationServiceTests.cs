using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Recorre la misma cadena que ejercita ResidApp.Web: identidad de sesión -> evidencia de
/// autorización real en SQL Server -> motor de decisión deny-by-default -> repositorio Dapper. Automatiza
/// lo que se verificó manualmente por HTTP durante el cableado de la Web (alta de residente).</summary>
public class ResidentBaselineApplicationServiceTests
{
    private static ResidentBaselineApplicationService BuildService(string externalSubject)
    {
        var evidenceProvider = new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory);
        var session = new FixedSessionIdentityProvider(externalSubject);
        var residents = new SqlResidentRepository(TestDatabase.ConnectionFactory);
        var baselines = new SqlBaselineRepository(TestDatabase.ConnectionFactory);
        return new ResidentBaselineApplicationService(
            new CreateResident(evidenceProvider, session, residents),
            new SignBaseline(evidenceProvider, session, baselines),
            new ReadDirectionBaseline(evidenceProvider, session, baselines));
    }

    private static CreateResidentCommand Command(SeededProfile seed, string displayName = "Residente de Aplicación") =>
        new(seed.ProfileScopeId, seed.CenterId, seed.UnitId, displayName, new DateOnly(1942, 3, 3),
            DocumentedSexCode.Other, null, null, null, null, null, Guid.NewGuid());

    [Fact]
    public async Task CreateResidentAsync_WithAdministracionProfile_Succeeds()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildService(seed.ExternalSubject);

        var result = await service.CreateResidentAsync(Command(seed));

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task CreateResidentAsync_WithUnknownProfileScope_ReturnsAccessDenied()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildService(seed.ExternalSubject);
        var command = Command(seed with { ProfileScopeId = Guid.NewGuid() });

        var result = await service.CreateResidentAsync(command);

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
    }

    [Fact]
    public async Task CreateResidentAsync_WithEnfermeriaWithoutPermission_ReturnsAccessDenied()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        var service = BuildService(seed.ExternalSubject);

        var result = await service.CreateResidentAsync(Command(seed));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
    }

    [Fact]
    public async Task CreateResidentAsync_WithEnfermeriaWithPermission_Succeeds()
    {
        var seed = await SeedFixture.CreateProfileAsync(
            SystemProfile.Enfermeria, [ResidentBaselinePermission.ResidentIdentityCreate.ToCode()]);
        var service = BuildService(seed.ExternalSubject);

        var result = await service.CreateResidentAsync(Command(seed, "Residente Vía Enfermería"));

        Assert.True(result.Ok);
    }
}

file sealed class FixedSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
