using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Baseline.Answers;
using ResidApp.Domain.Baseline.Catalogs;
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
        var scopes = new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory);
        var session = new FixedSessionIdentityProvider(externalSubject);
        var residents = new SqlResidentRepository(TestDatabase.ConnectionFactory);
        var baselines = new SqlBaselineRepository(TestDatabase.ConnectionFactory);
        return new ResidentBaselineApplicationService(
            new CreateResident(evidenceProvider, session, residents),
            new SignBaseline(evidenceProvider, session, baselines),
            new ReadDirectionBaseline(evidenceProvider, session, baselines),
            new CreateBaselineDraft(evidenceProvider, session, baselines),
            new LoadBaselineDraft(scopes, baselines, session),
            new SaveBaselineDraftArea(scopes, baselines, session),
            new SaveBaselineDraftBarthel(scopes, baselines, session),
            new CancelBaselineDraft(scopes, baselines, session));
    }

    private static CreateResidentCommand Command(SeededProfile seed, string displayName = "Residente de Aplicación") =>
        new(seed.ProfileScopeId, seed.CenterId, seed.UnitId, displayName, new DateOnly(1942, 3, 3),
            DocumentedSexCode.OtraCategoriaDocumentada, null, null, null, null, null, Guid.NewGuid());

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

    [Fact]
    public async Task CreateBaselineDraftAsync_WithoutPermission_ReturnsAccessDenied()
    {
        var (seed, resident) = await SeedResidentWithEnfermeriaAsync([]);
        var service = BuildService(seed.ExternalSubject);

        var result = await service.CreateBaselineDraftAsync(DraftCommand(seed, resident.ResidentId, BaselineReason.Alta));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
    }

    [Fact]
    public async Task CreateBaselineDraftAsync_WithPermission_Succeeds_AndCanBeLoadedEditedAndCancelled()
    {
        var (seed, resident) = await SeedResidentWithEnfermeriaAsync(
            [ResidentBaselinePermission.BaselineInitialComplete.ToCode()]);
        var service = BuildService(seed.ExternalSubject);

        var created = await service.CreateBaselineDraftAsync(DraftCommand(seed, resident.ResidentId, BaselineReason.Alta));
        Assert.True(created.Ok);

        var loaded = await service.LoadBaselineDraftAsync(new LoadBaselineDraftCommand(seed.ProfileScopeId, seed.CenterId, resident.ResidentId));
        Assert.True(loaded.Ok);
        Assert.NotNull(loaded.Value);
        Assert.Empty(loaded.Value!.Areas);

        var savedArea = await service.SaveBaselineDraftAreaAsync(new SaveBaselineDraftAreaCommand(
            seed.ProfileScopeId, seed.CenterId, resident.ResidentId, BaselineArea.AyudasHabituales,
            new UsualAidsAreaAnswer([UsualAidCode.Ninguno], null, null), null));
        Assert.True(savedArea.Ok);

        var cancelled = await service.CancelBaselineDraftAsync(
            new CancelBaselineDraftCommand(seed.ProfileScopeId, seed.CenterId, resident.ResidentId, "Reevaluación equivocada, se repite"));
        Assert.True(cancelled.Ok);

        var afterCancel = await service.LoadBaselineDraftAsync(new LoadBaselineDraftCommand(seed.ProfileScopeId, seed.CenterId, resident.ResidentId));
        Assert.True(afterCancel.Ok);
        Assert.Null(afterCancel.Value);
    }

    [Fact]
    public async Task CreateBaselineDraftAsync_WithAuxiliarProfile_ReturnsAccessDenied()
    {
        var (seed, resident) = await SeedResidentWithProfileAsync(SystemProfile.Auxiliar, []);
        var service = BuildService(seed.ExternalSubject);

        var result = await service.CreateBaselineDraftAsync(DraftCommand(seed, resident.ResidentId, BaselineReason.Alta));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
    }

    private static CreateBaselineDraftCommand DraftCommand(SeededProfile seed, ResidentId residentId, BaselineReason reason) =>
        new(seed.ProfileScopeId, seed.CenterId, residentId, reason, InformationSourceCode.ValoracionDirecta, null, new DateOnly(2026, 9, 14), Guid.NewGuid());

    private static Task<(SeededProfile Seed, CreateResidentResult Resident)> SeedResidentWithEnfermeriaAsync(IEnumerable<string> permissionCodes) =>
        SeedResidentWithProfileAsync(SystemProfile.Enfermeria, permissionCodes);

    private static async Task<(SeededProfile Seed, CreateResidentResult Resident)> SeedResidentWithProfileAsync(
        SystemProfile profile, IEnumerable<string> permissionCodes)
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var profileSeed = await SeedFixture.AddProfileToCenterAsync(profile, adminSeed.CenterId, adminSeed.UnitId, permissionCodes);
        var residents = new SqlResidentRepository(TestDatabase.ConnectionFactory);
        var resident = await residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Borrador Vía Aplicación", new DateOnly(1941, 7, 7), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));
        return (profileSeed, resident);
    }
}

file sealed class FixedSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
