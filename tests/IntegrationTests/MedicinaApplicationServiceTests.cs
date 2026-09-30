using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Pdf;
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
        var residents = new SqlEnfermeriaResidentDirectory(TestDatabase.ConnectionFactory);
        var listScopeResidents = new ListScopeResidents(scopes, residents, session);
        return new MedicinaApplicationService(
            new ListEscalations(scopes, changeInbox, session),
            new FindEscalationDetail(scopes, changeInbox, session),
            new ReadCurrentBaseline(
                new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory), session,
                new SqlBaselineRepository(TestDatabase.ConnectionFactory)),
            new StartMedicalAssessment(scopes, changeInbox, session, medical),
            new SaveMedicalAssessment(scopes, changeInbox, session, medical),
            new RegisterMedicalIndication(scopes, changeInbox, session, medical),
            new ListMedicalIndications(scopes, changeInbox, session),
            new CloseMedicalEvent(scopes, changeInbox, session, medical),
            new StartMedicalFollowUp(scopes, changeInbox, session, medical),
            new RecordMedicalFollowUpAction(scopes, changeInbox, session, medical),
            new ListMedicalFollowUps(scopes, changeInbox, session),
            new ActivateMedicalUrgentProtocol(scopes, changeInbox, session, medical),
            new RecordMedicalUrgentProtocolEntry(scopes, changeInbox, session, medical),
            new ListMedicalUrgentProtocols(scopes, changeInbox, session),
            new SignMedicalReferralReport(scopes, changeInbox, session, medical, new ReferralReportPdfRenderer()),
            new RecordMedicalFamilyCallAttempt(scopes, changeInbox, session, medical),
            new FindResidentIdentification(scopes, changeInbox, session),
            new DownloadReferralReport(scopes, changeInbox, session, new SqlReferralReportRepository(TestDatabase.ConnectionFactory)),
            listScopeResidents,
            new FindScopeResident(listScopeResidents),
            new RegisterClinicalEvent(scopes, residents, session, new SqlClinicalEventRepository(TestDatabase.ConnectionFactory)),
            new ListClosedEvents(scopes, changeInbox, session),
            new ReadBaselineHistory(
                new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory), session,
                new SqlBaselineRepository(TestDatabase.ConnectionFactory)),
            new ReadResidentTimeline(new FindScopeResident(listScopeResidents), changeInbox));
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

    private static CloseMedicalEventCommand Close(
        SeededProfile seed, Guid eventId, int revision, Guid? operationId = null,
        FamilyCommunicationDecision? decision = FamilyCommunicationDecision.NoComunicar,
        FamilyCommunicationType? type = null, string? text = null) =>
        new(seed.ProfileScopeId, seed.CenterId, eventId, revision, operationId ?? Guid.NewGuid(), decision, type, text);

    [Fact]
    public async Task LineaTemporal_RecogeCadaHitoConSuTexto_YMedicinaSoloVeLoQueLeLlega()
    {
        var (enfermera, companera, medica, eventId) = await SeedEscalatedAsync();
        var medicina = BuildMedicina(medica.ExternalSubject);
        var revision = await StartAndSaveMedicalAsync(medica, eventId);
        revision = (await medicina.SaveMedicalAssessmentAsync(SaveMedical(medica, eventId, revision, "Crepitantes bibasales, más a la izquierda."))).Value;
        Assert.True((await medicina.RegisterMedicalIndicationAsync(Indication(medica, eventId, revision))).Ok);
        var nursing = BuildService(companera.ExternalSubject);
        var indication = Assert.Single((await nursing.ListPendingIndicationsAsync(
            new ListPendingIndicationsCommand(companera.ProfileScopeId, companera.CenterId))).Value!).Indication;
        var read = await nursing.RecordIndicationProgressAsync(new RecordIndicationProgressCommand(
            companera.ProfileScopeId, companera.CenterId, eventId, indication.Id, indication.Revision));
        Assert.True((await nursing.RecordIndicationProgressAsync(new RecordIndicationProgressCommand(
            companera.ProfileScopeId, companera.CenterId, eventId, indication.Id, read.Value, Realizada: true))).Ok);
        Assert.True((await medicina.CloseMedicalEventAsync(Close(medica, eventId, (await FindAsync(medica, eventId))!.Revision))).Ok);

        // Otro evento del mismo residente, cerrado por Enfermería sin escalar: Medicina no lo ve.
        var residentId = (await FindAsync(medica, eventId))!.ResidentId;
        var nurseOnly = (await BuildService(enfermera.ExternalSubject).RegisterClinicalEventAsync(new RegisterClinicalEventCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, residentId, "Estreñimiento de tres días.",
            Domain.Auxiliar.DailyChangeClassification.Ordinario, null, Guid.NewGuid()))).Value!.EventId;
        var nurseRevision = await StartAndSaveAsync(enfermera, nurseOnly);
        Assert.True((await BuildService(enfermera.ExternalSubject).CloseClinicalEventAsync(new CloseClinicalEventCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, nurseOnly, nurseRevision, Guid.NewGuid(),
            FamilyCommunicationDecision.NoComunicar, null, null))).Ok);

        var nursingResult = await BuildService(enfermera.ExternalSubject).ReadResidentTimelineAsync(
            new ReadResidentTimelineCommand(enfermera.ProfileScopeId, enfermera.CenterId, residentId, SystemProfile.Enfermeria));
        var medicinaResult = await medicina.ReadResidentTimelineAsync(
            new ReadResidentTimelineCommand(medica.ProfileScopeId, medica.CenterId, residentId, SystemProfile.Medicina));
        Assert.True(nursingResult.Ok, nursingResult.Error?.Message);
        Assert.True(medicinaResult.Ok, medicinaResult.Error?.Message);
        var forNursing = nursingResult.Value!;
        var forMedicina = medicinaResult.Value!;

        Assert.Equal(forNursing.OrderByDescending(e => e.At), forNursing);
        var escalated = forNursing.Where(e => e.EventId == eventId).ToList();
        Assert.Single(escalated.OfType<TimelineEntry.EventRegistered>());
        Assert.Equal("Crepitantes en base derecha.", Assert.Single(escalated.OfType<TimelineEntry.NursingAssessmentSaved>()).Content.Findings);
        Assert.Equal("Disnea progresiva pese a oxigenoterapia.", Assert.Single(escalated.OfType<TimelineEntry.Escalated>()).Reason);
        var medicalVersions = escalated.OfType<TimelineEntry.MedicalAssessmentSaved>().Select(m => m.Content.FindingsAndExamination).ToList();
        Assert.Equal(2, medicalVersions.Count);
        Assert.Contains("Crepitantes bibasales.", medicalVersions);
        Assert.Contains("Crepitantes bibasales, más a la izquierda.", medicalVersions);
        Assert.Equal("Control de SpO2 cada 4 horas.", Assert.Single(escalated.OfType<TimelineEntry.IndicationIssued>()).Text);
        Assert.Single(escalated.OfType<TimelineEntry.IndicationRead>());
        Assert.Equal(MedicalIndicationStatus.Realizada, Assert.Single(escalated.OfType<TimelineEntry.IndicationResolved>()).Status);
        Assert.Equal(SystemProfile.Medicina, Assert.Single(escalated.OfType<TimelineEntry.EventClosed>()).Profile);
        Assert.Equal(SystemProfile.Enfermeria, Assert.Single(forNursing.OfType<TimelineEntry.EventClosed>(), e => e.EventId == nurseOnly).Profile);
        Assert.NotNull(Assert.Single(forNursing.OfType<TimelineEntry.LocationStarted>()).UnitName);

        Assert.Equal(escalated.Count, forMedicina.Count(e => e.EventId == eventId));
        Assert.DoesNotContain(forMedicina, e => e.EventId == nurseOnly);
    }

    [Fact]
    public async Task Historial_EscaladoCerradoPorMedicina_LoVenMedicinaYEnfermeria()
    {
        var (enfermera, _, medica, eventId) = await SeedEscalatedAsync();
        var revision = await StartAndSaveMedicalAsync(medica, eventId);
        Assert.True((await BuildMedicina(medica.ExternalSubject).CloseMedicalEventAsync(Close(medica, eventId, revision))).Ok);
        var residentId = (await FindAsync(medica, eventId))!.ResidentId;

        var medicina = await BuildMedicina(medica.ExternalSubject).ListClosedEventsAsync(
            new ListClosedEventsCommand(medica.ProfileScopeId, medica.CenterId, residentId, SystemProfile.Medicina));
        var enfermeria = await BuildService(enfermera.ExternalSubject).ListClosedEventsAsync(
            new ListClosedEventsCommand(enfermera.ProfileScopeId, enfermera.CenterId, residentId, SystemProfile.Enfermeria));

        var item = Assert.Single(medicina.Value!);
        Assert.Equal(eventId, item.EventId);
        Assert.True(item.Escalated);
        Assert.Null(item.Context!.BaselineVersionNumber);
        Assert.NotNull(item.Context.UnitName);
        Assert.Equal(eventId, Assert.Single(enfermeria.Value!).EventId);
    }

    [Fact]
    public async Task CierreMedico_DesdeValoracion_CierraEventoYValoracion_EsIdempotente_YSaleDeLaBandeja()
    {
        var (_, _, medica, eventId) = await SeedEscalatedAsync();
        var service = BuildMedicina(medica.ExternalSubject);
        var revision = await StartAndSaveMedicalAsync(medica, eventId);
        var operationId = Guid.NewGuid();

        var withoutDecision = await service.CloseMedicalEventAsync(Close(medica, eventId, revision, decision: null));
        var stale = await service.CloseMedicalEventAsync(Close(medica, eventId, revision - 1));
        var closed = await service.CloseMedicalEventAsync(Close(medica, eventId, revision, operationId));
        var repeated = await service.CloseMedicalEventAsync(Close(medica, eventId, revision, operationId));
        var another = await service.CloseMedicalEventAsync(Close(medica, eventId, closed.Value));

        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutDecision.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        Assert.True(closed.Ok);
        Assert.Equal(closed.Value, repeated.Value);
        Assert.Equal(ApplicationFailureCode.Conflict, another.Error!.Code);

        var detail = (await FindAsync(medica, eventId))!;
        Assert.Equal(ClinicalEventStatus.Cerrado, detail.Status);
        Assert.True(detail.Closure!.ClosedByCurrentAccount);
        Assert.Equal(FamilyCommunicationDecision.NoComunicar, detail.Closure.Decision);
        Assert.Empty((await service.ListEscalationsAsync(new ListEscalationsCommand(medica.ProfileScopeId, medica.CenterId))).Value!);
        Assert.Equal(1, await CountAuditAsync(eventId, "CLINICAL_EVENT_CLOSE"));

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal("CERRADA", await connection.ExecuteScalarAsync<string>(
            "SELECT estado_codigo FROM dbo.valoraciones_medicas WHERE evento_id = @EventId", new { EventId = eventId }));
        Assert.Equal("MEDICINA", await connection.ExecuteScalarAsync<string>(
            "SELECT perfil_activo FROM dbo.eventos_auditoria WHERE recurso_id = @EventId AND accion_codigo = 'CLINICAL_EVENT_CLOSE'", new { EventId = eventId }));
    }

    [Fact]
    public async Task CierreMedico_ConIndicacionesPendientes_SiguenParaEnfermeriaYMedicina_YLaComunicacionQuedaPendiente()
    {
        var (enfermera, _, medica, eventId) = await SeedEscalatedAsync();
        var service = BuildMedicina(medica.ExternalSubject);
        var first = (await service.RegisterMedicalIndicationAsync(Indication(medica, eventId, await StartAndSaveMedicalAsync(medica, eventId)))).Value;
        var revision = (await service.RegisterMedicalIndicationAsync(Indication(medica, eventId, first, "Pautar paracetamol si fiebre."))).Value;

        var withoutText = await service.CloseMedicalEventAsync(Close(
            medica, eventId, revision, decision: FamilyCommunicationDecision.Preparar, type: FamilyCommunicationType.Ordinaria, text: " "));
        var closed = await service.CloseMedicalEventAsync(Close(
            medica, eventId, revision, decision: FamilyCommunicationDecision.Preparar, type: FamilyCommunicationType.Ordinaria,
            text: "Ha tenido algo de fiebre; el equipo la ha valorado y sigue estable."));

        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutText.Error!.Code);
        Assert.True(closed.Ok);
        Assert.Equal(ClinicalEventStatus.Cerrado, (await FindAsync(medica, eventId))!.Status);
        var medicalList = () => service.ListMedicalIndicationsAsync(new ListMedicalIndicationsCommand(medica.ProfileScopeId, medica.CenterId));
        Assert.Equal(2, (await medicalList()).Value!.Count);

        // Enfermería no cierra otra vez, pero sigue viendo las indicaciones y las puede leer y resolver.
        var nursing = BuildService(enfermera.ExternalSubject);
        var pending = (await nursing.ListPendingIndicationsAsync(new ListPendingIndicationsCommand(enfermera.ProfileScopeId, enfermera.CenterId))).Value!;
        Assert.Equal(2, pending.Count);
        Assert.All(pending, p => Assert.Equal(ClinicalEventStatus.Cerrado, p.EventStatus));
        foreach (var (item, done) in new[] { (pending[0], true), (pending[1], false) })
        {
            var read = await nursing.RecordIndicationProgressAsync(new RecordIndicationProgressCommand(
                enfermera.ProfileScopeId, enfermera.CenterId, eventId, item.Indication.Id, item.Indication.Revision));
            Assert.True((await nursing.RecordIndicationProgressAsync(new RecordIndicationProgressCommand(
                enfermera.ProfileScopeId, enfermera.CenterId, eventId, item.Indication.Id, read.Value, Realizada: done,
                Incidencia: done ? null : "No había paracetamol en el botiquín."))).Ok);
        }
        Assert.Empty((await nursing.ListPendingIndicationsAsync(new ListPendingIndicationsCommand(enfermera.ProfileScopeId, enfermera.CenterId))).Value!);

        // Medicina deja de ver la realizada, pero no la incidencia registrada después de cerrar.
        var stillVisible = Assert.Single((await medicalList()).Value!);
        Assert.Equal(MedicalIndicationStatus.NoRealizada, stillVisible.Indication.Status);
        Assert.Equal("No había paracetamol en el botiquín.", stillVisible.Indication.Incident);

        var nurseClose = await nursing.CloseClinicalEventAsync(new CloseClinicalEventCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, eventId, closed.Value, Guid.NewGuid(), FamilyCommunicationDecision.NoComunicar, null, null));
        Assert.Equal(ApplicationFailureCode.Conflict, nurseClose.Error!.Code);

        var communication = Assert.Single((await nursing.ListPendingFamilyCommunicationsAsync(
            new ListPendingFamilyCommunicationsCommand(enfermera.ProfileScopeId, enfermera.CenterId))).Value!);
        Assert.Equal(eventId, communication.EventId);
        Assert.Equal(1, await CountAuditAsync(eventId, "CLINICAL_EVENT_CLOSE"));
    }

    [Fact]
    public async Task CierreMedico_SinValoracionFueraDeAmbitoOConOtroPerfil_SeRechaza()
    {
        var (enfermera, _, medica, eventId) = await SeedEscalatedAsync();
        var service = BuildMedicina(medica.ExternalSubject);
        var revision = (await FindAsync(medica, eventId))!.Revision;
        var notStarted = await service.CloseMedicalEventAsync(Close(medica, eventId, revision));
        var started = (await service.StartMedicalAssessmentAsync(new StartMedicalAssessmentCommand(medica.ProfileScopeId, medica.CenterId, eventId, revision))).Value;
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Medicina);

        var withoutAssessment = await service.CloseMedicalEventAsync(Close(medica, eventId, started));
        var byOutsider = await BuildMedicina(outsider.ExternalSubject).CloseMedicalEventAsync(Close(outsider, eventId, started));
        var byNurse = await BuildMedicina(enfermera.ExternalSubject).CloseMedicalEventAsync(Close(enfermera, eventId, started));

        Assert.Equal(ApplicationFailureCode.Conflict, notStarted.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutAssessment.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byOutsider.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byNurse.Error!.Code);
        Assert.Equal(ClinicalEventStatus.EnValoracionMedica, (await FindAsync(medica, eventId))!.Status);

        // En BD: un escalado no se cierra sin pasar por la valoración médica, y un evento cerrado no se reabre.
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var (_, _, otherMedica, escalatedId) = await SeedEscalatedAsync();
        var skipped = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.eventos_asistenciales SET estado_codigo = 'CERRADO', revision = revision + 1, cerrado_por_cuenta_id = @AccountId, cerrado_en = SYSUTCDATETIME(), comunicacion_familiar_codigo = 'NO_COMUNICAR' WHERE id = @EventId",
            new { EventId = escalatedId, AccountId = otherMedica.AccountId.Value }));
        Assert.Contains("CLINICAL_EVENT_TRANSITION_INVALID", skipped.Message);

        var closedRevision = (await service.CloseMedicalEventAsync(Close(medica, eventId, (await service.SaveMedicalAssessmentAsync(SaveMedical(medica, eventId, started))).Value))).Value;
        Assert.True(closedRevision > 0);
        var reopened = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.eventos_asistenciales SET estado_codigo = 'CON_INDICACION_PENDIENTE', revision = revision + 1, cerrado_por_cuenta_id = NULL, cerrado_en = NULL, comunicacion_familiar_codigo = NULL WHERE id = @EventId",
            new { EventId = eventId }));
        Assert.Contains("CLINICAL_EVENT_TRANSITION_INVALID", reopened.Message);
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

    /// <summary>Residente en la unidad de dos médicas y una enfermera, con un evento propio de la primera
    /// médica (MED-18), prioritario y con datos clínicos.</summary>
    private static async Task<(SeededProfile Medica, SeededProfile OtraMedica, SeededProfile Enfermera, ResidentId ResidentId, Guid EventId)> SeedOwnMedicalEventAsync()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var medica = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, adminSeed.CenterId, adminSeed.UnitId);
        var otraMedica = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, adminSeed.CenterId, adminSeed.UnitId);
        var enfermera = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, adminSeed.CenterId, adminSeed.UnitId);
        var resident = await new SqlResidentRepository(TestDatabase.ConnectionFactory).CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Evento Propio Medicina", new DateOnly(1943, 3, 3), Domain.Residents.DocumentedSexCode.Hombre,
            null, null, null, null, null, Guid.NewGuid()));
        var registered = await BuildMedicina(medica.ExternalSubject).RegisterClinicalEventAsync(new RegisterClinicalEventCommand(
            medica.ProfileScopeId, medica.CenterId, resident.ResidentId, "Soplo sistólico no conocido en la exploración.",
            Domain.Auxiliar.DailyChangeClassification.Prioritario, "TA 150/90.", Guid.NewGuid(), SystemProfile.Medicina));
        Assert.True(registered.Ok);
        return (medica, otraMedica, enfermera, resident.ResidentId, registered.Value!.EventId);
    }

    [Fact]
    public async Task EventoPropioMedicina_NaceEnValoracion_EntraEnSuBandeja_YNoEnLasDeEnfermeria()
    {
        var (medica, otraMedica, enfermera, _, eventId) = await SeedOwnMedicalEventAsync();

        var item = Assert.Single((await BuildMedicina(otraMedica.ExternalSubject).ListEscalationsAsync(
            new ListEscalationsCommand(otraMedica.ProfileScopeId, otraMedica.CenterId))).Value!);
        Assert.Equal(eventId, item.EventId);
        Assert.Null(item.Reason);
        Assert.Equal("Soplo sistólico no conocido en la exploración.", item.Observation);
        Assert.Equal(ClinicalEventStatus.EnValoracionMedica, item.Status);

        var detail = (await FindAsync(otraMedica, eventId))!;
        Assert.Equal(ClinicalEventOrigin.EventoMedicina, detail.Origin);
        Assert.Equal(SystemProfile.Medicina, detail.AuthorProfile);
        Assert.Null(detail.Escalation);
        Assert.Null(detail.Assessment);
        Assert.Equal("TA 150/90.", detail.ClinicalData);
        Assert.NotNull(detail.Medical.StartedAt);
        Assert.False(detail.Medical.StartedByCurrentAccount);
        Assert.True((await FindAsync(medica, eventId))!.Medical.StartedByCurrentAccount);

        // Enfermería lo ve en el detalle (llega desde sus indicaciones), pero no en sus bandejas ni lo valora.
        var nursing = BuildService(enfermera.ExternalSubject);
        foreach (var classification in new[] { Domain.Auxiliar.DailyChangeClassification.Ordinario, Domain.Auxiliar.DailyChangeClassification.Prioritario })
        {
            Assert.Empty((await nursing.ListPendingChangesAsync(new ListPendingChangesCommand(enfermera.ProfileScopeId, enfermera.CenterId, classification))).Value!);
        }
        var nurseDetail = (await nursing.FindPendingChangeDetailAsync(new FindPendingChangeDetailCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId))).Value!;
        Assert.Equal(ClinicalEventOrigin.EventoMedicina, nurseDetail.Origin);
        var nurseStart = await nursing.StartNursingAssessmentAsync(new StartNursingAssessmentCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId, detail.Revision));
        Assert.False(nurseStart.Ok);
    }

    [Fact]
    public async Task EventoPropioMedicina_OtraMedicaLoContinua_IndicaYCierra_YEnfermeriaVeLaIndicacion()
    {
        var (medica, otraMedica, enfermera, _, eventId) = await SeedOwnMedicalEventAsync();
        var otherService = BuildMedicina(otraMedica.ExternalSubject);

        var saved = await otherService.SaveMedicalAssessmentAsync(SaveMedical(otraMedica, eventId, (await FindAsync(otraMedica, eventId))!.Revision));
        var indicated = await otherService.RegisterMedicalIndicationAsync(Indication(otraMedica, eventId, saved.Value));
        Assert.True(saved.Ok);
        Assert.True(indicated.Ok);

        var pending = Assert.Single((await BuildService(enfermera.ExternalSubject).ListPendingIndicationsAsync(
            new ListPendingIndicationsCommand(enfermera.ProfileScopeId, enfermera.CenterId))).Value!);
        Assert.Equal(eventId, pending.EventId);

        var closed = await BuildMedicina(medica.ExternalSubject).CloseMedicalEventAsync(Close(medica, eventId, indicated.Value));
        Assert.True(closed.Ok);
        var detail = (await FindAsync(medica, eventId))!;
        Assert.Equal(ClinicalEventStatus.Cerrado, detail.Status);
        Assert.NotNull(detail.Closure);
        Assert.Empty((await otherService.ListEscalationsAsync(new ListEscalationsCommand(otraMedica.ProfileScopeId, otraMedica.CenterId))).Value!);
    }

    [Fact]
    public async Task EventoPropioMedicina_ActivaElProtocoloUrgente_ComoUnEscalado()
    {
        var (medica, _, _, _, eventId) = await SeedOwnMedicalEventAsync();
        var service = BuildMedicina(medica.ExternalSubject);

        var saved = await service.SaveMedicalAssessmentAsync(SaveMedical(medica, eventId, (await FindAsync(medica, eventId))!.Revision));
        var activated = await service.ActivateUrgentProtocolAsync(Activate(medica, eventId, saved.Value));

        Assert.True(activated.Ok);
        Assert.Equal(ClinicalEventStatus.ProtocoloUrgenteMedico, (await FindAsync(medica, eventId))!.Status);
        Assert.Equal(eventId, Assert.Single(await ProtocolsAsync(medica)).EventId);
    }

    [Fact]
    public async Task EventoPropioMedicina_ConOtroPerfilOFueraDeAmbito_SeDeniega_YLaListaDeResidentesEsLaDeSuAmbito()
    {
        var (medica, _, enfermera, residentId, _) = await SeedOwnMedicalEventAsync();
        var auxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, medica.CenterId, medica.UnitId);
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Medicina);
        RegisterClinicalEventCommand Register(SeededProfile seed, SystemProfile profile) => new(
            seed.ProfileScopeId, seed.CenterId, residentId, "Observación.", Domain.Auxiliar.DailyChangeClassification.Ordinario, null,
            Guid.NewGuid(), profile);

        var byNurseAsMedicina = await BuildService(enfermera.ExternalSubject).RegisterClinicalEventAsync(Register(enfermera, SystemProfile.Medicina));
        var byMedicaAsEnfermeria = await BuildMedicina(medica.ExternalSubject).RegisterClinicalEventAsync(Register(medica, SystemProfile.Enfermeria));
        var byAuxiliar = await BuildMedicina(auxiliar.ExternalSubject).RegisterClinicalEventAsync(Register(auxiliar, SystemProfile.Auxiliar));
        var byOutsider = await BuildMedicina(outsider.ExternalSubject).RegisterClinicalEventAsync(Register(outsider, SystemProfile.Medicina));

        Assert.Equal(ApplicationFailureCode.AccessDenied, byNurseAsMedicina.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byMedicaAsEnfermeria.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byAuxiliar.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byOutsider.Error!.Code);

        var residents = await BuildMedicina(medica.ExternalSubject).ListScopeResidentsAsync(
            new ListScopeResidentsCommand(medica.ProfileScopeId, medica.CenterId, SystemProfile.Medicina));
        var nurseAsMedicina = await BuildMedicina(enfermera.ExternalSubject).ListScopeResidentsAsync(
            new ListScopeResidentsCommand(enfermera.ProfileScopeId, enfermera.CenterId, SystemProfile.Medicina));
        var auxiliarList = await BuildMedicina(auxiliar.ExternalSubject).ListScopeResidentsAsync(
            new ListScopeResidentsCommand(auxiliar.ProfileScopeId, auxiliar.CenterId, SystemProfile.Auxiliar));
        Assert.Equal(residentId, Assert.Single(residents.Value!).ResidentId);
        Assert.Equal(ApplicationFailureCode.AccessDenied, nurseAsMedicina.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, auxiliarList.Error!.Code);
        Assert.Empty((await BuildMedicina(outsider.ExternalSubject).ListScopeResidentsAsync(
            new ListScopeResidentsCommand(outsider.ProfileScopeId, outsider.CenterId, SystemProfile.Medicina))).Value!);
    }

    private static StartMedicalFollowUpCommand StartFollowUp(
        SeededProfile seed, Guid eventId, int revision, DateOnly? dueDate = null, string? criterion = "Tras la radiografía.",
        string? objective = "Decidir antibiótico según la radiografía.") =>
        new(seed.ProfileScopeId, seed.CenterId, eventId, revision, dueDate, criterion, objective);

    private static RecordMedicalFollowUpActionCommand FollowUpAction(
        SeededProfile seed, Guid eventId, int revision, FollowUpActionType type, string? text = null, DateOnly? dueDate = null,
        string? criterion = null, string? incomingTeam = null, Guid? transferId = null) =>
        new(seed.ProfileScopeId, seed.CenterId, eventId, revision, type, text, dueDate, criterion, incomingTeam, transferId);

    private static Task<ApplicationResult<IReadOnlyList<MedicalFollowUpSummary>>> ListFollowUpsAsync(SeededProfile seed) =>
        BuildMedicina(seed.ExternalSubject).ListMedicalFollowUpsAsync(new ListMedicalFollowUpsCommand(seed.ProfileScopeId, seed.CenterId));

    [Fact]
    public async Task SeguimientoMedico_ExigeValoracionObjetivoYPlan_PasaAEnSeguimiento_YCambiaDeBandeja()
    {
        var (_, _, medica, eventId) = await SeedEscalatedAsync();
        var service = BuildMedicina(medica.ExternalSubject);
        var revision = (await FindAsync(medica, eventId))!.Revision;
        var started = (await service.StartMedicalAssessmentAsync(new StartMedicalAssessmentCommand(medica.ProfileScopeId, medica.CenterId, eventId, revision))).Value;

        var withoutAssessment = await service.StartMedicalFollowUpAsync(StartFollowUp(medica, eventId, started));
        var saved = (await service.SaveMedicalAssessmentAsync(SaveMedical(medica, eventId, started))).Value;
        var withoutObjective = await service.StartMedicalFollowUpAsync(StartFollowUp(medica, eventId, saved, objective: " "));
        var withoutPlan = await service.StartMedicalFollowUpAsync(StartFollowUp(medica, eventId, saved, criterion: " "));
        var stale = await service.StartMedicalFollowUpAsync(StartFollowUp(medica, eventId, saved - 1));
        var ok = await service.StartMedicalFollowUpAsync(StartFollowUp(medica, eventId, saved, new DateOnly(2030, 3, 1)));

        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutAssessment.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutObjective.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutPlan.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        Assert.True(ok.Ok);

        var detail = (await FindAsync(medica, eventId))!;
        Assert.Equal(ClinicalEventStatus.EnSeguimientoMedico, detail.Status);
        var followUp = detail.Medical.FollowUp!;
        Assert.Equal("Decidir antibiótico según la radiografía.", followUp.Objective);
        Assert.Equal(new DateOnly(2030, 3, 1), followUp.Tracking.DueDate);
        Assert.Equal("Tras la radiografía.", followUp.Tracking.Criterion);
        Assert.True(followUp.Tracking.StartedByCurrentAccount);
        Assert.Null(detail.FollowUp);

        Assert.Empty((await service.ListEscalationsAsync(new ListEscalationsCommand(medica.ProfileScopeId, medica.CenterId))).Value!);
        var item = Assert.Single((await ListFollowUpsAsync(medica)).Value!);
        Assert.Equal(eventId, item.EventId);
        Assert.Equal("Decidir antibiótico según la radiografía.", item.Objective);
        Assert.Null(item.LastActionType);
        Assert.Null(item.LastContinuity);
        Assert.Equal(1, await CountAuditAsync(eventId, "MEDICAL_FOLLOW_UP_START"));

        // La valoración médica no se edita durante el seguimiento.
        var saveDuring = await service.SaveMedicalAssessmentAsync(SaveMedical(medica, eventId, detail.Revision));
        Assert.Equal(ApplicationFailureCode.Conflict, saveDuring.Error!.Code);
    }

    [Fact]
    public async Task SeguimientoMedico_Acciones_ConservanAutoria_YLaRecepcionEsOpcional()
    {
        var (_, _, medica, eventId) = await SeedEscalatedAsync();
        var companero = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, medica.CenterId, medica.UnitId);
        var service = BuildMedicina(medica.ExternalSubject);
        var other = BuildMedicina(companero.ExternalSubject);
        var yesterday = DateOnly.FromDateTime(DateTime.Today).AddDays(-1);
        var revision = (await service.StartMedicalFollowUpAsync(StartFollowUp(medica, eventId, await StartAndSaveMedicalAsync(medica, eventId), yesterday))).Value;

        revision = (await service.RecordMedicalFollowUpActionAsync(FollowUpAction(medica, eventId, revision, FollowUpActionType.Actuacion, "Radiografía pendiente de informe."))).Value;
        var staleNote = await other.RecordMedicalFollowUpActionAsync(FollowUpAction(companero, eventId, revision - 1, FollowUpActionType.Actuacion, "Otra."));
        var rescheduleWithoutReason = await other.RecordMedicalFollowUpActionAsync(FollowUpAction(companero, eventId, revision, FollowUpActionType.Reprogramacion, dueDate: new DateOnly(2030, 4, 1)));
        revision = (await other.RecordMedicalFollowUpActionAsync(FollowUpAction(companero, eventId, revision, FollowUpActionType.Reprogramacion,
            "El informe llega mañana.", new DateOnly(2030, 4, 1), "Con el informe de la radiografía."))).Value;
        var keptWithoutNote = await service.RecordMedicalFollowUpActionAsync(FollowUpAction(medica, eventId, revision, FollowUpActionType.Conservacion));
        revision = keptWithoutNote.Value;
        var transferWithoutTeam = await service.RecordMedicalFollowUpActionAsync(FollowUpAction(medica, eventId, revision, FollowUpActionType.Transferencia, "Nota."));
        revision = (await service.RecordMedicalFollowUpActionAsync(FollowUpAction(medica, eventId, revision, FollowUpActionType.Transferencia,
            "Revisar el informe y decidir antibiótico.", incomingTeam: "Guardia de noche"))).Value;

        Assert.Equal(ApplicationFailureCode.Conflict, staleNote.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, rescheduleWithoutReason.Error!.Code);
        Assert.True(keptWithoutNote.Ok);
        Assert.Equal(ApplicationFailureCode.InvalidInput, transferWithoutTeam.Error!.Code);

        // Sin confirmar la recepción, el seguimiento sigue visible con la transferencia pendiente.
        var listed = Assert.Single((await ListFollowUpsAsync(companero)).Value!);
        Assert.True(listed.TransferPending);
        Assert.Equal(FollowUpActionType.Transferencia, listed.LastContinuity);
        Assert.Equal("Guardia de noche", listed.LastContinuityTeam);
        Assert.False(listed.LastContinuityByCurrentAccount);
        Assert.Equal(new DateOnly(2030, 4, 1), listed.DueDate);
        Assert.Equal("Con el informe de la radiografía.", listed.Criterion);

        var tracking = (await FindAsync(companero, eventId))!.Medical.FollowUp!.Tracking;
        var transfer = tracking.PendingTransfer!;
        Assert.Equal(new DateOnly(2030, 4, 1), tracking.DueDate);
        Assert.Equal(new[] { FollowUpActionType.Actuacion, FollowUpActionType.Reprogramacion, FollowUpActionType.Conservacion, FollowUpActionType.Transferencia },
            tracking.Actions.Select(a => a.Type));
        Assert.Equal(new[] { false, true, false, false }, tracking.Actions.Select(a => a.ByCurrentAccount));

        var received = await other.RecordMedicalFollowUpActionAsync(FollowUpAction(companero, eventId, revision, FollowUpActionType.Recepcion, transferId: transfer.Id));
        var receivedAgain = await other.RecordMedicalFollowUpActionAsync(FollowUpAction(companero, eventId, received.Value, FollowUpActionType.Recepcion, transferId: transfer.Id));
        Assert.True(received.Ok);
        Assert.Equal(ApplicationFailureCode.Conflict, receivedAgain.Error!.Code);
        Assert.Null((await FindAsync(companero, eventId))!.Medical.FollowUp!.Tracking.PendingTransfer);
        Assert.False(Assert.Single((await ListFollowUpsAsync(companero)).Value!).TransferPending);

        foreach (var code in new[] { "MEDICAL_FOLLOW_UP_NOTE", "MEDICAL_FOLLOW_UP_RESCHEDULE", "MEDICAL_FOLLOW_UP_KEEP", "MEDICAL_FOLLOW_UP_TRANSFER", "MEDICAL_FOLLOW_UP_RECEIVE" })
        {
            Assert.Equal(1, await CountAuditAsync(eventId, code));
        }
    }

    [Fact]
    public async Task SeguimientoMedico_Vencido_SigueEnLaBandeja_YSeResuelveConIndicacionOCierre()
    {
        var (enfermera, _, medica, eventId) = await SeedEscalatedAsync();
        var service = BuildMedicina(medica.ExternalSubject);
        var yesterday = DateOnly.FromDateTime(DateTime.Today).AddDays(-1);
        var revision = (await service.StartMedicalFollowUpAsync(StartFollowUp(medica, eventId, await StartAndSaveMedicalAsync(medica, eventId), yesterday, null))).Value;

        var overdue = Assert.Single((await ListFollowUpsAsync(medica)).Value!);
        Assert.Equal(yesterday, overdue.DueDate);

        // "Resolver" con una indicación: el seguimiento termina, el evento pasa a indicación pendiente y
        // no admite un segundo seguimiento médico.
        var indicated = await service.RegisterMedicalIndicationAsync(Indication(medica, eventId, revision));
        Assert.True(indicated.Ok);
        Assert.Equal(ClinicalEventStatus.ConIndicacionPendiente, (await FindAsync(medica, eventId))!.Status);
        Assert.Empty((await ListFollowUpsAsync(medica)).Value!);
        Assert.Single((await service.ListMedicalIndicationsAsync(new ListMedicalIndicationsCommand(medica.ProfileScopeId, medica.CenterId))).Value!);
        var second = await service.StartMedicalFollowUpAsync(StartFollowUp(medica, eventId, indicated.Value));
        Assert.Equal(ApplicationFailureCode.InvalidInput, second.Error!.Code);
        Assert.Equal(ClinicalEventStatus.ConIndicacionPendiente, (await FindAsync(medica, eventId))!.Status);
        var noteAfter = await service.RecordMedicalFollowUpActionAsync(FollowUpAction(medica, eventId, indicated.Value, FollowUpActionType.Actuacion, "Tarde."));
        Assert.Equal(ApplicationFailureCode.Conflict, noteAfter.Error!.Code);

        // Otro evento: desde indicación pendiente (sin seguimiento previo) sí se inicia, sus indicaciones
        // siguen visibles para Medicina mientras dura y después se cierra desde el seguimiento.
        var (_, _, otraMedica, otherEventId) = await SeedEscalatedAsync();
        var otherService = BuildMedicina(otraMedica.ExternalSubject);
        var withIndication = (await otherService.RegisterMedicalIndicationAsync(Indication(otraMedica, otherEventId, await StartAndSaveMedicalAsync(otraMedica, otherEventId)))).Value;
        var followed = (await otherService.StartMedicalFollowUpAsync(StartFollowUp(otraMedica, otherEventId, withIndication))).Value;
        Assert.Equal(ClinicalEventStatus.EnSeguimientoMedico, (await FindAsync(otraMedica, otherEventId))!.Status);
        Assert.Single((await otherService.ListMedicalIndicationsAsync(new ListMedicalIndicationsCommand(otraMedica.ProfileScopeId, otraMedica.CenterId))).Value!);

        var operationId = Guid.NewGuid();
        var closed = await otherService.CloseMedicalEventAsync(Close(otraMedica, otherEventId, followed, operationId));
        var repeated = await otherService.CloseMedicalEventAsync(Close(otraMedica, otherEventId, followed, operationId));
        Assert.True(closed.Ok);
        Assert.Equal(closed.Value, repeated.Value);
        Assert.Equal(ClinicalEventStatus.Cerrado, (await FindAsync(otraMedica, otherEventId))!.Status);
        Assert.Empty((await ListFollowUpsAsync(otraMedica)).Value!);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal("CERRADA", await connection.ExecuteScalarAsync<string>(
            "SELECT estado_codigo FROM dbo.valoraciones_medicas WHERE evento_id = @EventId", new { EventId = otherEventId }));

        // Enfermería ve el estado del evento en su detalle.
        var nurseDetail = await BuildService(enfermera.ExternalSubject).FindPendingChangeDetailAsync(
            new FindPendingChangeDetailCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId));
        Assert.Equal(ClinicalEventStatus.ConIndicacionPendiente, nurseDetail.Value!.Status);
    }

    [Fact]
    public async Task SeguimientoMedico_FueraDeAmbitoOConOtroPerfil_SeDeniega_YEnfermeriaNoPuedeConservar()
    {
        var (enfermera, _, medica, eventId) = await SeedEscalatedAsync();
        var revision = await StartAndSaveMedicalAsync(medica, eventId);
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Medicina);
        var (_, _, notEscalatedId) = await SeedOwnEventAsync();

        var byOutsider = await BuildMedicina(outsider.ExternalSubject).StartMedicalFollowUpAsync(StartFollowUp(outsider, eventId, revision));
        var byNurse = await BuildMedicina(enfermera.ExternalSubject).StartMedicalFollowUpAsync(StartFollowUp(enfermera, eventId, revision));
        var notEscalated = await BuildMedicina(medica.ExternalSubject).StartMedicalFollowUpAsync(StartFollowUp(medica, notEscalatedId, 1));
        var listByNurse = await BuildMedicina(enfermera.ExternalSubject).ListMedicalFollowUpsAsync(new ListMedicalFollowUpsCommand(enfermera.ProfileScopeId, enfermera.CenterId));

        Assert.Equal(ApplicationFailureCode.AccessDenied, byOutsider.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byNurse.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, notEscalated.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, listByNurse.Error!.Code);

        var followed = (await BuildMedicina(medica.ExternalSubject).StartMedicalFollowUpAsync(StartFollowUp(medica, eventId, revision))).Value;
        var actionByOutsider = await BuildMedicina(outsider.ExternalSubject).RecordMedicalFollowUpActionAsync(
            FollowUpAction(outsider, eventId, followed, FollowUpActionType.Actuacion, "x"));
        Assert.Equal(ApplicationFailureCode.AccessDenied, actionByOutsider.Error!.Code);
        Assert.Empty((await ListFollowUpsAsync(outsider)).Value!);

        // Conservar es solo del seguimiento médico: el caso de uso de Enfermería lo rechaza.
        var (nurse, _, ownEventId) = await SeedOwnEventAsync();
        var nursing = BuildService(nurse.ExternalSubject);
        var nurseRevision = await StartAndSaveAsync(nurse, ownEventId);
        var nurseFollowUp = (await nursing.StartFollowUpAsync(new StartFollowUpCommand(
            nurse.ProfileScopeId, nurse.CenterId, ownEventId, nurseRevision, new DateOnly(2030, 1, 1), null, null))).Value;
        var keep = await nursing.RecordFollowUpActionAsync(new RecordFollowUpActionCommand(
            nurse.ProfileScopeId, nurse.CenterId, ownEventId, nurseFollowUp, FollowUpActionType.Conservacion, "Me lo quedo."));
        Assert.Equal(ApplicationFailureCode.InvalidInput, keep.Error!.Code);
    }

    [Fact]
    public async Task LaBaseDeDatos_ProtegeElSeguimientoMedico()
    {
        var (_, _, medica, eventId) = await SeedEscalatedAsync();
        var service = BuildMedicina(medica.ExternalSubject);
        var revision = (await service.StartMedicalFollowUpAsync(StartFollowUp(medica, eventId, await StartAndSaveMedicalAsync(medica, eventId)))).Value;
        Assert.True((await service.RecordMedicalFollowUpActionAsync(FollowUpAction(medica, eventId, revision, FollowUpActionType.Actuacion, "Revisión."))).Ok);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        foreach (var (sql, expected) in new[]
        {
            ("UPDATE dbo.seguimientos_medicos SET objetivo = 'x' WHERE evento_id = @EventId", "MEDICAL_FOLLOW_UP_IMMUTABLE"),
            ("DELETE FROM dbo.seguimientos_medicos WHERE evento_id = @EventId", "MEDICAL_FOLLOW_UP_IMMUTABLE"),
            ("UPDATE a SET texto = 'x' FROM dbo.seguimiento_medico_acciones a JOIN dbo.seguimientos_medicos s ON s.id = a.seguimiento_id WHERE s.evento_id = @EventId", "MEDICAL_FOLLOW_UP_ACTION_IMMUTABLE"),
            ("DELETE a FROM dbo.seguimiento_medico_acciones a JOIN dbo.seguimientos_medicos s ON s.id = a.seguimiento_id WHERE s.evento_id = @EventId", "MEDICAL_FOLLOW_UP_ACTION_IMMUTABLE"),
            ("UPDATE dbo.eventos_asistenciales SET estado_codigo = 'EN_VALORACION_MEDICA', revision = revision + 1 WHERE id = @EventId", "CLINICAL_EVENT_TRANSITION_INVALID"),
            ("UPDATE dbo.eventos_asistenciales SET estado_codigo = 'EN_SEGUIMIENTO', revision = revision + 1 WHERE id = @EventId", "CLINICAL_EVENT_TRANSITION_INVALID"),
            ("INSERT INTO dbo.seguimientos_medicos (id, evento_id, residente_id, centro_id, fecha_prevista, criterio, objetivo, iniciado_por_cuenta_id, iniciado_en) SELECT NEWID(), id, residente_id, centro_id, '20300101', NULL, 'Otro', @AccountId, SYSUTCDATETIME() FROM dbo.eventos_asistenciales WHERE id = @EventId", "UX_segm_evento"),
            ("INSERT INTO dbo.seguimiento_medico_acciones (id, seguimiento_id, tipo_codigo, texto, registrado_por_cuenta_id, registrado_en) SELECT NEWID(), id, 'TRANSFERENCIA', 'Sin equipo', @AccountId, SYSUTCDATETIME() FROM dbo.seguimientos_medicos WHERE evento_id = @EventId", "CK_sma_tipo"),
        })
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, new { EventId = eventId, AccountId = medica.AccountId.Value }));
            Assert.Contains(expected, ex.Message);
        }

        // Un escalado no salta a seguimiento médico ni a protocolo urgente sin pasar por la valoración médica.
        var (_, _, _, escalatedId) = await SeedEscalatedAsync();
        var skipped = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.eventos_asistenciales SET estado_codigo = 'EN_SEGUIMIENTO_MEDICO', revision = revision + 1, valoracion_medica_iniciada_por_cuenta_id = @AccountId, valoracion_medica_iniciada_en = SYSUTCDATETIME() WHERE id = @EventId",
            new { EventId = escalatedId, AccountId = medica.AccountId.Value }));
        Assert.Contains("CLINICAL_EVENT_TRANSITION_INVALID", skipped.Message);
        var skippedToProtocol = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.eventos_asistenciales SET estado_codigo = 'PROTOCOLO_URGENTE_MEDICO', revision = revision + 1, valoracion_medica_iniciada_por_cuenta_id = @AccountId, valoracion_medica_iniciada_en = SYSUTCDATETIME() WHERE id = @EventId",
            new { EventId = escalatedId, AccountId = medica.AccountId.Value }));
        Assert.Contains("CLINICAL_EVENT_TRANSITION_INVALID", skippedToProtocol.Message);
    }

    private static ActivateUrgentProtocolCommand Activate(SeededProfile seed, Guid eventId, int revision, string? note = null) =>
        new(seed.ProfileScopeId, seed.CenterId, eventId, revision, note);

    private static RecordUrgentProtocolEntryCommand ProtocolEntry(
        SeededProfile seed, Guid eventId, int revision, UrgentProtocolEntryType type, string? text = null, string? service = null,
        DateTimeOffset? contactedAt = null) =>
        new(seed.ProfileScopeId, seed.CenterId, eventId, revision, type, text, service, contactedAt);

    private static async Task<IReadOnlyList<UrgentProtocolSummary>> ProtocolsAsync(SeededProfile seed) =>
        (await BuildMedicina(seed.ExternalSubject).ListUrgentProtocolsAsync(new ListUrgentProtocolsCommand(seed.ProfileScopeId, seed.CenterId))).Value!;

    [Fact]
    public async Task ProtocoloUrgenteMedico_DesdeValoracion_RegistraYSeCierra_SinTocarLasBandejasDeEnfermeria()
    {
        var (enfermera, _, medica, eventId) = await SeedEscalatedAsync();
        var service = BuildMedicina(medica.ExternalSubject);
        var revision = (await FindAsync(medica, eventId))!.Revision;
        var started = (await service.StartMedicalAssessmentAsync(new StartMedicalAssessmentCommand(medica.ProfileScopeId, medica.CenterId, eventId, revision))).Value;

        var withoutAssessment = await service.ActivateUrgentProtocolAsync(Activate(medica, eventId, started));
        var saved = (await service.SaveMedicalAssessmentAsync(SaveMedical(medica, eventId, started))).Value;
        var activated = await service.ActivateUrgentProtocolAsync(Activate(medica, eventId, saved));

        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutAssessment.Error!.Code);
        Assert.True(activated.Ok);
        var detail = (await FindAsync(medica, eventId))!;
        Assert.Equal(ClinicalEventStatus.ProtocoloUrgenteMedico, detail.Status);
        Assert.Equal(SystemProfile.Medicina, detail.UrgentProtocol!.Profile);
        Assert.Null(detail.UrgentProtocol.ActivationNote);
        Assert.Empty((await service.ListEscalationsAsync(new ListEscalationsCommand(medica.ProfileScopeId, medica.CenterId))).Value!);
        Assert.Equal(eventId, Assert.Single(await ProtocolsAsync(medica)).EventId);

        var contact = await service.RecordUrgentProtocolEntryAsync(ProtocolEntry(medica, eventId, activated.Value, UrgentProtocolEntryType.Contacto,
            service: "Urgencias del hospital de referencia", contactedAt: DateTimeOffset.UtcNow));
        var evolution = await service.RecordUrgentProtocolEntryAsync(ProtocolEntry(medica, eventId, contact.Value, UrgentProtocolEntryType.Evolucion, "Mejora tras nebulización."));
        Assert.True(contact.Ok);
        Assert.True(evolution.Ok);
        Assert.Equal(1, await CountAuditAsync(eventId, "URGENT_PROTOCOL_CONTACT"));
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            Assert.Equal("MEDICINA", await connection.ExecuteScalarAsync<string>(
                "SELECT perfil_activo FROM dbo.eventos_auditoria WHERE recurso_id = @EventId AND accion_codigo = 'URGENT_PROTOCOL_ACTIVATE'", new { EventId = eventId }));
        }

        // Enfermería lo ve en su detalle en solo lectura, pero no registra ni lo lista como suyo.
        var nursing = BuildService(enfermera.ExternalSubject);
        var nurseDetail = (await nursing.FindPendingChangeDetailAsync(new FindPendingChangeDetailCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId))).Value!;
        Assert.Equal(2, nurseDetail.UrgentProtocol!.Entries.Count);
        var nurseEntry = await nursing.RecordUrgentProtocolEntryAsync(new RecordUrgentProtocolEntryCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, eventId, evolution.Value, UrgentProtocolEntryType.Actuacion, "x"));
        Assert.Equal(ApplicationFailureCode.Conflict, nurseEntry.Error!.Code);
        Assert.Empty((await nursing.ListUrgentProtocolsAsync(new ListUrgentProtocolsCommand(enfermera.ProfileScopeId, enfermera.CenterId))).Value!);
        var nurseClose = await nursing.CloseClinicalEventAsync(new CloseClinicalEventCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, eventId, evolution.Value, Guid.NewGuid(), FamilyCommunicationDecision.NoComunicar, null, null));
        Assert.Equal(ApplicationFailureCode.Conflict, nurseClose.Error!.Code);

        var closed = await service.CloseMedicalEventAsync(Close(medica, eventId, evolution.Value));
        Assert.True(closed.Ok);
        Assert.Equal(ClinicalEventStatus.Cerrado, (await FindAsync(medica, eventId))!.Status);
        Assert.Empty(await ProtocolsAsync(medica));
    }

    [Fact]
    public async Task ProtocoloUrgenteMedico_DesdeIndicacionPendiente_MantieneVisiblesLasIndicaciones_YSoloEsDeMedicina()
    {
        var (enfermera, _, medica, eventId) = await SeedEscalatedAsync();
        var service = BuildMedicina(medica.ExternalSubject);
        var indicated = (await service.RegisterMedicalIndicationAsync(Indication(medica, eventId, await StartAndSaveMedicalAsync(medica, eventId)))).Value;
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Medicina);

        var byOutsider = await BuildMedicina(outsider.ExternalSubject).ActivateUrgentProtocolAsync(Activate(outsider, eventId, indicated));
        var byNurse = await BuildMedicina(enfermera.ExternalSubject).ActivateUrgentProtocolAsync(Activate(enfermera, eventId, indicated));
        var nurseUseCase = await BuildService(enfermera.ExternalSubject).ActivateUrgentProtocolAsync(
            new ActivateUrgentProtocolCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId, indicated, null));
        var activated = await service.ActivateUrgentProtocolAsync(Activate(medica, eventId, indicated, "Broncoespasmo."));

        Assert.Equal(ApplicationFailureCode.AccessDenied, byOutsider.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byNurse.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, nurseUseCase.Error!.Code);
        Assert.True(activated.Ok);
        Assert.Equal(ClinicalEventStatus.ProtocoloUrgenteMedico, (await FindAsync(medica, eventId))!.Status);
        Assert.Single((await service.ListMedicalIndicationsAsync(new ListMedicalIndicationsCommand(medica.ProfileScopeId, medica.CenterId))).Value!);
        Assert.Empty(await ProtocolsAsync(outsider));
    }

    [Fact]
    public async Task DerivacionMedica_SoloLaFirmaMedicina_EnfermeriaLaVeYDescarga_YSeCierraConActualizacionRelevante()
    {
        var (enfermera, _, medica, eventId) = await SeedEscalatedAsync();
        var service = BuildMedicina(medica.ExternalSubject);
        var nursing = BuildService(enfermera.ExternalSubject);
        var revision = (await service.ActivateUrgentProtocolAsync(Activate(medica, eventId, await StartAndSaveMedicalAsync(medica, eventId)))).Value;

        var byNurse = await nursing.SignReferralReportAsync(SignCommand(enfermera, eventId, revision, Guid.NewGuid()));
        var operationId = Guid.NewGuid();
        var signed = await service.SignReferralReportAsync(SignCommand(medica, eventId, revision, operationId));
        var repeated = await service.SignReferralReportAsync(SignCommand(medica, eventId, revision, operationId));
        var callByNurse = await nursing.RecordFamilyCallAttemptAsync(CallCommand(enfermera, eventId, signed.Value));

        Assert.Equal(ApplicationFailureCode.Conflict, byNurse.Error!.Code);
        Assert.True(signed.Ok);
        Assert.Equal(signed.Value, repeated.Value);
        Assert.Equal(ApplicationFailureCode.Conflict, callByNurse.Error!.Code);

        var detail = (await FindAsync(medica, eventId))!;
        Assert.Equal(ClinicalEventStatus.ProtocoloUrgenteMedico, detail.Status);
        Assert.Equal(SystemProfile.Medicina, detail.Referral!.Profile);
        var nurseDetail = (await nursing.FindPendingChangeDetailAsync(new FindPendingChangeDetailCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId))).Value!;
        Assert.False(nurseDetail.Referral!.SignedByCurrentAccount);
        var (reportId, pdf, _) = await ReadReportAsync(eventId);
        var byNurseDownload = await nursing.DownloadReferralReportAsync(new DownloadReferralReportCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId));
        Assert.Equal(pdf, byNurseDownload.Value!.Content);
        Assert.Equal(1, await CountAuditAsync(reportId, "REFERRAL_REPORT_DOWNLOAD"));

        var closeWithoutCall = await service.CloseMedicalEventAsync(Close(medica, eventId, signed.Value, null,
            FamilyCommunicationDecision.Preparar, FamilyCommunicationType.Relevante, "Ha sido trasladado a Urgencias."));
        revision = (await service.RecordFamilyCallAttemptAsync(CallCommand(medica, eventId, signed.Value, result: FamilyCallResult.NumeroErroneo))).Value;
        var noComunicar = await service.CloseMedicalEventAsync(Close(medica, eventId, revision));
        var closed = await service.CloseMedicalEventAsync(Close(medica, eventId, revision, null,
            FamilyCommunicationDecision.Preparar, FamilyCommunicationType.Relevante, "Ha sido trasladado a Urgencias."));

        Assert.Equal(ApplicationFailureCode.InvalidInput, closeWithoutCall.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, noComunicar.Error!.Code);
        Assert.True(closed.Ok);
        Assert.Equal(ClinicalEventStatus.Cerrado, (await FindAsync(medica, eventId))!.Status);
    }
}

file sealed class FixedMedicinaSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
