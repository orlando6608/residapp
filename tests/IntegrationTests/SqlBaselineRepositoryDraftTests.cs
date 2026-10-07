using Dapper;
using ResidApp.Application.Authorization;
using ResidApp.Application.Ports;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Baseline.Answers;
using ResidApp.Domain.Baseline.Catalogs;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Contra la instancia real de SQL Server. ENF-19 a ENF-22 (grupo E2): crear el contenido de un
/// borrador de basal, guardar áreas y Barthel, cancelar, y encadenar con la firma ya existente
/// (SignDraftAsync) para demostrar el ciclo completo de extremo a extremo.</summary>
public class SqlBaselineRepositoryDraftTests
{
    private readonly SqlBaselineRepository _repository = new(TestDatabase.ConnectionFactory);
    private readonly SqlResidentRepository _residents = new(TestDatabase.ConnectionFactory);

    [Fact]
    public async Task CreateDraftAsync_Succeeds_AndSecondAttemptForSameResidentConflicts()
    {
        var (seed, resident) = await SeedResidentAsync();

        var result = await _repository.CreateDraftAsync(CreateInput(seed, resident.ResidentId, BaselineReason.Alta, Guid.NewGuid()));
        Assert.Equal(1, result.DraftRevision);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            _repository.CreateDraftAsync(CreateInput(seed, resident.ResidentId, BaselineReason.Alta, Guid.NewGuid())));
    }

    [Fact]
    public async Task CreateDraftAsync_SameOperationIdTwice_DoesNotDuplicate()
    {
        var (seed, resident) = await SeedResidentAsync();
        var operationId = Guid.NewGuid();
        var input = CreateInput(seed, resident.ResidentId, BaselineReason.Alta, operationId);

        var first = await _repository.CreateDraftAsync(input);
        var second = await _repository.CreateDraftAsync(input);

        Assert.Equal(first.DraftId, second.DraftId);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var count = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.basales_borrador WHERE residente_id = @Id", new { Id = resident.ResidentId.Value });
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SaveAreaAsync_ThenLoadOwnedDraftAsync_RoundTripsTheAnswer()
    {
        var (seed, resident) = await SeedResidentAsync();
        await _repository.CreateDraftAsync(CreateInput(seed, resident.ResidentId, BaselineReason.Alta, Guid.NewGuid()));
        var owner = Owner(seed, resident.ResidentId);

        var answer = new UsualAidsAreaAnswer([UsualAidCode.Gafas, UsualAidCode.Audifono], null, null);
        await _repository.SaveAreaAsync(new SaveBaselineDraftAreaInput(owner, BaselineArea.AyudasHabituales, answer, "Usa gafas y audífono a diario"));

        var draft = await _repository.LoadOwnedDraftAsync(owner);
        Assert.NotNull(draft);
        var area = Assert.Single(draft!.Areas);
        Assert.Equal(BaselineArea.AyudasHabituales, area.AreaCode);
        Assert.Equal("Usa gafas y audífono a diario", area.Observation);
        var roundTripped = Assert.IsType<UsualAidsAreaAnswer>(area.Answer);
        Assert.Equal([UsualAidCode.Gafas, UsualAidCode.Audifono], roundTripped.AidCodes);
    }

    [Fact]
    public async Task SaveAreaAsync_WhenNotTheOwner_ThrowsNotAuthorized()
    {
        var (seed, resident) = await SeedResidentAsync();
        await _repository.CreateDraftAsync(CreateInput(seed, resident.ResidentId, BaselineReason.Alta, Guid.NewGuid()));

        var impostor = new OwnedActiveDraftInput(AccountId.From(Guid.NewGuid()), SystemProfile.Enfermeria, seed.CenterId, resident.ResidentId);
        var answer = new UsualAidsAreaAnswer([UsualAidCode.Ninguno], null, null);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            _repository.SaveAreaAsync(new SaveBaselineDraftAreaInput(impostor, BaselineArea.AyudasHabituales, answer, null)));
    }

    [Fact]
    public async Task SaveBarthelAsync_DerivesTotalFromCatalogAndPersistsItems()
    {
        var (seed, resident) = await SeedResidentAsync();
        await _repository.CreateDraftAsync(CreateInput(seed, resident.ResidentId, BaselineReason.Alta, Guid.NewGuid()));
        var owner = Owner(seed, resident.ResidentId);

        var items = BaselineTestData.FullBarthelItems();
        await _repository.SaveBarthelAsync(new SaveBaselineDraftBarthelInput(owner, new DateOnly(2026, 9, 14), items));

        var draft = await _repository.LoadOwnedDraftAsync(owner);
        Assert.NotNull(draft?.Barthel);
        Assert.Equal(100, draft!.Barthel.TotalScore);
        Assert.Equal(10, draft.Barthel.Items.Count);
    }

    [Fact]
    public async Task CancelDraftAsync_ThenLoadOwnedDraftAsync_ReturnsNull_AndAllowsANewDraft()
    {
        var (seed, resident) = await SeedResidentAsync();
        await _repository.CreateDraftAsync(CreateInput(seed, resident.ResidentId, BaselineReason.Alta, Guid.NewGuid()));
        var owner = Owner(seed, resident.ResidentId);

        await _repository.CancelDraftAsync(new CancelBaselineDraftInput(owner, "Cambio de turno antes de completarlo"));
        Assert.Null(await _repository.LoadOwnedDraftAsync(owner));

        var recreated = await _repository.CreateDraftAsync(CreateInput(seed, resident.ResidentId, BaselineReason.Alta, Guid.NewGuid()));
        Assert.Equal(1, recreated.DraftRevision);
    }

    [Fact]
    public async Task FullCycle_CreateDraft_SaveNineAreas_SaveBarthel_Sign_ProducesCurrentBaseline()
    {
        var (seed, resident) = await SeedResidentAsync();
        var created = await _repository.CreateDraftAsync(CreateInput(seed, resident.ResidentId, BaselineReason.Alta, Guid.NewGuid()));
        var owner = Owner(seed, resident.ResidentId);

        foreach (var (area, answer) in BaselineTestData.NineAreas())
        {
            await _repository.SaveAreaAsync(new SaveBaselineDraftAreaInput(owner, area, answer, null));
        }

        await _repository.SaveBarthelAsync(new SaveBaselineDraftBarthelInput(owner, new DateOnly(2026, 9, 14), BaselineTestData.FullBarthelItems()));

        var signed = await _repository.SignDraftAsync(new SignBaselineDraftInput(
            seed.AccountId, SystemProfile.Enfermeria, seed.CenterId, seed.UnitId, resident.ResidentId, created.DraftId, created.DraftRevision, Guid.NewGuid()));
        Assert.Equal(1, signed.VersionNumber);

        var current = await _repository.ReadCurrentSummaryAsync(new ReadCurrentBaselineSummaryInput(seed.CenterId, resident.ResidentId));
        Assert.NotNull(current);
        Assert.Equal(9, current!.Areas.Count);
        Assert.Equal(100, current.BarthelTotal);

        // Regresión: ReadAsClinicalDirectionAsync con ClinicalResourceType.BaselineCurrent nunca se había
        // ejercitado contra un basal realmente firmado (ReadCurrentSummaryAsync, arriba, usa un alias de
        // tabla distinto y seguro). El SQL de este camino usaba "current" como alias — palabra reservada
        // en T-SQL — y fallaba con "Incorrect syntax near the keyword 'current'" en cuanto había una fila
        // real que leer. Descubierto al montar el escenario integrado de pruebas para CJ.
        var directionSeed = await SeedFixture.AddProfileToCenterAsync(
            SystemProfile.DireccionClinica, seed.CenterId, seed.UnitId, [ResidentBaselinePermission.ClinicalDetailRead.ToCode()]);
        var headers = await _repository.ReadAsClinicalDirectionAsync(DirectionReadTestData.Input(
            directionSeed, seed.CenterId, seed.UnitId, resident.ResidentId, ClinicalResourceType.BaselineCurrent, Guid.NewGuid()));
        Assert.Single(headers);
        Assert.Equal(1, headers[0].VersionNumber);
    }

    private static CreateBaselineDraftInput CreateInput(SeededProfile seed, ResidentId residentId, BaselineReason reason, Guid operationId) =>
        new(seed.AccountId, SystemProfile.Enfermeria, seed.CenterId, seed.UnitId, residentId, reason,
            InformationSourceCode.ValoracionDirecta, null, new DateOnly(2026, 9, 14), operationId);

    private static OwnedActiveDraftInput Owner(SeededProfile seed, ResidentId residentId) =>
        new(seed.AccountId, SystemProfile.Enfermeria, seed.CenterId, residentId);

    private async Task<(SeededProfile Seed, CreateResidentResult Resident)> SeedResidentAsync()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var enfermeriaSeed = await SeedFixture.AddProfileToCenterAsync(
            SystemProfile.Enfermeria, adminSeed.CenterId, adminSeed.UnitId, ["BASELINE_INITIAL_COMPLETE", "BASELINE_REEVALUATE"]);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Borrador Basal", new DateOnly(1940, 6, 6), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));
        return (enfermeriaSeed, resident);
    }
}
