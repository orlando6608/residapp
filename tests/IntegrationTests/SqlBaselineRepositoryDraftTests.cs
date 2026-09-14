using Dapper;
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

        var items = FullBarthelItems();
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

        await _repository.SaveAreaAsync(new SaveBaselineDraftAreaInput(owner, BaselineArea.Movilidad,
            new MobilityAreaAnswer(MobilityDisplacementCode.DeambulaIndependienteSinAyuda, MobilityAidCode.Ninguna, null, MobilityTransferCode.Independiente), null));
        await _repository.SaveAreaAsync(new SaveBaselineDraftAreaInput(owner, BaselineArea.Alimentacion,
            new FeedingAreaAnswer(FeedingRouteCode.Oral, FoodTextureCode.Normal, null, LiquidConsistencyCode.Iddsi0FinoSinEspesar,
                FeedingAssistanceCode.Independiente, SwallowingPrecautionsCode.NingunaDocumentada, null), null));
        await _repository.SaveAreaAsync(new SaveBaselineDraftAreaInput(owner, BaselineArea.Continencia,
            new ContinenceAreaAnswer(ContinenceValueCode.Continente, ContinenceValueCode.Continente, [ContinenceManagementCode.Ninguno], null), null));
        await _repository.SaveAreaAsync(new SaveBaselineDraftAreaInput(owner, BaselineArea.AseoHigiene,
            new PersonalCareAreaAnswer(PersonalCareCode.Independiente, BathingCode.Independiente), null));
        await _repository.SaveAreaAsync(new SaveBaselineDraftAreaInput(owner, BaselineArea.Cognicion,
            new CognitionAreaAnswer(CognitionCategoryCode.SinDeterioroConocidoODocumentado, null, null, null, null, null, null), null));
        await _repository.SaveAreaAsync(new SaveBaselineDraftAreaInput(owner, BaselineArea.Comunicacion,
            new CommunicationAreaAnswer(ComprehensionCode.ComprensionFuncional, ExpressionCode.ExpresaNecesidadesEficazmente, [CommunicationFormCode.LenguajeOral], null), null));
        await _repository.SaveAreaAsync(new SaveBaselineDraftAreaInput(owner, BaselineArea.Conducta,
            new BehaviorAreaAnswer(BehaviorStatusCode.SinConductasRelevantesConocidas, [], null), null));
        await _repository.SaveAreaAsync(new SaveBaselineDraftAreaInput(owner, BaselineArea.Sueno,
            new SleepAreaAnswer([SleepPatternCode.PatronHabitualmenteConservado]), null));
        await _repository.SaveAreaAsync(new SaveBaselineDraftAreaInput(owner, BaselineArea.AyudasHabituales,
            new UsualAidsAreaAnswer([UsualAidCode.Ninguno], null, null), null));

        await _repository.SaveBarthelAsync(new SaveBaselineDraftBarthelInput(owner, new DateOnly(2026, 9, 14), FullBarthelItems()));

        var signed = await _repository.SignDraftAsync(new SignBaselineDraftInput(
            seed.AccountId, SystemProfile.Enfermeria, seed.CenterId, seed.UnitId, resident.ResidentId, created.DraftId, created.DraftRevision, Guid.NewGuid()));
        Assert.Equal(1, signed.VersionNumber);

        var current = await _repository.ReadCurrentSummaryAsync(new ReadCurrentBaselineSummaryInput(seed.CenterId, resident.ResidentId));
        Assert.NotNull(current);
        Assert.Equal(9, current!.Areas.Count);
        Assert.Equal(100, current.BarthelTotal);
    }

    private static List<BarthelItem> FullBarthelItems() =>
    [
        new(BarthelItemCode.Comer, "INDEPENDIENTE", BarthelCatalog.ScoreOf(BarthelItemCode.Comer, "INDEPENDIENTE")),
        new(BarthelItemCode.Lavarse, "SOLO_COMPLETO", BarthelCatalog.ScoreOf(BarthelItemCode.Lavarse, "SOLO_COMPLETO")),
        new(BarthelItemCode.Vestirse, "INDEPENDIENTE", BarthelCatalog.ScoreOf(BarthelItemCode.Vestirse, "INDEPENDIENTE")),
        new(BarthelItemCode.Arreglarse, "INDEPENDIENTE_HIGIENE_PERSONAL_BASICA", BarthelCatalog.ScoreOf(BarthelItemCode.Arreglarse, "INDEPENDIENTE_HIGIENE_PERSONAL_BASICA")),
        new(BarthelItemCode.Deposicion, "CONTINENTE", BarthelCatalog.ScoreOf(BarthelItemCode.Deposicion, "CONTINENTE")),
        new(BarthelItemCode.Miccion, "CONTINENTE", BarthelCatalog.ScoreOf(BarthelItemCode.Miccion, "CONTINENTE")),
        new(BarthelItemCode.UsoRetrete, "INDEPENDIENTE", BarthelCatalog.ScoreOf(BarthelItemCode.UsoRetrete, "INDEPENDIENTE")),
        new(BarthelItemCode.TrasladoCamaSillon, "INDEPENDIENTE", BarthelCatalog.ScoreOf(BarthelItemCode.TrasladoCamaSillon, "INDEPENDIENTE")),
        new(BarthelItemCode.Deambulacion, "CAMINA_50M_INDEPENDIENTE_CON_AYUDA_TECNICA_SI_PRECISA",
            BarthelCatalog.ScoreOf(BarthelItemCode.Deambulacion, "CAMINA_50M_INDEPENDIENTE_CON_AYUDA_TECNICA_SI_PRECISA")),
        new(BarthelItemCode.Escaleras, "SUBE_BAJA_UN_PISO_SOLO", BarthelCatalog.ScoreOf(BarthelItemCode.Escaleras, "SUBE_BAJA_UN_PISO_SOLO")),
    ];

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
