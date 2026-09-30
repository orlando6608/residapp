using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Baseline.Catalogs;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.EnfermeriaApplicationServiceTests;

namespace ResidApp.IntegrationTests;

/// <summary>Contra la instancia real de SQL Server. Historial, bloque 1 (historia 11 de Enfermería, 9 de
/// Medicina): eventos cerrados del residente con el basal y la ubicación de su fecha (HIS-01, HIS-03) y
/// versiones firmadas del basal (ENF-24).</summary>
public class HistorialTests
{
    private readonly SqlBaselineRepository _baselines = new(TestDatabase.ConnectionFactory);

    [Fact]
    public async Task EventoCerrado_ConservaElBasalDeSuFecha_TrasFirmarseUnaVersionNueva()
    {
        var (enfermera, medica, residentId) = await SeedResidentAsync();
        await SignBaselineAsync(enfermera, residentId, BaselineReason.Alta);
        var nursing = BuildService(enfermera.ExternalSubject);

        var closedId = await RegisterAsync(enfermera, residentId, "Caída sin lesiones en el baño.");
        var revision = await StartAndSaveAsync(enfermera, closedId);
        Assert.True((await nursing.CloseClinicalEventAsync(new CloseClinicalEventCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, closedId, revision, Guid.NewGuid(),
            FamilyCommunicationDecision.NoComunicar, null, null))).Ok);

        await SignBaselineAsync(enfermera, residentId, BaselineReason.RevisionProgramada);
        var openId = await RegisterAsync(enfermera, residentId, "Inapetencia en la cena.");

        var history = await nursing.ListClosedEventsAsync(
            new ListClosedEventsCommand(enfermera.ProfileScopeId, enfermera.CenterId, residentId, SystemProfile.Enfermeria));
        var item = Assert.Single(history.Value!);
        Assert.Equal(closedId, item.EventId);
        Assert.Equal(ClinicalEventOrigin.EventoEnfermeria, item.Origin);
        Assert.False(item.Escalated);
        Assert.Equal(1, item.Context!.BaselineVersionNumber);
        Assert.NotNull(item.Context.UnitName);

        // El detalle del evento cerrado conserva la versión de su fecha; el abierto, la vigente al registrarse.
        var closedDetail = await FindDetailAsync(enfermera, closedId);
        var openDetail = await FindDetailAsync(enfermera, openId);
        Assert.Equal(1, closedDetail.Context!.BaselineVersionNumber);
        Assert.Equal(2, openDetail.Context!.BaselineVersionNumber);

        // Medicina solo ve los escalados y sus eventos propios, también en el Historial.
        var medicina = await new ListClosedEvents(
                new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new SqlChangeInboxDirectory(TestDatabase.ConnectionFactory),
                new FixedHistorialSessionIdentityProvider(medica.ExternalSubject))
            .ExecuteAsync(new ListClosedEventsCommand(medica.ProfileScopeId, medica.CenterId, residentId, SystemProfile.Medicina));
        Assert.True(medicina.Ok);
        Assert.Empty(medicina.Value!);

        // La línea temporal recoge las dos versiones del basal.
        var timeline = await nursing.ReadResidentTimelineAsync(
            new ReadResidentTimelineCommand(enfermera.ProfileScopeId, enfermera.CenterId, residentId, SystemProfile.Enfermeria));
        Assert.True(timeline.Ok, timeline.Error?.Message);
        Assert.Equal([2, 1], timeline.Value!.OfType<TimelineEntry.BaselineSigned>().Select(b => b.VersionNumber));
    }

    [Fact]
    public async Task HistorialDelBasal_EnfermeriaYMedicina_VenVigenteEHistoricas()
    {
        var (enfermera, medica, residentId) = await SeedResidentAsync();
        Assert.Empty((await ReadBaselineHistoryAsync(enfermera, residentId)).Value!);

        await SignBaselineAsync(enfermera, residentId, BaselineReason.Alta);
        await SignBaselineAsync(enfermera, residentId, BaselineReason.RevisionProgramada);

        foreach (var profile in new[] { enfermera, medica })
        {
            var history = await ReadBaselineHistoryAsync(profile, residentId);
            Assert.Collection(history.Value!,
                current =>
                {
                    Assert.Equal(2, current.VersionNumber);
                    Assert.True(current.IsCurrent);
                    Assert.Equal(BaselineReason.RevisionProgramada, current.ReasonCode);
                    Assert.Equal(SystemProfile.Enfermeria, current.SignedByProfile);
                    Assert.Equal(100, current.BarthelTotal);
                    Assert.Equal(1, current.ReplacesVersionNumber);
                },
                previous =>
                {
                    Assert.Equal(1, previous.VersionNumber);
                    Assert.False(previous.IsCurrent);
                    Assert.Equal(BaselineReason.Alta, previous.ReasonCode);
                    Assert.Null(previous.ReplacesVersionNumber);
                });
        }
    }

    [Fact]
    public async Task VersionDelBasal_EnfermeriaYMedicina_AbrenLaHistoricaConSusAreasYSuBarthel()
    {
        var (enfermera, medica, residentId) = await SeedResidentAsync();
        await SignBaselineAsync(enfermera, residentId, BaselineReason.Alta);
        await SignBaselineAsync(enfermera, residentId, BaselineReason.RevisionProgramada);

        foreach (var profile in new[] { enfermera, medica })
        {
            var historic = (await ReadBaselineVersionAsync(profile, residentId, 1)).Value!;
            Assert.Equal(1, historic.Header.VersionNumber);
            Assert.False(historic.Header.IsCurrent);
            Assert.Equal(BaselineReason.Alta, historic.Header.ReasonCode);
            Assert.Equal(InformationSourceCode.ValoracionDirecta, historic.InformationSource);
            Assert.Equal(new DateOnly(2026, 9, 14), historic.InformationDate);
            Assert.Equal(new DateOnly(2026, 9, 14), historic.BarthelDate);
            Assert.Equal(Enum.GetValues<BaselineArea>().Order(), historic.Areas.Select(a => a.AreaCode).Order());
            Assert.Equal(BaselineTestData.NineAreas().Single(a => a.Area == BaselineArea.Movilidad).Answer,
                historic.Areas.Single(a => a.AreaCode == BaselineArea.Movilidad).Answer);
            // Los diez ítems, en el orden del formulario, suman el total.
            Assert.Equal(Enum.GetValues<BarthelItemCode>(), historic.BarthelItems.Select(i => i.ItemCode));
            Assert.Equal(BaselineTestData.FullBarthelItems(), historic.BarthelItems);
            Assert.Equal(historic.Header.BarthelTotal, historic.BarthelItems.Sum(i => i.AwardedScore));

            var current = (await ReadBaselineVersionAsync(profile, residentId, 2)).Value!;
            Assert.True(current.Header.IsCurrent);
            Assert.Equal(1, current.Header.ReplacesVersionNumber);

            var missing = await ReadBaselineVersionAsync(profile, residentId, 3);
            Assert.True(missing.Ok);
            Assert.Null(missing.Value);
        }
    }

    [Fact]
    public async Task Historial_AuxiliarDireccionYOtroCentro_NoAcceden()
    {
        var (enfermera, _, residentId) = await SeedResidentAsync();
        await SignBaselineAsync(enfermera, residentId, BaselineReason.Alta);
        var auxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, enfermera.CenterId, enfermera.UnitId);
        var direccion = await SeedFixture.AddProfileToCenterAsync(
            SystemProfile.DireccionClinica, enfermera.CenterId, enfermera.UnitId, ["CLINICAL_DETAIL_READ"]);
        var otroCentro = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);

        // Dirección Clínica solo entra por su lectura auditada, nunca por esta.
        foreach (var profile in new[] { auxiliar, direccion, otroCentro })
        {
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await ReadBaselineHistoryAsync(profile, residentId)).Error!.Code);
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await ReadBaselineVersionAsync(profile, residentId, 1)).Error!.Code);
        }

        var session = new FixedHistorialSessionIdentityProvider(auxiliar.ExternalSubject);
        var auxiliarList = await new ListClosedEvents(
                new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new SqlChangeInboxDirectory(TestDatabase.ConnectionFactory), session)
            .ExecuteAsync(new ListClosedEventsCommand(auxiliar.ProfileScopeId, auxiliar.CenterId, residentId, SystemProfile.Auxiliar));
        Assert.Equal(ApplicationFailureCode.AccessDenied, auxiliarList.Error!.Code);

        var otherCenterList = await BuildService(otroCentro.ExternalSubject).ListClosedEventsAsync(
            new ListClosedEventsCommand(otroCentro.ProfileScopeId, otroCentro.CenterId, residentId, SystemProfile.Enfermeria));
        Assert.Empty(otherCenterList.Value!);

        // La línea temporal exige que el residente esté en el ámbito del perfil pedido.
        foreach (var (profile, perfil) in new[] { (auxiliar, SystemProfile.Auxiliar), (otroCentro, SystemProfile.Enfermeria) })
        {
            var timelineSession = new FixedHistorialSessionIdentityProvider(profile.ExternalSubject);
            var scopes = new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory);
            var timeline = await new ReadResidentTimeline(
                    new FindScopeResident(new ListScopeResidents(scopes, new SqlEnfermeriaResidentDirectory(TestDatabase.ConnectionFactory), timelineSession)),
                    new SqlChangeInboxDirectory(TestDatabase.ConnectionFactory))
                .ExecuteAsync(new ReadResidentTimelineCommand(profile.ProfileScopeId, profile.CenterId, residentId, perfil));
            Assert.Equal(ApplicationFailureCode.AccessDenied, timeline.Error!.Code);
        }
    }

    /// <summary>Un residente en la unidad de una enfermera con permisos de basal y de una médica.</summary>
    private static async Task<(SeededProfile Enfermera, SeededProfile Medica, ResidentId ResidentId)> SeedResidentAsync()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var enfermera = await SeedFixture.AddProfileToCenterAsync(
            SystemProfile.Enfermeria, admin.CenterId, admin.UnitId, ["BASELINE_INITIAL_COMPLETE", "BASELINE_REEVALUATE"]);
        var medica = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, admin.CenterId, admin.UnitId);
        var resident = await new SqlResidentRepository(TestDatabase.ConnectionFactory).CreateWithInitialLocationAsync(new CreateResidentInput(
            admin.AccountId, SystemProfile.Administracion, admin.CenterId, admin.UnitId,
            "Residente Historial", new DateOnly(1939, 9, 9), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));
        return (enfermera, medica, resident.ResidentId);
    }

    /// <summary>Crea, completa y firma un basal (mismo recorrido que SqlBaselineRepositoryDraftTests).</summary>
    private async Task SignBaselineAsync(SeededProfile seed, ResidentId residentId, BaselineReason reason)
    {
        var created = await _baselines.CreateDraftAsync(new CreateBaselineDraftInput(
            seed.AccountId, SystemProfile.Enfermeria, seed.CenterId, seed.UnitId, residentId, reason,
            InformationSourceCode.ValoracionDirecta, null, new DateOnly(2026, 9, 14), Guid.NewGuid()));
        var owner = new OwnedActiveDraftInput(seed.AccountId, SystemProfile.Enfermeria, seed.CenterId, residentId);
        foreach (var (area, answer) in BaselineTestData.NineAreas())
        {
            await _baselines.SaveAreaAsync(new SaveBaselineDraftAreaInput(owner, area, answer, null));
        }
        await _baselines.SaveBarthelAsync(new SaveBaselineDraftBarthelInput(owner, new DateOnly(2026, 9, 14), BaselineTestData.FullBarthelItems()));
        await _baselines.SignDraftAsync(new SignBaselineDraftInput(
            seed.AccountId, SystemProfile.Enfermeria, seed.CenterId, seed.UnitId, residentId, created.DraftId, created.DraftRevision, Guid.NewGuid()));
    }

    private static async Task<Guid> RegisterAsync(SeededProfile enfermera, ResidentId residentId, string observation) =>
        (await BuildService(enfermera.ExternalSubject).RegisterClinicalEventAsync(new RegisterClinicalEventCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, residentId, observation, DailyChangeClassification.Ordinario, null, Guid.NewGuid())))
        .Value!.EventId;

    private static async Task<PendingChangeDetail> FindDetailAsync(SeededProfile enfermera, Guid eventId) =>
        (await BuildService(enfermera.ExternalSubject).FindPendingChangeDetailAsync(
            new FindPendingChangeDetailCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId))).Value!;

    private Task<ApplicationResult<IReadOnlyList<BaselineHistoryEntry>>> ReadBaselineHistoryAsync(SeededProfile profile, ResidentId residentId) =>
        new ReadBaselineHistory(
                new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory),
                new FixedHistorialSessionIdentityProvider(profile.ExternalSubject), _baselines)
            .ExecuteAsync(new ReadBaselineHistoryCommand(profile.ProfileScopeId, profile.CenterId, residentId));

    private Task<ApplicationResult<BaselineVersionDetail?>> ReadBaselineVersionAsync(SeededProfile profile, ResidentId residentId, int version) =>
        new ReadBaselineHistory(
                new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory),
                new FixedHistorialSessionIdentityProvider(profile.ExternalSubject), _baselines)
            .ExecuteVersionAsync(new ReadBaselineVersionCommand(profile.ProfileScopeId, profile.CenterId, residentId, version));
}

file sealed class FixedHistorialSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
