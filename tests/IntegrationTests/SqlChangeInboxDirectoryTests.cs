using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Contra la instancia real de SQL Server. ENF-02/ENF-03/ENF-04, grupo E4: bandejas y detalle
/// sobre lo que Auxiliar ya genera (AUX-11A/AUX-12).</summary>
public class SqlChangeInboxDirectoryTests
{
    private readonly SqlChangeInboxDirectory _directory = new(TestDatabase.ConnectionFactory);
    private readonly SqlDailyClosureRepository _closures = new(TestDatabase.ConnectionFactory);
    private readonly SqlResidentRepository _residents = new(TestDatabase.ConnectionFactory);

    [Fact]
    public async Task ListAsync_OrdinarioEnLaMismaUnidad_EsVisibleParaEnfermeria()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var auxiliarSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, adminSeed.CenterId, adminSeed.UnitId);
        var enfermeriaSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, adminSeed.CenterId, adminSeed.UnitId);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Bandeja Ordinaria", new DateOnly(1953, 3, 13), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));
        await _closures.RegisterChangeAsync(new RegisterDailyChangeInput(
            auxiliarSeed.AccountId, adminSeed.CenterId, adminSeed.UnitId, resident.ResidentId,
            [new DailyChangeAreaInput(DailyChangeAreaCode.HecesDiuresis, [], "Diuresis escasa esta tarde")],
            38.2m, DailyChangeClassification.Ordinario, null, null, Guid.NewGuid()));

        var list = await _directory.ListAsync(enfermeriaSeed.ProfileScopeId, adminSeed.CenterId, DailyChangeClassification.Ordinario);

        Assert.Single(list);
        Assert.Equal(resident.ResidentId, list[0].ResidentId);
        Assert.Equal(SystemProfile.Auxiliar, list[0].AuthorProfile);
        Assert.Equal([DailyChangeAreaCode.HecesDiuresis], list[0].Areas);

        var prioritarios = await _directory.ListAsync(enfermeriaSeed.ProfileScopeId, adminSeed.CenterId, DailyChangeClassification.Prioritario);
        Assert.Empty(prioritarios);
    }

    [Fact]
    public async Task ListAsync_PrioritarioConMotivoYAvisoDirecto_IncluyeAmbosCampos()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var auxiliarSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, adminSeed.CenterId, adminSeed.UnitId);
        var enfermeriaSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, adminSeed.CenterId, adminSeed.UnitId);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Bandeja Prioritaria", new DateOnly(1954, 4, 14), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));
        await _closures.RegisterChangeAsync(new RegisterDailyChangeInput(
            auxiliarSeed.AccountId, adminSeed.CenterId, adminSeed.UnitId, resident.ResidentId,
            [new DailyChangeAreaInput(DailyChangeAreaCode.DolorMalestar, [DailyChangeAreaOptionCode.Cefalea], null)],
            null, DailyChangeClassification.Prioritario, DailyChangePriorityReason.DolorNuevoIntenso,
            "Se avisa a Enfermería por teléfono a las 17:10.", Guid.NewGuid()));

        var list = await _directory.ListAsync(enfermeriaSeed.ProfileScopeId, adminSeed.CenterId, DailyChangeClassification.Prioritario);

        Assert.Single(list);
        Assert.Equal(DailyChangePriorityReason.DolorNuevoIntenso, list[0].PriorityReason);
        Assert.Equal("Se avisa a Enfermería por teléfono a las 17:10.", list[0].DirectNoticeNotes);
    }

    [Fact]
    public async Task ListAsync_SinUnidadConcedida_NoEsVisible()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var auxiliarSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, adminSeed.CenterId, adminSeed.UnitId);
        var enfermeriaSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria); // otro centro/unidad
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Fuera De Unidad", new DateOnly(1955, 5, 15), DocumentedSexCode.NoConsta, null, null, null, null, null, Guid.NewGuid()));
        await _closures.RegisterChangeAsync(new RegisterDailyChangeInput(
            auxiliarSeed.AccountId, adminSeed.CenterId, adminSeed.UnitId, resident.ResidentId,
            [new DailyChangeAreaInput(DailyChangeAreaCode.Sueno, [DailyChangeAreaOptionCode.Insomnio], null)],
            null, DailyChangeClassification.Ordinario, null, null, Guid.NewGuid()));

        var list = await _directory.ListAsync(enfermeriaSeed.ProfileScopeId, enfermeriaSeed.CenterId, DailyChangeClassification.Ordinario);

        Assert.Empty(list);
    }

    [Fact]
    public async Task FindAsync_DevuelveAreasConOpcionesYTextoLibre()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var auxiliarSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, adminSeed.CenterId, adminSeed.UnitId);
        var enfermeriaSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, adminSeed.CenterId, adminSeed.UnitId);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Detalle Cambio", new DateOnly(1956, 6, 16), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));
        var registered = await _closures.RegisterChangeAsync(new RegisterDailyChangeInput(
            auxiliarSeed.AccountId, adminSeed.CenterId, adminSeed.UnitId, resident.ResidentId,
            [
                new DailyChangeAreaInput(DailyChangeAreaCode.Sueno, [DailyChangeAreaOptionCode.Insomnio, DailyChangeAreaOptionCode.Somnolencia], null),
                new DailyChangeAreaInput(DailyChangeAreaCode.EstadoConciencia, [], "Más somnoliento de lo habitual durante la tarde."),
            ],
            37.1m, DailyChangeClassification.Ordinario, null, null, Guid.NewGuid()));

        var detail = await _directory.FindAsync(enfermeriaSeed.ProfileScopeId, adminSeed.CenterId, registered.ClosureId);

        Assert.NotNull(detail);
        Assert.Equal(resident.ResidentId, detail!.ResidentId);
        Assert.Equal(37.1m, detail.TemperatureCelsius);
        Assert.Equal(2, detail.Areas.Count);
        var sueno = detail.Areas.Single(a => a.AreaCode == DailyChangeAreaCode.Sueno);
        Assert.Equal([DailyChangeAreaOptionCode.Insomnio, DailyChangeAreaOptionCode.Somnolencia], sueno.Options);
        Assert.Null(sueno.FreeText);
        var conciencia = detail.Areas.Single(a => a.AreaCode == DailyChangeAreaCode.EstadoConciencia);
        Assert.Equal("Más somnoliento de lo habitual durante la tarde.", conciencia.FreeText);
    }

    [Fact]
    public async Task FindAsync_FueraDeAmbito_ReturnsNull()
    {
        var enfermeriaSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);

        var detail = await _directory.FindAsync(enfermeriaSeed.ProfileScopeId, enfermeriaSeed.CenterId, Guid.NewGuid());

        Assert.Null(detail);
    }
}
