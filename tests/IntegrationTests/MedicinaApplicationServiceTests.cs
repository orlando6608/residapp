using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
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
        var medical = new SqlMedicalAssessmentRepository(TestDatabase.ConnectionFactory);
        return new MedicinaApplicationService(
            new ListEscalations(scopes, changeInbox, session),
            new FindEscalationDetail(scopes, changeInbox, session),
            new ReadCurrentBaseline(
                new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory), session,
                new SqlBaselineRepository(TestDatabase.ConnectionFactory)),
            new StartMedicalAssessment(scopes, changeInbox, session, medical),
            new SaveMedicalAssessment(scopes, changeInbox, session, medical),
            new RegisterMedicalIndication(scopes, changeInbox, session, medical),
            new ListMedicalIndications(scopes, changeInbox, session));
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

    /// <summary>Evento escalado a Medicina, con una médica y una segunda enfermera en la misma unidad.</summary>
    private static async Task<(SeededProfile Enfermera, SeededProfile Companera, SeededProfile Medica, Guid EventId)> SeedEscalatedAsync()
    {
        var (enfermera, companera, eventId) = await SeedOwnEventAsync();
        var medica = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, enfermera.CenterId, enfermera.UnitId);
        var revision = await StartAndSaveAsync(enfermera, eventId);
        Assert.True((await BuildService(enfermera.ExternalSubject).EscalateClinicalEventAsync(EscalateCommand(enfermera, eventId, revision))).Ok);
        return (enfermera, companera, medica, eventId);
    }

    private static SaveMedicalAssessmentCommand SaveMedical(SeededProfile seed, Guid eventId, int revision, string? findings = "Crepitantes bibasales.") =>
        new(seed.ProfileScopeId, seed.CenterId, eventId, revision, findings, "Posible infección respiratoria.", "Se solicita radiografía.",
            null, null, null, 96, null, 91, null, null, null, null, null, null);

    private static RegisterMedicalIndicationCommand Indication(
        SeededProfile seed, Guid eventId, int revision, string? text = "Control de SpO2 cada 4 horas.", string? criterion = "Si baja de 90 %, avisar.") =>
        new(seed.ProfileScopeId, seed.CenterId, eventId, revision, text, null, criterion, null);

    /// <summary>Empieza y guarda la valoración médica; devuelve la revisión con la que se puede indicar.</summary>
    private static async Task<int> StartAndSaveMedicalAsync(SeededProfile medica, Guid eventId)
    {
        var service = BuildMedicina(medica.ExternalSubject);
        var revision = (await FindAsync(medica, eventId))!.Revision;
        var started = await service.StartMedicalAssessmentAsync(new StartMedicalAssessmentCommand(medica.ProfileScopeId, medica.CenterId, eventId, revision));
        return (await service.SaveMedicalAssessmentAsync(SaveMedical(medica, eventId, started.Value))).Value;
    }

    [Fact]
    public async Task ValoracionMedica_RegistraQuienEmpieza_ConservaElBorradorYSusVersiones_SinTocarLaDeEnfermeria()
    {
        var (_, _, medica, eventId) = await SeedEscalatedAsync();
        var service = BuildMedicina(medica.ExternalSubject);
        var before = (await FindAsync(medica, eventId))!;

        var notStarted = await service.SaveMedicalAssessmentAsync(SaveMedical(medica, eventId, before.Revision));
        var started = await service.StartMedicalAssessmentAsync(new StartMedicalAssessmentCommand(medica.ProfileScopeId, medica.CenterId, eventId, before.Revision));
        var empty = await service.SaveMedicalAssessmentAsync(new SaveMedicalAssessmentCommand(
            medica.ProfileScopeId, medica.CenterId, eventId, started.Value, " ", null, null,
            null, null, null, null, null, null, null, null, null, null, null, null));
        var saved = await service.SaveMedicalAssessmentAsync(SaveMedical(medica, eventId, started.Value));
        var stale = await service.SaveMedicalAssessmentAsync(SaveMedical(medica, eventId, started.Value, "Otra cosa."));

        Assert.Equal(ApplicationFailureCode.Conflict, notStarted.Error!.Code);
        Assert.True(started.Ok);
        Assert.Equal(ApplicationFailureCode.InvalidInput, empty.Error!.Code);
        Assert.True(saved.Ok);
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);

        var detail = (await FindAsync(medica, eventId))!;
        Assert.Equal(ClinicalEventStatus.EnValoracionMedica, detail.Status);
        Assert.True(detail.Medical.StartedByCurrentAccount);
        Assert.Equal("Crepitantes bibasales.", detail.Medical.Assessment!.Content.FindingsAndExamination);
        Assert.Equal(91, detail.Medical.Assessment.Content.Vitals.OxygenSaturationPct);
        Assert.Equal("Crepitantes en base derecha.", detail.Assessment!.Content.Findings);
        var inbox = await service.ListEscalationsAsync(new ListEscalationsCommand(medica.ProfileScopeId, medica.CenterId));
        Assert.Equal(ClinicalEventStatus.EnValoracionMedica, Assert.Single(inbox.Value!).Status);
        Assert.Equal(1, await CountAuditAsync(eventId, "MEDICAL_ASSESSMENT_SAVE"));

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.valoraciones_medicas_versiones WHERE evento_id = @EventId", new { EventId = eventId }));
    }

    [Fact]
    public async Task Indicacion_ExigeValoracionYPlan_PasaElEventoAIndicacionPendiente_YAdmiteMas()
    {
        var (_, _, medica, eventId) = await SeedEscalatedAsync();
        var service = BuildMedicina(medica.ExternalSubject);
        var revision = (await FindAsync(medica, eventId))!.Revision;
        var started = (await service.StartMedicalAssessmentAsync(new StartMedicalAssessmentCommand(medica.ProfileScopeId, medica.CenterId, eventId, revision))).Value;

        var withoutAssessment = await service.RegisterMedicalIndicationAsync(Indication(medica, eventId, started));
        var saved = (await service.SaveMedicalAssessmentAsync(SaveMedical(medica, eventId, started))).Value;
        var withoutPlan = await service.RegisterMedicalIndicationAsync(Indication(medica, eventId, saved, criterion: " "));
        var withoutText = await service.RegisterMedicalIndicationAsync(Indication(medica, eventId, saved, text: " "));
        var first = await service.RegisterMedicalIndicationAsync(Indication(medica, eventId, saved));
        var second = await service.RegisterMedicalIndicationAsync(new RegisterMedicalIndicationCommand(
            medica.ProfileScopeId, medica.CenterId, eventId, first.Value, "Pautar paracetamol si fiebre.", new DateOnly(2030, 1, 15), null, "Máximo 3 g/día."));

        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutAssessment.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutPlan.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutText.Error!.Code);
        Assert.True(first.Ok);
        Assert.True(second.Ok);

        var detail = (await FindAsync(medica, eventId))!;
        Assert.Equal(ClinicalEventStatus.ConIndicacionPendiente, detail.Status);
        Assert.Equal(2, detail.Medical.Indications.Count);
        Assert.All(detail.Medical.Indications, i => Assert.Equal(MedicalIndicationStatus.PendienteLectura, i.Status));
        Assert.Empty((await service.ListEscalationsAsync(new ListEscalationsCommand(medica.ProfileScopeId, medica.CenterId))).Value!);
        Assert.Equal(2, (await service.ListMedicalIndicationsAsync(new ListMedicalIndicationsCommand(medica.ProfileScopeId, medica.CenterId))).Value!.Count);

        // La valoración médica no se edita tras pasar a la conducta.
        var saveAfter = await service.SaveMedicalAssessmentAsync(SaveMedical(medica, eventId, detail.Revision));
        Assert.Equal(ApplicationFailureCode.Conflict, saveAfter.Error!.Code);
    }

    [Fact]
    public async Task Enfermeria_LeeYRegistraLaIndicacion_EnHitosDistintos_YMedicinaLoVe()
    {
        var (enfermera, companera, medica, eventId) = await SeedEscalatedAsync();
        var revision = await StartAndSaveMedicalAsync(medica, eventId);
        Assert.True((await BuildMedicina(medica.ExternalSubject).RegisterMedicalIndicationAsync(Indication(medica, eventId, revision))).Ok);
        var nursing = BuildService(companera.ExternalSubject);

        var pending = Assert.Single((await nursing.ListPendingIndicationsAsync(new ListPendingIndicationsCommand(companera.ProfileScopeId, companera.CenterId))).Value!);
        var indication = pending.Indication;
        Assert.Equal(eventId, pending.EventId);
        Assert.Equal(MedicalIndicationStatus.PendienteLectura, indication.Status);

        var doneBeforeReading = await nursing.RecordIndicationProgressAsync(new RecordIndicationProgressCommand(
            companera.ProfileScopeId, companera.CenterId, eventId, indication.Id, indication.Revision, Realizada: true));
        var read = await nursing.RecordIndicationProgressAsync(new RecordIndicationProgressCommand(
            companera.ProfileScopeId, companera.CenterId, eventId, indication.Id, indication.Revision));
        var readAgain = await nursing.RecordIndicationProgressAsync(new RecordIndicationProgressCommand(
            companera.ProfileScopeId, companera.CenterId, eventId, indication.Id, indication.Revision));
        var notDoneWithoutIncident = await BuildService(enfermera.ExternalSubject).RecordIndicationProgressAsync(new RecordIndicationProgressCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, eventId, indication.Id, read.Value, Realizada: false, Incidencia: " "));

        Assert.Equal(ApplicationFailureCode.Conflict, doneBeforeReading.Error!.Code);
        Assert.True(read.Ok);
        Assert.Equal(ApplicationFailureCode.Conflict, readAgain.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, notDoneWithoutIncident.Error!.Code);
        var stillVisible = Assert.Single((await nursing.ListPendingIndicationsAsync(new ListPendingIndicationsCommand(companera.ProfileScopeId, companera.CenterId))).Value!);
        Assert.Equal(MedicalIndicationStatus.Leida, stillVisible.Indication.Status);

        var notDone = await BuildService(enfermera.ExternalSubject).RecordIndicationProgressAsync(new RecordIndicationProgressCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, eventId, indication.Id, read.Value, Realizada: false, Incidencia: "La residente rechaza el pulsioxímetro."));

        Assert.True(notDone.Ok);
        Assert.Empty((await nursing.ListPendingIndicationsAsync(new ListPendingIndicationsCommand(companera.ProfileScopeId, companera.CenterId))).Value!);
        var seenByMedicina = Assert.Single((await BuildMedicina(medica.ExternalSubject).ListMedicalIndicationsAsync(
            new ListMedicalIndicationsCommand(medica.ProfileScopeId, medica.CenterId))).Value!).Indication;
        Assert.Equal(MedicalIndicationStatus.NoRealizada, seenByMedicina.Status);
        Assert.Equal("La residente rechaza el pulsioxímetro.", seenByMedicina.Incident);
        Assert.NotNull(seenByMedicina.ReadAt);
        Assert.Equal(1, await CountAuditAsync(indication.Id, "MEDICAL_INDICATION_READ"));
        Assert.Equal(1, await CountAuditAsync(indication.Id, "MEDICAL_INDICATION_NOT_DONE"));
    }

    [Fact]
    public async Task Indicaciones_FueraDeAmbitoOConElPerfilEquivocado_SeDeniegan()
    {
        var (enfermera, _, medica, eventId) = await SeedEscalatedAsync();
        var revision = await StartAndSaveMedicalAsync(medica, eventId);
        Assert.True((await BuildMedicina(medica.ExternalSubject).RegisterMedicalIndicationAsync(Indication(medica, eventId, revision))).Ok);
        var indication = (await FindAsync(medica, eventId))!.Medical.Indications.Single();
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);

        var byOutsider = await BuildService(outsider.ExternalSubject).RecordIndicationProgressAsync(new RecordIndicationProgressCommand(
            outsider.ProfileScopeId, outsider.CenterId, eventId, indication.Id, indication.Revision));
        var byMedica = await BuildService(medica.ExternalSubject).RecordIndicationProgressAsync(new RecordIndicationProgressCommand(
            medica.ProfileScopeId, medica.CenterId, eventId, indication.Id, indication.Revision));
        var indicationByNurse = await BuildMedicina(enfermera.ExternalSubject).RegisterMedicalIndicationAsync(Indication(enfermera, eventId, revision + 1));
        var outsiderList = await BuildService(outsider.ExternalSubject).ListPendingIndicationsAsync(new ListPendingIndicationsCommand(outsider.ProfileScopeId, outsider.CenterId));

        Assert.Equal(ApplicationFailureCode.AccessDenied, byOutsider.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byMedica.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, indicationByNurse.Error!.Code);
        Assert.Empty(outsiderList.Value!);
    }

    [Fact]
    public async Task LaBaseDeDatos_RechazaSaltosDeEstadoYBorrados_DelCicloMedico()
    {
        var (_, _, medica, eventId) = await SeedEscalatedAsync();
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var skipped = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.eventos_asistenciales SET estado_codigo = 'CON_INDICACION_PENDIENTE', revision = revision + 1, valoracion_medica_iniciada_por_cuenta_id = @AccountId, valoracion_medica_iniciada_en = SYSUTCDATETIME() WHERE id = @EventId",
            new { EventId = eventId, AccountId = medica.AccountId.Value }));
        Assert.Contains("CLINICAL_EVENT_TRANSITION_INVALID", skipped.Message);

        var revision = await StartAndSaveMedicalAsync(medica, eventId);
        Assert.True((await BuildMedicina(medica.ExternalSubject).RegisterMedicalIndicationAsync(Indication(medica, eventId, revision))).Ok);

        foreach (var (sql, expected) in new[]
        {
            ("UPDATE dbo.indicaciones_medicas SET estado_codigo = 'REALIZADA', revision = revision + 1, leida_por_cuenta_id = emitida_por_cuenta_id, leida_en = emitida_en, resuelta_por_cuenta_id = emitida_por_cuenta_id, resuelta_en = emitida_en WHERE evento_id = @EventId", "MEDICAL_INDICATION_TRANSITION_INVALID"),
            ("UPDATE dbo.indicaciones_medicas SET texto = 'x', revision = revision + 1, estado_codigo = 'LEIDA', leida_por_cuenta_id = emitida_por_cuenta_id, leida_en = emitida_en WHERE evento_id = @EventId", "MEDICAL_INDICATION_TRANSITION_INVALID"),
            ("DELETE FROM dbo.indicaciones_medicas WHERE evento_id = @EventId", "MEDICAL_INDICATION_DELETE_FORBIDDEN"),
            ("UPDATE dbo.valoraciones_medicas_versiones SET valoracion = 'x' WHERE evento_id = @EventId", "MEDICAL_ASSESSMENT_VERSION_IMMUTABLE"),
            ("DELETE FROM dbo.valoraciones_medicas WHERE evento_id = @EventId", "MEDICAL_ASSESSMENT_DELETE_FORBIDDEN"),
            ("UPDATE dbo.eventos_asistenciales SET estado_codigo = 'EN_VALORACION_MEDICA', revision = revision + 1 WHERE id = @EventId", "CLINICAL_EVENT_TRANSITION_INVALID"),
        })
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, new { EventId = eventId }));
            Assert.Contains(expected, ex.Message);
        }
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
