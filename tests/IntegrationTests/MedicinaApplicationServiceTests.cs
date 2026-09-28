using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Enfermeria;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.EnfermeriaApplicationServiceTests;

namespace ResidApp.IntegrationTests;

/// <summary>Historia 1 de Medicina en lectura (MED-01 a MED-03): los escalados de Enfermería llegan a la
/// bandeja de Medicina de su unidad, con el detalle de solo lectura, y un ámbito de Medicina nunca ve
/// eventos que no se le han escalado.</summary>
public class MedicinaApplicationServiceTests
{
    private static MedicinaApplicationService BuildMedicina(string externalSubject)
    {
        var scopes = new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory);
        var session = new FixedMedicinaSessionIdentityProvider(externalSubject);
        var changeInbox = new SqlChangeInboxDirectory(TestDatabase.ConnectionFactory);
        return new MedicinaApplicationService(
            new ListEscalations(scopes, changeInbox, session),
            new FindEscalationDetail(scopes, changeInbox, session),
            new ReadCurrentBaseline(
                new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory), session,
                new SqlBaselineRepository(TestDatabase.ConnectionFactory)));
    }

    private static async Task<PendingChangeDetail?> FindAsync(SeededProfile seed, Guid eventId) =>
        (await BuildMedicina(seed.ExternalSubject).FindEscalationDetailAsync(
            new FindEscalationDetailCommand(seed.ProfileScopeId, seed.CenterId, eventId))).Value;

    [Fact]
    public async Task Escalado_LlegaALaBandejaDeMedicinaDeSuUnidad_ConElDetalleCompleto()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var (_, _, notEscalatedId) = await SeedOwnEventAsync();
        var medica = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, enfermera.CenterId, enfermera.UnitId);
        var revision = await StartAndSaveAsync(enfermera, eventId);
        Assert.True((await BuildService(enfermera.ExternalSubject).EscalateClinicalEventAsync(
            EscalateCommand(enfermera, eventId, revision, "Disnea progresiva."))).Ok);

        var inbox = await BuildMedicina(medica.ExternalSubject).ListEscalationsAsync(new ListEscalationsCommand(medica.ProfileScopeId, medica.CenterId));

        var item = Assert.Single(inbox.Value!);
        Assert.Equal(eventId, item.EventId);
        Assert.Equal("Disnea progresiva.", item.Reason);
        Assert.Equal(93, item.Vitals!.OxygenSaturationPct);
        Assert.Equal("Se incorpora a 45º.", item.Actions);
        Assert.Equal(ClinicalEventStatus.EscaladoMedicina, item.Status);

        var detail = (await FindAsync(medica, eventId))!;
        Assert.Equal("Tos productiva desde la mañana.", detail.Observation);
        Assert.Equal("Crepitantes en base derecha.", detail.Assessment!.Content.Findings);
        Assert.Equal("Disnea progresiva.", detail.Escalation!.Reason);
        Assert.False(detail.Escalation.EscalatedByCurrentAccount);

        // Un evento no escalado de la misma unidad no es visible para Medicina.
        Assert.Null(await FindAsync(medica, notEscalatedId));

        var baseline = await BuildMedicina(medica.ExternalSubject).ReadCurrentBaselineAsync(
            new ReadCurrentBaselineCommand(medica.ProfileScopeId, medica.CenterId, detail.ResidentId));
        Assert.True(baseline.Ok);
    }

    [Fact]
    public async Task Escalados_NoSonVisiblesDesdeOtroCentro_NiConPerfilesQueNoSonMedicina()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var revision = await StartAndSaveAsync(enfermera, eventId);
        Assert.True((await BuildService(enfermera.ExternalSubject).EscalateClinicalEventAsync(EscalateCommand(enfermera, eventId, revision))).Ok);
        var otherCenter = await SeedFixture.CreateProfileAsync(SystemProfile.Medicina);

        var outsideInbox = await BuildMedicina(otherCenter.ExternalSubject).ListEscalationsAsync(
            new ListEscalationsCommand(otherCenter.ProfileScopeId, otherCenter.CenterId));
        var byEnfermeria = await BuildMedicina(enfermera.ExternalSubject).ListEscalationsAsync(
            new ListEscalationsCommand(enfermera.ProfileScopeId, enfermera.CenterId));
        var detailByEnfermeria = await BuildMedicina(enfermera.ExternalSubject).FindEscalationDetailAsync(
            new FindEscalationDetailCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId));

        Assert.Empty(outsideInbox.Value!);
        Assert.Null(await FindAsync(otherCenter, eventId));
        Assert.Equal(ApplicationFailureCode.AccessDenied, byEnfermeria.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, detailByEnfermeria.Error!.Code);
    }

    [Fact]
    public async Task Medicina_NoPuedeUsarLosCasosDeUsoDeEnfermeria()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var medica = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, enfermera.CenterId, enfermera.UnitId);
        var service = BuildService(medica.ExternalSubject);

        var inbox = await service.ListPendingChangesAsync(new ListPendingChangesCommand(
            medica.ProfileScopeId, medica.CenterId, Domain.Auxiliar.DailyChangeClassification.Ordinario));
        var start = await service.StartNursingAssessmentAsync(new StartNursingAssessmentCommand(medica.ProfileScopeId, medica.CenterId, eventId, 1));

        Assert.Equal(ApplicationFailureCode.AccessDenied, inbox.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, start.Error!.Code);
    }
}

file sealed class FixedMedicinaSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
