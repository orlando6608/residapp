using Dapper;
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

    /// <summary>Historia 8 de Medicina (MED-21): con permiso de basal, Medicina crea, completa y firma el basal con
    /// el mismo módulo que Enfermería, y la versión queda firmada por Medicina.</summary>
    [Fact]
    public async Task Medicina_ConPermisoDeBasal_CreaCompletaYFirmaElBasal()
    {
        var (seed, resident) = await SeedResidentWithProfileAsync(SystemProfile.Medicina,
            [ResidentBaselinePermission.BaselineInitialComplete.ToCode(), ResidentBaselinePermission.BaselineReevaluate.ToCode()]);
        var service = BuildService(seed.ExternalSubject);
        var residentId = resident.ResidentId;

        Assert.True((await service.CanManageBaselineAsync(seed.ProfileScopeId, seed.CenterId, residentId)).Value);
        Assert.True((await service.CreateBaselineDraftAsync(DraftCommand(seed, residentId, BaselineReason.Alta))).Ok);
        foreach (var (area, answer) in BaselineTestData.NineAreas())
        {
            Assert.True((await service.SaveBaselineDraftAreaAsync(
                new SaveBaselineDraftAreaCommand(seed.ProfileScopeId, seed.CenterId, residentId, area, answer, null))).Ok);
        }
        Assert.True((await service.SaveBaselineDraftBarthelAsync(new SaveBaselineDraftBarthelCommand(
            seed.ProfileScopeId, seed.CenterId, residentId, new DateOnly(2026, 9, 14), BaselineTestData.FullBarthelItems()))).Ok);

        var draft = (await service.LoadBaselineDraftAsync(new LoadBaselineDraftCommand(seed.ProfileScopeId, seed.CenterId, residentId))).Value!;
        var signed = await service.SignBaselineAsync(new SignBaselineCommand(
            seed.ProfileScopeId, seed.CenterId, residentId, draft.Id, draft.DraftRevision, Guid.NewGuid()));
        Assert.True(signed.Ok, signed.Error?.Message);
        Assert.Equal(1, signed.Value!.VersionNumber);

        var history = await new SqlBaselineRepository(TestDatabase.ConnectionFactory)
            .ReadHistoryAsync(new ReadCurrentBaselineSummaryInput(seed.CenterId, residentId));
        var version = Assert.Single(history);
        Assert.Equal(SystemProfile.Medicina, version.SignedByProfile);
        Assert.True(version.IsCurrent);
    }

    /// <summary>Historia 8 de Medicina: sin permiso no se ofrece ni se crea el basal, y el permiso de basal nunca
    /// concede el alta administrativa de residentes.</summary>
    [Fact]
    public async Task Medicina_SinPermisoNoGestionaElBasal_YConPermisoNoDaDeAltaResidentes()
    {
        var (withoutPermission, resident) = await SeedResidentWithProfileAsync(SystemProfile.Medicina, []);
        var service = BuildService(withoutPermission.ExternalSubject);
        Assert.False((await service.CanManageBaselineAsync(withoutPermission.ProfileScopeId, withoutPermission.CenterId, resident.ResidentId)).Value);
        Assert.Equal(ApplicationFailureCode.AccessDenied,
            (await service.CreateBaselineDraftAsync(DraftCommand(withoutPermission, resident.ResidentId, BaselineReason.Alta))).Error!.Code);

        var (withPermission, _) = await SeedResidentWithProfileAsync(SystemProfile.Medicina,
            [ResidentBaselinePermission.BaselineInitialComplete.ToCode(), ResidentBaselinePermission.BaselineReevaluate.ToCode()]);
        var created = await BuildService(withPermission.ExternalSubject).CreateResidentAsync(Command(withPermission));
        Assert.Equal(ApplicationFailureCode.AccessDenied, created.Error!.Code);

        var (auxiliar, auxResident) = await SeedResidentWithProfileAsync(SystemProfile.Auxiliar, []);
        Assert.False((await BuildService(auxiliar.ExternalSubject)
            .CanManageBaselineAsync(auxiliar.ProfileScopeId, auxiliar.CenterId, auxResident.ResidentId)).Value);
    }

    /// <summary>DIR-05: Dirección Clínica ve el contenido del basal vigente solo tras dejar su auditoría; el historial sigue sin
    /// contenido; sin el permiso clínico no hay contenido ni auditoría.</summary>
    [Fact]
    public async Task Direccion_LeeElContenidoDelBasalVigenteAuditado_ElHistorialSoloCabeceras_YSinPermisoNada()
    {
        var (nursing, resident) = await SeedResidentWithProfileAsync(SystemProfile.Enfermeria,
            [ResidentBaselinePermission.BaselineInitialComplete.ToCode()]);
        var nursingService = BuildService(nursing.ExternalSubject);
        var residentId = resident.ResidentId;
        Assert.True((await nursingService.CreateBaselineDraftAsync(DraftCommand(nursing, residentId, BaselineReason.Alta))).Ok);
        foreach (var (area, answer) in BaselineTestData.NineAreas())
        {
            Assert.True((await nursingService.SaveBaselineDraftAreaAsync(
                new SaveBaselineDraftAreaCommand(nursing.ProfileScopeId, nursing.CenterId, residentId, area, answer, null))).Ok);
        }
        Assert.True((await nursingService.SaveBaselineDraftBarthelAsync(new SaveBaselineDraftBarthelCommand(
            nursing.ProfileScopeId, nursing.CenterId, residentId, new DateOnly(2026, 9, 14), BaselineTestData.FullBarthelItems()))).Ok);
        var draft = (await nursingService.LoadBaselineDraftAsync(new LoadBaselineDraftCommand(nursing.ProfileScopeId, nursing.CenterId, residentId))).Value!;
        Assert.True((await nursingService.SignBaselineAsync(new SignBaselineCommand(
            nursing.ProfileScopeId, nursing.CenterId, residentId, draft.Id, draft.DraftRevision, Guid.NewGuid()))).Ok);

        var direction = await SeedFixture.AddProfileToCenterAsync(
            SystemProfile.DireccionClinica, nursing.CenterId, nursing.UnitId, [ResidentBaselinePermission.ClinicalDetailRead.ToCode()]);
        var withoutPermission = await SeedFixture.AddProfileToCenterAsync(SystemProfile.DireccionClinica, nursing.CenterId, nursing.UnitId, []);
        ReadDirectionBaselineCommand Read(SeededProfile who, string type) => new(
            who.ProfileScopeId, who.CenterId, residentId, type, "CONTINUIDAD_ASISTENCIAL", Guid.NewGuid(), "Revisión de continuidad (prueba).");
        async Task<int> AuditedReadsAsync(string type)
        {
            using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
            return await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE residente_id = @Id AND accion_codigo = 'CLINICAL_DETAIL_READ' AND tipo_recurso = @type",
                new { Id = residentId.Value, type });
        }

        var denied = await BuildService(withoutPermission.ExternalSubject).ReadDirectionBaselineAsync(Read(withoutPermission, "BASELINE_CURRENT"));
        Assert.Equal(ApplicationFailureCode.AccessDenied, denied.Error!.Code);
        Assert.Equal(0, await AuditedReadsAsync("BASELINE_CURRENT"));

        var directionService = BuildService(direction.ExternalSubject);
        var history = await directionService.ReadDirectionBaselineAsync(Read(direction, "BASELINE_HISTORY"));
        Assert.True(history.Ok, history.Error?.Message);
        Assert.Single(history.Value!.Headers);
        Assert.Null(history.Value.Content);
        Assert.Equal(0, await AuditedReadsAsync("BASELINE_CURRENT"));

        var current = await directionService.ReadDirectionBaselineAsync(Read(direction, "BASELINE_CURRENT"));
        Assert.True(current.Ok, current.Error?.Message);
        Assert.Equal(1, Assert.Single(current.Value!.Headers).VersionNumber);
        Assert.Equal(9, current.Value.Content!.Areas.Count);
        Assert.Equal(100, current.Value.Content.Header.BarthelTotal);
        Assert.Equal(1, await AuditedReadsAsync("BASELINE_CURRENT"));
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
