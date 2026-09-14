using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Recorre la misma cadena que AuxiliarApplicationServiceTests, pero para el vertical Enfermería
/// (grupo E1): identidad de sesión -> IProfileScopeDirectoryProvider/IEnfermeriaResidentDirectory ->
/// EnfermeriaApplicationService.</summary>
public class EnfermeriaApplicationServiceTests
{
    private static EnfermeriaApplicationService BuildService(string externalSubject)
    {
        var scopes = new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory);
        var directory = new SqlEnfermeriaResidentDirectory(TestDatabase.ConnectionFactory);
        var evidenceProvider = new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory);
        var baselines = new SqlBaselineRepository(TestDatabase.ConnectionFactory);
        var session = new FixedSessionIdentityProvider(externalSubject);

        var events = new SqlClinicalEventRepository(TestDatabase.ConnectionFactory);
        var changeInbox = new SqlChangeInboxDirectory(TestDatabase.ConnectionFactory);
        var listScopeResidents = new ListScopeResidents(scopes, directory, session);
        return new EnfermeriaApplicationService(
            listScopeResidents,
            new FindScopeResident(listScopeResidents),
            new ReadCurrentBaseline(evidenceProvider, session, baselines),
            new RegisterClinicalEvent(scopes, directory, session, events),
            new ListPendingChanges(scopes, changeInbox, session),
            new FindPendingChangeDetail(scopes, changeInbox, session));
    }

    [Fact]
    public async Task ListScopeResidentsAsync_WithNonEnfermeriaProfile_ReturnsAccessDenied()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Auxiliar);
        var service = BuildService(seed.ExternalSubject);

        var result = await service.ListScopeResidentsAsync(new ListScopeResidentsCommand(seed.ProfileScopeId, seed.CenterId));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
    }

    [Fact]
    public async Task ListScopeResidentsAsync_WithEnfermeriaProfile_ReturnsResidentsInGrantedUnit()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var enfermeriaSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, adminSeed.CenterId, adminSeed.UnitId);
        var residents = new SqlResidentRepository(TestDatabase.ConnectionFactory);
        var resident = await residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Servicio Enfermería", new DateOnly(1943, 3, 3), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));

        // La cuenta autenticada para este ámbito de perfil Enfermería es la del propio ámbito sembrado, no la de Administración.
        var service = BuildService(enfermeriaSeed.ExternalSubject);

        var result = await service.ListScopeResidentsAsync(new ListScopeResidentsCommand(enfermeriaSeed.ProfileScopeId, enfermeriaSeed.CenterId));

        Assert.True(result.Ok);
        Assert.Single(result.Value!);
        Assert.Equal(resident.ResidentId, result.Value![0].ResidentId);
    }

    [Fact]
    public async Task FindScopeResidentAsync_WhenResidentOutsideScope_ReturnsNullWithoutError()
    {
        var enfermeriaSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        var service = BuildService(enfermeriaSeed.ExternalSubject);

        var result = await service.FindScopeResidentAsync(
            new FindScopeResidentCommand(enfermeriaSeed.ProfileScopeId, enfermeriaSeed.CenterId, ResidentId.New()));

        Assert.True(result.Ok);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ReadCurrentBaselineAsync_WithEnfermeriaProfile_IsAuthorizedAndReturnsNullWhenNoBaseline()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var enfermeriaSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, adminSeed.CenterId, adminSeed.UnitId);
        var residents = new SqlResidentRepository(TestDatabase.ConnectionFactory);
        var resident = await residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Basal Enfermería", new DateOnly(1945, 5, 5), DocumentedSexCode.OtraCategoriaDocumentada, null, null, null, null, null, Guid.NewGuid()));

        var service = BuildService(enfermeriaSeed.ExternalSubject);

        var result = await service.ReadCurrentBaselineAsync(
            new ReadCurrentBaselineCommand(enfermeriaSeed.ProfileScopeId, enfermeriaSeed.CenterId, resident.ResidentId));

        Assert.True(result.Ok);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task RegisterClinicalEventAsync_ResidenteEnAmbito_Succeeds()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var enfermeriaSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, adminSeed.CenterId, adminSeed.UnitId);
        var residents = new SqlResidentRepository(TestDatabase.ConnectionFactory);
        var resident = await residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Evento Enfermería", new DateOnly(1950, 1, 1), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));
        var service = BuildService(enfermeriaSeed.ExternalSubject);

        var result = await service.RegisterClinicalEventAsync(new RegisterClinicalEventCommand(
            enfermeriaSeed.ProfileScopeId, enfermeriaSeed.CenterId, resident.ResidentId,
            "Se observa desorientación de inicio brusco durante la ronda de tarde.",
            DailyChangeClassification.Prioritario, "Constantes estables, sin fiebre.", Guid.NewGuid()));

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task RegisterClinicalEventAsync_SinObservacion_ReturnsInvalidInput()
    {
        var enfermeriaSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        var service = BuildService(enfermeriaSeed.ExternalSubject);

        var result = await service.RegisterClinicalEventAsync(new RegisterClinicalEventCommand(
            enfermeriaSeed.ProfileScopeId, enfermeriaSeed.CenterId, ResidentId.New(),
            "   ", DailyChangeClassification.Ordinario, null, Guid.NewGuid()));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.InvalidInput, result.Error!.Code);
    }

    [Fact]
    public async Task RegisterClinicalEventAsync_ResidenteFueraDeAmbito_ReturnsAccessDenied()
    {
        var enfermeriaSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        var service = BuildService(enfermeriaSeed.ExternalSubject);

        var result = await service.RegisterClinicalEventAsync(new RegisterClinicalEventCommand(
            enfermeriaSeed.ProfileScopeId, enfermeriaSeed.CenterId, ResidentId.New(),
            "Observación sobre un residente fuera de ámbito.", DailyChangeClassification.Ordinario, null, Guid.NewGuid()));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
    }

    [Fact]
    public async Task RegisterClinicalEventAsync_ConAuxiliarProfile_ReturnsAccessDenied()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Auxiliar);
        var service = BuildService(seed.ExternalSubject);

        var result = await service.RegisterClinicalEventAsync(new RegisterClinicalEventCommand(
            seed.ProfileScopeId, seed.CenterId, ResidentId.New(),
            "Observación con perfil incorrecto.", DailyChangeClassification.Ordinario, null, Guid.NewGuid()));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
    }

    [Fact]
    public async Task ListPendingChangesAsync_ConAuxiliarProfile_ReturnsAccessDenied()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Auxiliar);
        var service = BuildService(seed.ExternalSubject);

        var result = await service.ListPendingChangesAsync(new ListPendingChangesCommand(
            seed.ProfileScopeId, seed.CenterId, DailyChangeClassification.Ordinario));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
    }

    [Fact]
    public async Task ListPendingChangesAsync_ConEnfermeriaProfile_DevuelveElCambioDeSuUnidad()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var auxiliarSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, adminSeed.CenterId, adminSeed.UnitId);
        var enfermeriaSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, adminSeed.CenterId, adminSeed.UnitId);
        var residents = new SqlResidentRepository(TestDatabase.ConnectionFactory);
        var resident = await residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Bandeja Servicio Aplicación", new DateOnly(1957, 7, 17), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));
        var closures = new SqlDailyClosureRepository(TestDatabase.ConnectionFactory);
        await closures.RegisterChangeAsync(new RegisterDailyChangeInput(
            auxiliarSeed.AccountId, adminSeed.CenterId, adminSeed.UnitId, resident.ResidentId,
            [new DailyChangeAreaInput(DailyChangeAreaCode.AnimoConducta, [], "Más apagado de lo habitual")],
            null, DailyChangeClassification.Ordinario, null, null, Guid.NewGuid()));
        var service = BuildService(enfermeriaSeed.ExternalSubject);

        var result = await service.ListPendingChangesAsync(new ListPendingChangesCommand(
            enfermeriaSeed.ProfileScopeId, enfermeriaSeed.CenterId, DailyChangeClassification.Ordinario));

        Assert.True(result.Ok);
        Assert.Single(result.Value!);
        Assert.Equal(resident.ResidentId, result.Value![0].ResidentId);
    }

    [Fact]
    public async Task FindPendingChangeDetailAsync_FueraDeAmbito_ReturnsNullWithoutError()
    {
        var enfermeriaSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        var service = BuildService(enfermeriaSeed.ExternalSubject);

        var result = await service.FindPendingChangeDetailAsync(new FindPendingChangeDetailCommand(
            enfermeriaSeed.ProfileScopeId, enfermeriaSeed.CenterId, Guid.NewGuid()));

        Assert.True(result.Ok);
        Assert.Null(result.Value);
    }
}

file sealed class FixedSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
