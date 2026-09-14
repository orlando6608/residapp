using Dapper;
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
        var closures = new SqlDailyClosureRepository(TestDatabase.ConnectionFactory);
        var session = new FixedSessionIdentityProvider(externalSubject);

        var listAssignedResidents = new ListAssignedResidents(scopes, directory, session);
        return new AuxiliarApplicationService(
            listAssignedResidents,
            new FindAssignedResident(listAssignedResidents),
            new ReadCurrentBaseline(evidenceProvider, session, baselines),
            new RegisterDailyClosure(scopes, directory, session, closures),
            new RegisterDailyChange(scopes, directory, session, closures));
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

    [Fact]
    public async Task RegisterDailyClosureAsync_SinCambiosParaResidenteAsignado_Succeeds()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var auxiliarSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, adminSeed.CenterId, adminSeed.UnitId);
        var residents = new SqlResidentRepository(TestDatabase.ConnectionFactory);
        var resident = await residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Cierre Aplicación", new DateOnly(1946, 6, 6), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));
        await AssignAsync(auxiliarSeed.ProfileScopeId, adminSeed.CenterId, resident.ResidentId, adminSeed.AccountId);
        var service = BuildService(auxiliarSeed.ExternalSubject);

        var result = await service.RegisterDailyClosureAsync(new RegisterDailyClosureCommand(
            auxiliarSeed.ProfileScopeId, auxiliarSeed.CenterId, resident.ResidentId, DailyClosureType.SinCambios, null, Guid.NewGuid()));

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task RegisterDailyClosureAsync_NoValorableSinMotivo_ReturnsInvalidInput()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var auxiliarSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, adminSeed.CenterId, adminSeed.UnitId);
        var residents = new SqlResidentRepository(TestDatabase.ConnectionFactory);
        var resident = await residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Cierre Sin Motivo Aplicación", new DateOnly(1946, 6, 16), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));
        await AssignAsync(auxiliarSeed.ProfileScopeId, adminSeed.CenterId, resident.ResidentId, adminSeed.AccountId);
        var service = BuildService(auxiliarSeed.ExternalSubject);

        var result = await service.RegisterDailyClosureAsync(new RegisterDailyClosureCommand(
            auxiliarSeed.ProfileScopeId, auxiliarSeed.CenterId, resident.ResidentId, DailyClosureType.NoValorable, "   ", Guid.NewGuid()));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.InvalidInput, result.Error!.Code);
    }

    [Fact]
    public async Task RegisterDailyClosureAsync_ResidenteNoAsignado_ReturnsAccessDenied()
    {
        var auxiliarSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Auxiliar);
        var service = BuildService(auxiliarSeed.ExternalSubject);

        var result = await service.RegisterDailyClosureAsync(new RegisterDailyClosureCommand(
            auxiliarSeed.ProfileScopeId, auxiliarSeed.CenterId, ResidentId.New(), DailyClosureType.SinCambios, null, Guid.NewGuid()));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
    }

    [Fact]
    public async Task RegisterDailyChangeAsync_OrdinarioConAreas_Succeeds()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var auxiliarSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, adminSeed.CenterId, adminSeed.UnitId);
        var residents = new SqlResidentRepository(TestDatabase.ConnectionFactory);
        var resident = await residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Cambio Aplicación", new DateOnly(1952, 2, 2), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));
        await AssignAsync(auxiliarSeed.ProfileScopeId, adminSeed.CenterId, resident.ResidentId, adminSeed.AccountId);
        var service = BuildService(auxiliarSeed.ExternalSubject);

        var result = await service.RegisterDailyChangeAsync(new RegisterDailyChangeCommand(
            auxiliarSeed.ProfileScopeId, auxiliarSeed.CenterId, resident.ResidentId,
            [new RegisterDailyChangeAreaCommand(DailyChangeAreaCode.HecesDiuresis, [], "Diuresis escasa esta tarde")],
            null, DailyChangeClassification.Ordinario, null, null, Guid.NewGuid()));

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task RegisterDailyChangeAsync_ConOpcionRapidaSinTexto_Succeeds()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var auxiliarSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, adminSeed.CenterId, adminSeed.UnitId);
        var residents = new SqlResidentRepository(TestDatabase.ConnectionFactory);
        var resident = await residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Cambio Opción Rápida", new DateOnly(1952, 3, 3), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));
        await AssignAsync(auxiliarSeed.ProfileScopeId, adminSeed.CenterId, resident.ResidentId, adminSeed.AccountId);
        var service = BuildService(auxiliarSeed.ExternalSubject);

        // AUX-07: un área con checklist se puede completar solo con opciones rápidas, sin texto libre.
        var result = await service.RegisterDailyChangeAsync(new RegisterDailyChangeCommand(
            auxiliarSeed.ProfileScopeId, auxiliarSeed.CenterId, resident.ResidentId,
            [new RegisterDailyChangeAreaCommand(
                DailyChangeAreaCode.Sueno, [DailyChangeAreaOptionCode.Insomnio, DailyChangeAreaOptionCode.Somnolencia], null)],
            null, DailyChangeClassification.Ordinario, null, null, Guid.NewGuid()));

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task RegisterDailyChangeAsync_OpcionNoPerteneceAlArea_ReturnsInvalidInput()
    {
        var auxiliarSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Auxiliar);
        var service = BuildService(auxiliarSeed.ExternalSubject);

        // Insomnio es del catálogo de Sueño, no del de Dolor/malestar (DailyChangeAreaOptionsCatalog):
        // debe rechazarse antes de comprobar autorización o residente asignado.
        var result = await service.RegisterDailyChangeAsync(new RegisterDailyChangeCommand(
            auxiliarSeed.ProfileScopeId, auxiliarSeed.CenterId, ResidentId.New(),
            [new RegisterDailyChangeAreaCommand(DailyChangeAreaCode.DolorMalestar, [DailyChangeAreaOptionCode.Insomnio], null)],
            null, DailyChangeClassification.Ordinario, null, null, Guid.NewGuid()));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.InvalidInput, result.Error!.Code);
    }

    [Fact]
    public async Task RegisterDailyChangeAsync_SinAreas_ReturnsInvalidInput()
    {
        var auxiliarSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Auxiliar);
        var service = BuildService(auxiliarSeed.ExternalSubject);

        var result = await service.RegisterDailyChangeAsync(new RegisterDailyChangeCommand(
            auxiliarSeed.ProfileScopeId, auxiliarSeed.CenterId, ResidentId.New(),
            [], null, DailyChangeClassification.Ordinario, null, null, Guid.NewGuid()));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.InvalidInput, result.Error!.Code);
    }

    [Fact]
    public async Task RegisterDailyChangeAsync_PrioritarioSinDocumentacion_ReturnsInvalidInput()
    {
        var auxiliarSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Auxiliar);
        var service = BuildService(auxiliarSeed.ExternalSubject);

        var result = await service.RegisterDailyChangeAsync(new RegisterDailyChangeCommand(
            auxiliarSeed.ProfileScopeId, auxiliarSeed.CenterId, ResidentId.New(),
            [new RegisterDailyChangeAreaCommand(DailyChangeAreaCode.IncidenciasCaidas, [], "Casi se cae al levantarse")],
            null, DailyChangeClassification.Prioritario, null, null, Guid.NewGuid()));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.InvalidInput, result.Error!.Code);
    }

    [Fact]
    public async Task RegisterDailyChangeAsync_ResidenteNoAsignado_ReturnsAccessDenied()
    {
        var auxiliarSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Auxiliar);
        var service = BuildService(auxiliarSeed.ExternalSubject);

        var result = await service.RegisterDailyChangeAsync(new RegisterDailyChangeCommand(
            auxiliarSeed.ProfileScopeId, auxiliarSeed.CenterId, ResidentId.New(),
            [new RegisterDailyChangeAreaCommand(DailyChangeAreaCode.AnimoConducta, [], "Más irritable de lo habitual")],
            null, DailyChangeClassification.Ordinario, null, null, Guid.NewGuid()));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
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
