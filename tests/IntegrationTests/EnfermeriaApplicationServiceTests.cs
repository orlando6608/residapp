using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Pdf;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Recorre la misma cadena que AuxiliarApplicationServiceTests, pero para el vertical Enfermería
/// (grupo E1): identidad de sesión -> IProfileScopeDirectoryProvider/IEnfermeriaResidentDirectory ->
/// EnfermeriaApplicationService.</summary>
public class EnfermeriaApplicationServiceTests
{
    internal static EnfermeriaApplicationService BuildService(string externalSubject, TimeSpan? correctionWindow = null)
    {
        var scopes = new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory);
        var directory = new SqlEnfermeriaResidentDirectory(TestDatabase.ConnectionFactory);
        var evidenceProvider = new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory);
        var baselines = new SqlBaselineRepository(TestDatabase.ConnectionFactory);
        var session = new FixedSessionIdentityProvider(externalSubject);

        var events = new SqlClinicalEventRepository(TestDatabase.ConnectionFactory);
        var changeInbox = new SqlChangeInboxDirectory(TestDatabase.ConnectionFactory);
        var assessments = new SqlNursingAssessmentRepository(TestDatabase.ConnectionFactory);
        var listScopeResidents = new ListScopeResidents(scopes, directory, session);
        var corrections = new SqlAssessmentCorrectionRepository(TestDatabase.ConnectionFactory);
        var settings = new AssessmentCorrectionSettings(correctionWindow ?? TimeSpan.FromHours(6));
        return new EnfermeriaApplicationService(
            listScopeResidents,
            new FindScopeResident(listScopeResidents),
            new ReadCurrentBaseline(evidenceProvider, session, baselines),
            new RegisterClinicalEvent(scopes, directory, session, events),
            new ListPendingChanges(scopes, changeInbox, session),
            new FindPendingChangeDetail(scopes, changeInbox, session),
            new StartNursingAssessment(scopes, changeInbox, session, assessments),
            new SaveNursingAssessment(scopes, changeInbox, session, assessments),
            new CloseClinicalEvent(scopes, changeInbox, session, assessments),
            new ListPendingFamilyCommunications(scopes, changeInbox, session),
            new StartFollowUp(scopes, changeInbox, session, assessments),
            new RecordFollowUpAction(scopes, changeInbox, session, assessments),
            new ListFollowUps(scopes, changeInbox, session),
            new EscalateClinicalEvent(scopes, changeInbox, session, assessments),
            new ListPendingIndications(scopes, changeInbox, session),
            new RecordIndicationProgress(scopes, changeInbox, session, new SqlMedicalIndicationRepository(TestDatabase.ConnectionFactory)),
            new ActivateUrgentProtocol(scopes, changeInbox, session, assessments),
            new RecordUrgentProtocolEntry(scopes, changeInbox, session, assessments),
            new ListUrgentProtocols(scopes, changeInbox, session),
            new SignReferralReport(scopes, changeInbox, session, assessments, new ReferralReportPdfRenderer()),
            new RecordFamilyCallAttempt(scopes, changeInbox, session, assessments),
            new FindResidentIdentification(scopes, changeInbox, session),
            new DownloadReferralReport(scopes, changeInbox, session, new SqlReferralReportRepository(TestDatabase.ConnectionFactory)),
            new ListClosedEvents(scopes, changeInbox, session),
            new ReadBaselineHistory(evidenceProvider, session, baselines),
            new ReadResidentTimeline(new FindScopeResident(listScopeResidents), changeInbox),
            new CorrectNursingAssessment(scopes, changeInbox, session, corrections, settings),
            new RectifyAssessment(scopes, changeInbox, session, corrections, settings),
            settings,
            new ListOpenEscalations(scopes, changeInbox, session),
            new ListOpenEvents(scopes, changeInbox, session),
            new ListTransferTeams(scopes, session, new SqlTransferTeamDirectory(TestDatabase.ConnectionFactory)));
    }

    /// <summary>Un residente en la unidad de dos profesionales de Enfermería y un evento propio de la
    /// primera, para recorrer la valoración y la concurrencia entre ambas.</summary>
    internal static async Task<(SeededProfile Enfermera, SeededProfile Companera, Guid EventId)> SeedOwnEventAsync(
        DailyChangeClassification classification = DailyChangeClassification.Ordinario)
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var enfermera = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, adminSeed.CenterId, adminSeed.UnitId);
        var companera = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, adminSeed.CenterId, adminSeed.UnitId);
        var resident = await new SqlResidentRepository(TestDatabase.ConnectionFactory).CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Valoración Enfermería", new DateOnly(1944, 4, 4), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));
        var registered = await BuildService(enfermera.ExternalSubject).RegisterClinicalEventAsync(new RegisterClinicalEventCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, resident.ResidentId,
            "Tos productiva desde la mañana.", classification, null, Guid.NewGuid()));
        return (enfermera, companera, registered.Value!.EventId);
    }

    private static SaveNursingAssessmentCommand SaveCommand(SeededProfile seed, Guid eventId, int revision, string? hallazgos = "Crepitantes en base derecha.") =>
        new(seed.ProfileScopeId, seed.CenterId, eventId, revision, hallazgos, null, "Se incorpora a 45º.", null, null,
            37.8m, 130, 80, 92, 22, 93, RespiratorySupportCode.AireAmbiente, null, null, null, null, null);

    [Fact]
    public async Task RegisterClinicalEvent_EntraEnLaBandejaComoPendiente_ConSuAutoriaReal()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync(DailyChangeClassification.Prioritario);
        var service = BuildService(enfermera.ExternalSubject);

        var result = await service.ListPendingChangesAsync(new ListPendingChangesCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, DailyChangeClassification.Prioritario));

        var item = Assert.Single(result.Value!);
        Assert.Equal(eventId, item.EventId);
        Assert.Equal(ClinicalEventOrigin.EventoEnfermeria, item.Origin);
        Assert.Equal(SystemProfile.Enfermeria, item.AuthorProfile);
        Assert.Equal("Tos productiva desde la mañana.", item.Observation);
        Assert.Equal(ClinicalEventStatus.Pendiente, item.Status);
    }

    [Fact]
    public async Task StartAndSaveAssessment_RegistraQuienEmpiezaYConservaElBorrador()
    {
        var (enfermera, companera, eventId) = await SeedOwnEventAsync();
        var service = BuildService(enfermera.ExternalSubject);
        var before = (await service.FindPendingChangeDetailAsync(new FindPendingChangeDetailCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId))).Value!;

        var started = await service.StartNursingAssessmentAsync(new StartNursingAssessmentCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, eventId, before.Revision));
        Assert.True(started.Ok);
        var saved = await service.SaveNursingAssessmentAsync(SaveCommand(enfermera, eventId, started.Value));
        Assert.True(saved.Ok);

        var detail = (await service.FindPendingChangeDetailAsync(new FindPendingChangeDetailCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId))).Value!;
        Assert.Equal(ClinicalEventStatus.EnValoracion, detail.Status);
        Assert.Equal(before.Revision + 2, detail.Revision);
        Assert.True(detail.AssessmentStartedByCurrentAccount);
        Assert.Equal("Tos productiva desde la mañana.", detail.Observation);
        Assert.Equal("Crepitantes en base derecha.", detail.Assessment!.Content.Findings);
        Assert.Equal(93, detail.Assessment.Content.Vitals.OxygenSaturationPct);
        Assert.Equal(RespiratorySupportCode.AireAmbiente, detail.Assessment.Content.Vitals.RespiratorySupport);

        // Sin propiedad permanente: otra enfermera de la unidad ve quién empezó y puede continuar.
        var otherView = (await BuildService(companera.ExternalSubject).FindPendingChangeDetailAsync(
            new FindPendingChangeDetailCommand(companera.ProfileScopeId, companera.CenterId, eventId))).Value!;
        Assert.False(otherView.AssessmentStartedByCurrentAccount);
        Assert.False(otherView.Assessment!.LastUpdatedByCurrentAccount);
        var continued = await BuildService(companera.ExternalSubject).SaveNursingAssessmentAsync(
            SaveCommand(companera, eventId, otherView.Revision, "Crepitantes bilaterales."));
        Assert.True(continued.Ok);
    }

    [Fact]
    public async Task StaleRevision_ExigeRecargar_YNoSobrescribeElTrabajoAjeno()
    {
        var (enfermera, companera, eventId) = await SeedOwnEventAsync();
        var mine = BuildService(enfermera.ExternalSubject);
        var theirs = BuildService(companera.ExternalSubject);
        var openedByBoth = (await mine.FindPendingChangeDetailAsync(new FindPendingChangeDetailCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId))).Value!.Revision;

        var theirStart = await theirs.StartNursingAssessmentAsync(new StartNursingAssessmentCommand(companera.ProfileScopeId, companera.CenterId, eventId, openedByBoth));
        Assert.True(theirStart.Ok);
        var myStart = await mine.StartNursingAssessmentAsync(new StartNursingAssessmentCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId, openedByBoth));
        Assert.Equal(ApplicationFailureCode.Conflict, myStart.Error!.Code);

        Assert.True((await theirs.SaveNursingAssessmentAsync(SaveCommand(companera, eventId, theirStart.Value, "Su valoración."))).Ok);
        var myStaleSave = await mine.SaveNursingAssessmentAsync(SaveCommand(enfermera, eventId, theirStart.Value, "Mi valoración."));
        Assert.Equal(ApplicationFailureCode.Conflict, myStaleSave.Error!.Code);

        var detail = (await mine.FindPendingChangeDetailAsync(new FindPendingChangeDetailCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId))).Value!;
        Assert.Equal("Su valoración.", detail.Assessment!.Content.Findings);
    }

    [Fact]
    public async Task SaveAssessment_SinEmpezar_EsConflicto_YVacia_EsInvalida()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var service = BuildService(enfermera.ExternalSubject);

        var notStarted = await service.SaveNursingAssessmentAsync(SaveCommand(enfermera, eventId, 1));
        Assert.Equal(ApplicationFailureCode.Conflict, notStarted.Error!.Code);

        var started = await service.StartNursingAssessmentAsync(new StartNursingAssessmentCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId, 1));
        var empty = await service.SaveNursingAssessmentAsync(new SaveNursingAssessmentCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, eventId, started.Value,
            "  ", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null));
        Assert.Equal(ApplicationFailureCode.InvalidInput, empty.Error!.Code);
    }

    [Fact]
    public async Task StartAssessment_ConAuxiliarOFueraDeAmbito_ReturnsAccessDenied()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var auxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, enfermera.CenterId, enfermera.UnitId);
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);

        var byAuxiliar = await BuildService(auxiliar.ExternalSubject).StartNursingAssessmentAsync(
            new StartNursingAssessmentCommand(auxiliar.ProfileScopeId, auxiliar.CenterId, eventId, 1));
        var byOutsider = await BuildService(outsider.ExternalSubject).StartNursingAssessmentAsync(
            new StartNursingAssessmentCommand(outsider.ProfileScopeId, outsider.CenterId, eventId, 1));

        Assert.Equal(ApplicationFailureCode.AccessDenied, byAuxiliar.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byOutsider.Error!.Code);
    }

    [Fact]
    public async Task FindPendingChangeDetail_DevuelveSoloLosRangosDeReferenciaDeSuCentro()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var otherCenter = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.rangos_referencia_constantes (centro_id, constante_codigo, minimo, maximo)
                VALUES (@CenterId, 'SATURACION_O2', 92, NULL), (@CenterId, 'TEMPERATURA', 35, 38), (@OtherCenterId, 'GLUCEMIA', 70, 180)
                """, new { CenterId = enfermera.CenterId.Value, OtherCenterId = otherCenter.CenterId.Value });
        }
        var service = BuildService(enfermera.ExternalSubject);

        var detail = (await service.FindPendingChangeDetailAsync(new FindPendingChangeDetailCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId))).Value!;

        Assert.Equal(2, detail.ReferenceRanges.Count);
        Assert.Contains(new VitalSignRange(VitalSignCode.SaturacionO2, 92m, null), detail.ReferenceRanges);
        Assert.Contains(new VitalSignRange(VitalSignCode.Temperatura, 35m, 38m), detail.ReferenceRanges);
    }

    [Fact]
    public async Task RangoDeReferencia_LaBaseDeDatosRechazaLimitesIncoherentes()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        foreach (var (min, max) in new (decimal?, decimal?)[] { (null, null), (38m, 35m) })
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
                "INSERT INTO dbo.rangos_referencia_constantes (centro_id, constante_codigo, minimo, maximo) VALUES (@CenterId, 'TEMPERATURA', @Min, @Max)",
                new { CenterId = seed.CenterId.Value, Min = min, Max = max }));
            Assert.Contains("CK_rrc_limites", ex.Message);
        }
    }

    [Fact]
    public async Task EventoAsistencial_LaBaseDeDatosRechazaSaltarRevisionYBorrar()
    {
        var (_, _, eventId) = await SeedOwnEventAsync();
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var skipped = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.eventos_asistenciales SET revision = revision + 2 WHERE id = @EventId", new { EventId = eventId }));
        Assert.Contains("CLINICAL_EVENT_TRANSITION_INVALID", skipped.Message);
        var deleted = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.eventos_asistenciales WHERE id = @EventId", new { EventId = eventId }));
        Assert.Contains("CLINICAL_EVENT_DELETE_FORBIDDEN", deleted.Message);
    }

    /// <summary>Empieza y guarda la valoración; devuelve la revisión con la que se puede cerrar.</summary>
    internal static async Task<int> StartAndSaveAsync(SeededProfile seed, Guid eventId)
    {
        var service = BuildService(seed.ExternalSubject);
        var started = await service.StartNursingAssessmentAsync(new StartNursingAssessmentCommand(seed.ProfileScopeId, seed.CenterId, eventId, 1));
        return (await service.SaveNursingAssessmentAsync(SaveCommand(seed, eventId, started.Value))).Value;
    }

    internal static CloseClinicalEventCommand CloseCommand(
        SeededProfile seed, Guid eventId, int revision, Guid operationId,
        FamilyCommunicationDecision? decision = FamilyCommunicationDecision.NoComunicar,
        FamilyCommunicationType? type = null, string? text = null) =>
        new(seed.ProfileScopeId, seed.CenterId, eventId, revision, operationId, decision, type, text);

    internal static async Task<int> CountAuditAsync(Guid resourceId, string actionCode)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE recurso_id = @ResourceId AND accion_codigo = @ActionCode",
            new { ResourceId = resourceId, ActionCode = actionCode });
    }

    [Fact]
    public async Task CloseEvent_NoComunicar_SaleDeLasBandejas_YLaValoracionYaNoSeEdita()
    {
        var (enfermera, companera, eventId) = await SeedOwnEventAsync();
        var revision = await StartAndSaveAsync(enfermera, eventId);
        var service = BuildService(enfermera.ExternalSubject);

        var closed = await service.CloseClinicalEventAsync(CloseCommand(enfermera, eventId, revision, Guid.NewGuid()));

        Assert.True(closed.Ok);
        Assert.Equal(revision + 1, closed.Value);
        var inbox = await service.ListPendingChangesAsync(new ListPendingChangesCommand(enfermera.ProfileScopeId, enfermera.CenterId, DailyChangeClassification.Ordinario));
        Assert.DoesNotContain(inbox.Value!, e => e.EventId == eventId);

        // El detalle de un evento cerrado sigue siendo legible, con su cierre y la valoración ya cerrada.
        var detail = (await BuildService(companera.ExternalSubject).FindPendingChangeDetailAsync(
            new FindPendingChangeDetailCommand(companera.ProfileScopeId, companera.CenterId, eventId))).Value!;
        Assert.Equal(ClinicalEventStatus.Cerrado, detail.Status);
        Assert.False(detail.Closure!.ClosedByCurrentAccount);
        Assert.Equal(FamilyCommunicationDecision.NoComunicar, detail.Closure.Decision);
        Assert.Null(detail.Closure.Communication);
        Assert.Equal("Crepitantes en base derecha.", detail.Assessment!.Content.Findings);
        Assert.Equal(1, await CountAuditAsync(eventId, "CLINICAL_EVENT_CLOSE"));

        var saveAfterClose = await service.SaveNursingAssessmentAsync(SaveCommand(enfermera, eventId, detail.Revision));
        Assert.Equal(ApplicationFailureCode.Conflict, saveAfterClose.Error!.Code);
    }

    [Fact]
    public async Task CloseEvent_MismoEnvioRepetido_NoCierraDosVeces_YOtroCierreEsConflicto()
    {
        var (enfermera, companera, eventId) = await SeedOwnEventAsync();
        var revision = await StartAndSaveAsync(enfermera, eventId);
        var service = BuildService(enfermera.ExternalSubject);
        var command = CloseCommand(enfermera, eventId, revision, Guid.NewGuid(),
            FamilyCommunicationDecision.Preparar, FamilyCommunicationType.Ordinaria, "Hoy ha tenido tos; la estamos vigilando.");

        var first = await service.CloseClinicalEventAsync(command);
        var repeated = await service.CloseClinicalEventAsync(command);
        var byOther = await BuildService(companera.ExternalSubject).CloseClinicalEventAsync(
            CloseCommand(companera, eventId, revision, Guid.NewGuid()));

        Assert.True(first.Ok);
        Assert.True(repeated.Ok);
        Assert.Equal(first.Value, repeated.Value);
        Assert.Equal(ApplicationFailureCode.Conflict, byOther.Error!.Code);
        Assert.Equal(1, await CountAuditAsync(eventId, "CLINICAL_EVENT_CLOSE"));
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.comunicaciones_familiares WHERE evento_id = @EventId", new { EventId = eventId }));
    }

    [Fact]
    public async Task CloseEvent_PrepararComunicacion_QuedaPendienteDeAprobacion_SoloEnSuAmbito()
    {
        var (enfermera, companera, eventId) = await SeedOwnEventAsync();
        var revision = await StartAndSaveAsync(enfermera, eventId);
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);

        var closed = await BuildService(enfermera.ExternalSubject).CloseClinicalEventAsync(CloseCommand(
            enfermera, eventId, revision, Guid.NewGuid(),
            FamilyCommunicationDecision.Preparar, FamilyCommunicationType.Relevante, "  Hoy ha tenido fiebre y la hemos atendido.  "));
        Assert.True(closed.Ok);

        var pending = await BuildService(companera.ExternalSubject).ListPendingFamilyCommunicationsAsync(
            new ListPendingFamilyCommunicationsCommand(companera.ProfileScopeId, companera.CenterId));
        var item = Assert.Single(pending.Value!);
        Assert.Equal(eventId, item.EventId);
        Assert.Equal(FamilyCommunicationType.Relevante, item.Communication.Type);
        Assert.Equal("Hoy ha tenido fiebre y la hemos atendido.", item.Communication.Text);

        var detail = (await BuildService(companera.ExternalSubject).FindPendingChangeDetailAsync(
            new FindPendingChangeDetailCommand(companera.ProfileScopeId, companera.CenterId, eventId))).Value!;
        Assert.Equal(FamilyCommunicationDecision.Preparar, detail.Closure!.Decision);
        Assert.Equal(item.Communication, detail.Closure.Communication);
        Assert.Equal(1, await CountAuditAsync(eventId, "CLINICAL_EVENT_CLOSE"));

        var outside = await BuildService(outsider.ExternalSubject).ListPendingFamilyCommunicationsAsync(
            new ListPendingFamilyCommunicationsCommand(outsider.ProfileScopeId, outsider.CenterId));
        Assert.Empty(outside.Value!);
    }

    [Fact]
    public async Task CloseEvent_SinValoracionOSinDecision_EsInvalido_YConRevisionAntigua_EsConflicto()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var service = BuildService(enfermera.ExternalSubject);
        var started = await service.StartNursingAssessmentAsync(new StartNursingAssessmentCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId, 1));

        var withoutAssessment = await service.CloseClinicalEventAsync(CloseCommand(enfermera, eventId, started.Value, Guid.NewGuid()));
        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutAssessment.Error!.Code);

        var saved = (await service.SaveNursingAssessmentAsync(SaveCommand(enfermera, eventId, started.Value))).Value;
        var withoutDecision = await service.CloseClinicalEventAsync(CloseCommand(enfermera, eventId, saved, Guid.NewGuid(), decision: null));
        var prepareWithoutText = await service.CloseClinicalEventAsync(CloseCommand(
            enfermera, eventId, saved, Guid.NewGuid(), FamilyCommunicationDecision.Preparar, FamilyCommunicationType.Ordinaria, " "));
        var stale = await service.CloseClinicalEventAsync(CloseCommand(enfermera, eventId, started.Value, Guid.NewGuid()));

        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutDecision.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, prepareWithoutText.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        var detail = (await service.FindPendingChangeDetailAsync(new FindPendingChangeDetailCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId))).Value!;
        Assert.Equal(ClinicalEventStatus.EnValoracion, detail.Status);
        Assert.Equal(saved, detail.Revision);
    }

    [Fact]
    public async Task CloseEvent_ConAuxiliarOFueraDeAmbito_ReturnsAccessDenied()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var revision = await StartAndSaveAsync(enfermera, eventId);
        var auxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, enfermera.CenterId, enfermera.UnitId);
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);

        var byAuxiliar = await BuildService(auxiliar.ExternalSubject).CloseClinicalEventAsync(CloseCommand(auxiliar, eventId, revision, Guid.NewGuid()));
        var byOutsider = await BuildService(outsider.ExternalSubject).CloseClinicalEventAsync(CloseCommand(outsider, eventId, revision, Guid.NewGuid()));
        var listByAuxiliar = await BuildService(auxiliar.ExternalSubject).ListPendingFamilyCommunicationsAsync(
            new ListPendingFamilyCommunicationsCommand(auxiliar.ProfileScopeId, auxiliar.CenterId));

        Assert.Equal(ApplicationFailureCode.AccessDenied, byAuxiliar.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byOutsider.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, listByAuxiliar.Error!.Code);
    }

    [Fact]
    public async Task SaveAssessment_CadaGuardadoDejaUnaVersionInmutable()
    {
        var (enfermera, companera, eventId) = await SeedOwnEventAsync();
        var first = await StartAndSaveAsync(enfermera, eventId);
        var second = (await BuildService(companera.ExternalSubject).SaveNursingAssessmentAsync(
            SaveCommand(companera, eventId, first, "Crepitantes bilaterales."))).Value;

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var versions = (await connection.QueryAsync<(int Revision, string Findings, Guid SavedBy)>("""
            SELECT revision_evento, hallazgos, guardado_por_cuenta_id FROM dbo.valoraciones_enfermeria_versiones
             WHERE evento_id = @EventId ORDER BY revision_evento
            """, new { EventId = eventId })).ToList();
        Assert.Equal([(first, "Crepitantes en base derecha.", enfermera.AccountId.Value), (second, "Crepitantes bilaterales.", companera.AccountId.Value)], versions);

        var updated = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.valoraciones_enfermeria_versiones SET hallazgos = 'x' WHERE evento_id = @EventId", new { EventId = eventId }));
        Assert.Contains("NURSING_ASSESSMENT_VERSION_IMMUTABLE", updated.Message);
        var deleted = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.valoraciones_enfermeria_versiones WHERE evento_id = @EventId", new { EventId = eventId }));
        Assert.Contains("NURSING_ASSESSMENT_VERSION_IMMUTABLE", deleted.Message);
    }

    [Fact]
    public async Task EventoCerrado_LaBaseDeDatosRechazaReabrirloYModificarSuValoracionYComunicacion()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var revision = await StartAndSaveAsync(enfermera, eventId);
        Assert.True((await BuildService(enfermera.ExternalSubject).CloseClinicalEventAsync(CloseCommand(
            enfermera, eventId, revision, Guid.NewGuid(),
            FamilyCommunicationDecision.Preparar, FamilyCommunicationType.Ordinaria, "Texto para la familia."))).Ok);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        foreach (var (sql, expected) in new[]
        {
            ("UPDATE dbo.eventos_asistenciales SET estado_codigo = 'EN_VALORACION', revision = revision + 1, cerrado_por_cuenta_id = NULL, cerrado_en = NULL, comunicacion_familiar_codigo = NULL WHERE id = @EventId", "CLINICAL_EVENT_TRANSITION_INVALID"),
            ("UPDATE dbo.eventos_asistenciales SET revision = revision + 1 WHERE id = @EventId", "CLINICAL_EVENT_TRANSITION_INVALID"),
            ("UPDATE dbo.valoraciones_enfermeria SET hallazgos = 'x' WHERE evento_id = @EventId", "NURSING_ASSESSMENT_IMMUTABLE"),
            ("UPDATE dbo.comunicaciones_familiares SET texto = 'x' WHERE evento_id = @EventId", "FAMILY_COMMUNICATION_IMMUTABLE"),
            ("DELETE FROM dbo.comunicaciones_familiares WHERE evento_id = @EventId", "FAMILY_COMMUNICATION_IMMUTABLE"),
        })
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, new { EventId = eventId }));
            Assert.Contains(expected, ex.Message);
        }
    }

    internal static StartFollowUpCommand StartFollowUpCommand(
        SeededProfile seed, Guid eventId, int revision, DateOnly? dueDate = null, string? criterion = "Si reaparece la tos.") =>
        new(seed.ProfileScopeId, seed.CenterId, eventId, revision, dueDate, criterion, "Vigilar tolerancia.");

    internal static async Task<PendingChangeDetail> DetailAsync(SeededProfile seed, Guid eventId) =>
        (await BuildService(seed.ExternalSubject).FindPendingChangeDetailAsync(
            new FindPendingChangeDetailCommand(seed.ProfileScopeId, seed.CenterId, eventId))).Value!;

    private static async Task<IReadOnlyList<FollowUpSummary>> FollowUpsAsync(SeededProfile seed) =>
        (await BuildService(seed.ExternalSubject).ListFollowUpsAsync(new ListFollowUpsCommand(seed.ProfileScopeId, seed.CenterId))).Value!;

    [Fact]
    public async Task StartFollowUp_SacaElEventoDeLasBandejas_YLoPoneEnSeguimientos()
    {
        var (enfermera, companera, eventId) = await SeedOwnEventAsync();
        var revision = await StartAndSaveAsync(enfermera, eventId);
        var service = BuildService(enfermera.ExternalSubject);

        var started = await service.StartFollowUpAsync(StartFollowUpCommand(enfermera, eventId, revision, new DateOnly(2030, 1, 15)));

        Assert.True(started.Ok);
        var inbox = await service.ListPendingChangesAsync(new ListPendingChangesCommand(enfermera.ProfileScopeId, enfermera.CenterId, DailyChangeClassification.Ordinario));
        Assert.DoesNotContain(inbox.Value!, e => e.EventId == eventId);
        var item = Assert.Single(await FollowUpsAsync(companera));
        Assert.Equal(eventId, item.EventId);
        Assert.Equal(new DateOnly(2030, 1, 15), item.DueDate);
        Assert.Equal("Si reaparece la tos.", item.Criterion);
        Assert.False(item.TransferPending);

        var detail = await DetailAsync(companera, eventId);
        Assert.Equal(ClinicalEventStatus.EnSeguimiento, detail.Status);
        Assert.False(detail.FollowUp!.StartedByCurrentAccount);
        Assert.Equal("Vigilar tolerancia.", detail.FollowUp.ContinuityNotes);
        Assert.Equal(1, await CountAuditAsync(eventId, "FOLLOW_UP_START"));

        // La valoración no se edita durante el seguimiento: lo nuevo son actuaciones del seguimiento.
        var save = await service.SaveNursingAssessmentAsync(SaveCommand(enfermera, eventId, detail.Revision));
        Assert.Equal(ApplicationFailureCode.Conflict, save.Error!.Code);
    }

    [Fact]
    public async Task StartFollowUp_SinPlanOSinValoracion_EsInvalido()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var service = BuildService(enfermera.ExternalSubject);
        var started = await service.StartNursingAssessmentAsync(new StartNursingAssessmentCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId, 1));

        var withoutAssessment = await service.StartFollowUpAsync(StartFollowUpCommand(enfermera, eventId, started.Value));
        var saved = (await service.SaveNursingAssessmentAsync(SaveCommand(enfermera, eventId, started.Value))).Value;
        var withoutPlan = await service.StartFollowUpAsync(StartFollowUpCommand(enfermera, eventId, saved, null, " "));

        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutAssessment.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutPlan.Error!.Code);
        Assert.Equal(ClinicalEventStatus.EnValoracion, (await DetailAsync(enfermera, eventId)).Status);
    }

    [Fact]
    public async Task FollowUpActions_ConservanAutoria_ReprogramanYTransfierenConRecepcion()
    {
        var (enfermera, companera, eventId) = await SeedOwnEventAsync();
        var revision = await StartAndSaveAsync(enfermera, eventId);
        var mine = BuildService(enfermera.ExternalSubject);
        var theirs = BuildService(companera.ExternalSubject);
        revision = (await mine.StartFollowUpAsync(StartFollowUpCommand(enfermera, eventId, revision, new DateOnly(2030, 1, 15)))).Value;

        revision = (await mine.RecordFollowUpActionAsync(new RecordFollowUpActionCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, eventId, revision, FollowUpActionType.Actuacion, "Tolera la dieta."))).Value;
        var stale = await theirs.RecordFollowUpActionAsync(new RecordFollowUpActionCommand(
            companera.ProfileScopeId, companera.CenterId, eventId, revision - 1, FollowUpActionType.Actuacion, "Otra."));
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        var unjustified = await theirs.RecordFollowUpActionAsync(new RecordFollowUpActionCommand(
            companera.ProfileScopeId, companera.CenterId, eventId, revision, FollowUpActionType.Reprogramacion, null, new DateOnly(2030, 2, 1)));
        Assert.Equal(ApplicationFailureCode.InvalidInput, unjustified.Error!.Code);
        revision = (await theirs.RecordFollowUpActionAsync(new RecordFollowUpActionCommand(
            companera.ProfileScopeId, companera.CenterId, eventId, revision, FollowUpActionType.Reprogramacion,
            "Persiste la tos.", new DateOnly(2030, 2, 1)))).Value;
        revision = (await mine.RecordFollowUpActionAsync(new RecordFollowUpActionCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, eventId, revision, FollowUpActionType.Transferencia,
            "Revisar a las 8.", EquipoEntranteId: await TransferTeamData.CreateAsync(enfermera, "Turno de noche")))).Value;

        var pending = (await DetailAsync(companera, eventId)).FollowUp!;
        Assert.Equal(new DateOnly(2030, 2, 1), pending.DueDate);
        Assert.Null(pending.Criterion);
        Assert.Equal(new DateOnly(2030, 1, 15), pending.InitialDueDate);
        Assert.Equal("Turno de noche", pending.PendingTransfer!.IncomingTeam);
        Assert.Equal([false, true, false], pending.Actions.Select(a => a.ByCurrentAccount).ToArray());
        Assert.True(Assert.Single(await FollowUpsAsync(companera)).TransferPending);

        revision = (await theirs.RecordFollowUpActionAsync(new RecordFollowUpActionCommand(
            companera.ProfileScopeId, companera.CenterId, eventId, revision, FollowUpActionType.Recepcion,
            TransferenciaId: pending.PendingTransfer.Id))).Value;
        var again = await theirs.RecordFollowUpActionAsync(new RecordFollowUpActionCommand(
            companera.ProfileScopeId, companera.CenterId, eventId, revision, FollowUpActionType.Recepcion,
            TransferenciaId: pending.PendingTransfer.Id));

        Assert.Equal(ApplicationFailureCode.Conflict, again.Error!.Code);
        Assert.Null((await DetailAsync(companera, eventId)).FollowUp!.PendingTransfer);
        Assert.False(Assert.Single(await FollowUpsAsync(companera)).TransferPending);
        Assert.Equal(1, await CountAuditAsync(eventId, "FOLLOW_UP_RECEIVE"));
    }

    [Fact]
    public async Task FollowUp_Vencido_SigueAbiertoYVisible_YAlResolverloSeCierra()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var revision = await StartAndSaveAsync(enfermera, eventId);
        var service = BuildService(enfermera.ExternalSubject);
        var yesterday = DateOnly.FromDateTime(DateTime.Today).AddDays(-1);
        revision = (await service.StartFollowUpAsync(StartFollowUpCommand(enfermera, eventId, revision, yesterday, null))).Value;

        var overdue = Assert.Single(await FollowUpsAsync(enfermera));
        Assert.True(new FollowUpPlan(overdue.DueDate, overdue.Criterion).IsOverdue(DateOnly.FromDateTime(DateTime.Today)));
        Assert.Equal(ClinicalEventStatus.EnSeguimiento, (await DetailAsync(enfermera, eventId)).Status);

        var closed = await service.CloseClinicalEventAsync(CloseCommand(enfermera, eventId, revision, Guid.NewGuid()));

        Assert.True(closed.Ok);
        Assert.Empty(await FollowUpsAsync(enfermera));
        var detail = await DetailAsync(enfermera, eventId);
        Assert.Equal(ClinicalEventStatus.Cerrado, detail.Status);
        Assert.NotNull(detail.FollowUp);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal("CERRADA", await connection.ExecuteScalarAsync<string>(
            "SELECT estado_codigo FROM dbo.valoraciones_enfermeria WHERE evento_id = @EventId", new { EventId = eventId }));
    }

    [Fact]
    public async Task FollowUp_ConAuxiliarOFueraDeAmbito_ReturnsAccessDenied()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var revision = await StartAndSaveAsync(enfermera, eventId);
        var auxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, enfermera.CenterId, enfermera.UnitId);
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);

        var byAuxiliar = await BuildService(auxiliar.ExternalSubject).StartFollowUpAsync(StartFollowUpCommand(auxiliar, eventId, revision));
        var byOutsider = await BuildService(outsider.ExternalSubject).StartFollowUpAsync(StartFollowUpCommand(outsider, eventId, revision));
        var actionByOutsider = await BuildService(outsider.ExternalSubject).RecordFollowUpActionAsync(new RecordFollowUpActionCommand(
            outsider.ProfileScopeId, outsider.CenterId, eventId, revision, FollowUpActionType.Actuacion, "x"));
        var listByAuxiliar = await BuildService(auxiliar.ExternalSubject).ListFollowUpsAsync(new ListFollowUpsCommand(auxiliar.ProfileScopeId, auxiliar.CenterId));

        Assert.Equal(ApplicationFailureCode.AccessDenied, byAuxiliar.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byOutsider.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, actionByOutsider.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, listByAuxiliar.Error!.Code);
    }

    [Fact]
    public async Task Transferencia_ConEquipoDeLaUnidad_GuardaElVinculoYElNombreCopiado()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var revision = await StartAndSaveAsync(enfermera, eventId);
        var service = BuildService(enfermera.ExternalSubject);
        revision = (await service.StartFollowUpAsync(StartFollowUpCommand(enfermera, eventId, revision))).Value;
        var teamId = await TransferTeamData.CreateAsync(enfermera, "Equipo de noche");
        await TransferTeamData.CreateAsync(enfermera, "Equipo inactivo", active: false);

        var listed = (await service.ListTransferTeamsAsync(new ListTransferTeamsQuery(enfermera.ProfileScopeId, enfermera.CenterId, eventId))).Value!;
        var transfer = await service.RecordFollowUpActionAsync(new RecordFollowUpActionCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, eventId, revision, FollowUpActionType.Transferencia, "Revisar a las 8.",
            EquipoEntranteId: teamId));
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("UPDATE dbo.equipos SET nombre_visible = 'Renombrado despues' WHERE id = @teamId", new { teamId });

        Assert.Equal(new TransferTeam(teamId, "Equipo de noche"), Assert.Single(listed));
        Assert.True(transfer.Ok, transfer.Error?.Message);
        Assert.Equal(teamId, await connection.ExecuteScalarAsync<Guid>(
            "SELECT a.equipo_entrante_id FROM dbo.seguimiento_acciones a JOIN dbo.seguimientos s ON s.id = a.seguimiento_id WHERE s.evento_id = @eventId AND a.tipo_codigo = 'TRANSFERENCIA'",
            new { eventId }));
        Assert.Equal("Equipo de noche", (await DetailAsync(enfermera, eventId)).FollowUp!.PendingTransfer!.IncomingTeam);
    }

    [Fact]
    public async Task Transferencia_ConEquipoInactivoAjenoInexistenteOSinEquipo_SeRechazaSinAvanzarLaRevision()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var revision = await StartAndSaveAsync(enfermera, eventId);
        var service = BuildService(enfermera.ExternalSubject);
        revision = (await service.StartFollowUpAsync(StartFollowUpCommand(enfermera, eventId, revision))).Value;
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        Guid?[] invalid =
        [
            await TransferTeamData.CreateAsync(enfermera, "Inactivo", active: false),
            await TransferTeamData.CreateAsync(outsider, "De otro centro"),
            Guid.NewGuid(),
            Guid.Empty,
            null,
        ];

        var results = new List<ApplicationResult<int>>();
        foreach (var team in invalid)
        {
            results.Add(await service.RecordFollowUpActionAsync(new RecordFollowUpActionCommand(
                enfermera.ProfileScopeId, enfermera.CenterId, eventId, revision, FollowUpActionType.Transferencia, "Nota.",
                EquipoEntranteId: team)));
        }

        Assert.All(results, r => Assert.Equal(ApplicationFailureCode.InvalidInput, r.Error!.Code));
        // La revisión no avanzó: la misma sigue valiendo.
        Assert.True((await service.RecordFollowUpActionAsync(new RecordFollowUpActionCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, eventId, revision, FollowUpActionType.Actuacion, "Sigue igual."))).Ok);
    }

    [Fact]
    public async Task ListTransferTeams_ConAuxiliarOFueraDeAmbito_NoEntregaEquipos()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        await TransferTeamData.CreateAsync(enfermera, "Equipo de noche");
        var auxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, enfermera.CenterId, enfermera.UnitId);
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);

        var byAuxiliar = await BuildService(auxiliar.ExternalSubject).ListTransferTeamsAsync(
            new ListTransferTeamsQuery(auxiliar.ProfileScopeId, auxiliar.CenterId, eventId));
        var byOutsider = await BuildService(outsider.ExternalSubject).ListTransferTeamsAsync(
            new ListTransferTeamsQuery(outsider.ProfileScopeId, outsider.CenterId, eventId));
        var foreignScope = await BuildService(outsider.ExternalSubject).ListTransferTeamsAsync(
            new ListTransferTeamsQuery(enfermera.ProfileScopeId, enfermera.CenterId, eventId));

        Assert.Equal(ApplicationFailureCode.AccessDenied, byAuxiliar.Error!.Code);
        Assert.Empty(byOutsider.Value!);
        Assert.Equal(ApplicationFailureCode.AccessDenied, foreignScope.Error!.Code);
    }

    [Fact]
    public async Task Seguimiento_LaBaseDeDatosRechazaModificarloYSaltosDeEstado()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var skipped = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.eventos_asistenciales SET estado_codigo = 'EN_SEGUIMIENTO', revision = revision + 1, valoracion_iniciada_por_cuenta_id = @AccountId, valoracion_iniciada_en = SYSUTCDATETIME() WHERE id = @EventId",
            new { EventId = eventId, AccountId = enfermera.AccountId.Value }));
        Assert.Contains("CLINICAL_EVENT_TRANSITION_INVALID", skipped.Message);

        var revision = await StartAndSaveAsync(enfermera, eventId);
        var service = BuildService(enfermera.ExternalSubject);
        revision = (await service.StartFollowUpAsync(StartFollowUpCommand(enfermera, eventId, revision))).Value;
        await service.RecordFollowUpActionAsync(new RecordFollowUpActionCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, eventId, revision, FollowUpActionType.Transferencia, EquipoEntranteId: await TransferTeamData.CreateAsync(enfermera, "Tarde")));

        foreach (var (sql, expected) in new[]
        {
            ("UPDATE dbo.eventos_asistenciales SET estado_codigo = 'EN_VALORACION', revision = revision + 1 WHERE id = @EventId", "CLINICAL_EVENT_TRANSITION_INVALID"),
            ("UPDATE dbo.seguimientos SET criterio = 'x' WHERE evento_id = @EventId", "FOLLOW_UP_IMMUTABLE"),
            ("DELETE FROM dbo.seguimientos WHERE evento_id = @EventId", "FOLLOW_UP_IMMUTABLE"),
            ("UPDATE a SET texto = 'x' FROM dbo.seguimiento_acciones a JOIN dbo.seguimientos s ON s.id = a.seguimiento_id WHERE s.evento_id = @EventId", "FOLLOW_UP_ACTION_IMMUTABLE"),
            ("""
             INSERT INTO dbo.seguimiento_acciones (id, seguimiento_id, tipo_codigo, transferencia_id, registrado_por_cuenta_id, registrado_en)
             SELECT NEWID(), a.seguimiento_id, 'RECEPCION', a.id, a.registrado_por_cuenta_id, SYSUTCDATETIME()
               FROM dbo.seguimiento_acciones a JOIN dbo.seguimientos s ON s.id = a.seguimiento_id
              CROSS JOIN (VALUES (1), (2)) twice(n)
              WHERE s.evento_id = @EventId AND a.tipo_codigo = 'TRANSFERENCIA'
             """, "UX_sa_recepcion"),
        })
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, new { EventId = eventId }));
            Assert.Contains(expected, ex.Message);
        }
    }

    internal static EscalateClinicalEventCommand EscalateCommand(SeededProfile seed, Guid eventId, int revision, string? reason = "Disnea progresiva pese a oxigenoterapia.") =>
        new(seed.ProfileScopeId, seed.CenterId, eventId, revision, reason);

    [Fact]
    public async Task Escalate_DesdeValoracion_SaleDeLasBandejas_YCierraLaValoracion()
    {
        var (enfermera, companera, eventId) = await SeedOwnEventAsync();
        var revision = await StartAndSaveAsync(enfermera, eventId);
        var service = BuildService(enfermera.ExternalSubject);

        var escalated = await service.EscalateClinicalEventAsync(EscalateCommand(enfermera, eventId, revision, "  Disnea progresiva.  "));

        Assert.True(escalated.Ok);
        var inbox = await service.ListPendingChangesAsync(new ListPendingChangesCommand(enfermera.ProfileScopeId, enfermera.CenterId, DailyChangeClassification.Ordinario));
        Assert.DoesNotContain(inbox.Value!, e => e.EventId == eventId);
        var detail = await DetailAsync(companera, eventId);
        Assert.Equal(ClinicalEventStatus.EscaladoMedicina, detail.Status);
        Assert.Equal("Disnea progresiva.", detail.Escalation!.Reason);
        Assert.False(detail.Escalation.EscalatedByCurrentAccount);
        Assert.Equal(1, await CountAuditAsync(eventId, "CLINICAL_EVENT_ESCALATE"));
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal("CERRADA", await connection.ExecuteScalarAsync<string>(
            "SELECT estado_codigo FROM dbo.valoraciones_enfermeria WHERE evento_id = @EventId", new { EventId = eventId }));

        var saveAfter = await service.SaveNursingAssessmentAsync(SaveCommand(enfermera, eventId, detail.Revision));
        var closeAfter = await service.CloseClinicalEventAsync(CloseCommand(enfermera, eventId, detail.Revision, Guid.NewGuid()));
        Assert.Equal(ApplicationFailureCode.Conflict, saveAfter.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, closeAfter.Error!.Code);
    }

    [Fact]
    public async Task Escalate_DesdeSeguimiento_SaleDeLaBandejaDeSeguimientos()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var revision = await StartAndSaveAsync(enfermera, eventId);
        var service = BuildService(enfermera.ExternalSubject);
        revision = (await service.StartFollowUpAsync(StartFollowUpCommand(enfermera, eventId, revision))).Value;

        var escalated = await service.EscalateClinicalEventAsync(EscalateCommand(enfermera, eventId, revision));

        Assert.True(escalated.Ok);
        Assert.Empty(await FollowUpsAsync(enfermera));
        Assert.Equal(ClinicalEventStatus.EscaladoMedicina, (await DetailAsync(enfermera, eventId)).Status);
    }

    [Fact]
    public async Task Escalate_SinMotivoOSinValoracion_EsInvalido_YRevisionAntigua_EsConflicto_YFueraDeAmbito_Denegado()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var service = BuildService(enfermera.ExternalSubject);
        var started = await service.StartNursingAssessmentAsync(new StartNursingAssessmentCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId, 1));
        var withoutAssessment = await service.EscalateClinicalEventAsync(EscalateCommand(enfermera, eventId, started.Value));
        var saved = (await service.SaveNursingAssessmentAsync(SaveCommand(enfermera, eventId, started.Value))).Value;
        var withoutReason = await service.EscalateClinicalEventAsync(EscalateCommand(enfermera, eventId, saved, " "));
        var stale = await service.EscalateClinicalEventAsync(EscalateCommand(enfermera, eventId, started.Value));
        var auxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, enfermera.CenterId, enfermera.UnitId);
        var byAuxiliar = await BuildService(auxiliar.ExternalSubject).EscalateClinicalEventAsync(EscalateCommand(auxiliar, eventId, saved));

        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutAssessment.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutReason.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byAuxiliar.Error!.Code);
        Assert.Equal(ClinicalEventStatus.EnValoracion, (await DetailAsync(enfermera, eventId)).Status);
    }

    [Fact]
    public async Task Escalado_LaBaseDeDatosRechazaModificarloYSalirDeEscalado()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var revision = await StartAndSaveAsync(enfermera, eventId);
        Assert.True((await BuildService(enfermera.ExternalSubject).EscalateClinicalEventAsync(EscalateCommand(enfermera, eventId, revision))).Ok);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        foreach (var (sql, expected) in new[]
        {
            ("UPDATE dbo.eventos_asistenciales SET estado_codigo = 'EN_VALORACION', revision = revision + 1 WHERE id = @EventId", "CLINICAL_EVENT_TRANSITION_INVALID"),
            ("UPDATE dbo.escalados_medicina SET motivo = 'x' WHERE evento_id = @EventId", "CLINICAL_EVENT_ESCALATION_IMMUTABLE"),
            ("DELETE FROM dbo.escalados_medicina WHERE evento_id = @EventId", "CLINICAL_EVENT_ESCALATION_IMMUTABLE"),
        })
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, new { EventId = eventId }));
            Assert.Contains(expected, ex.Message);
        }
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

    internal static ActivateUrgentProtocolCommand ActivateCommand(SeededProfile seed, Guid eventId, int revision, string? note = null) =>
        new(seed.ProfileScopeId, seed.CenterId, eventId, revision, note);

    private static RecordUrgentProtocolEntryCommand EntryCommand(
        SeededProfile seed, Guid eventId, int revision, UrgentProtocolEntryType type, string? text = null, string? service = null,
        DateTimeOffset? contactedAt = null) =>
        new(seed.ProfileScopeId, seed.CenterId, eventId, revision, type, text, service, contactedAt);

    private static async Task<IReadOnlyList<UrgentProtocolSummary>> ProtocolsAsync(SeededProfile seed) =>
        (await BuildService(seed.ExternalSubject).ListUrgentProtocolsAsync(new ListUrgentProtocolsCommand(seed.ProfileScopeId, seed.CenterId))).Value!;

    [Fact]
    public async Task ProtocoloUrgente_ExigeValoracion_SaleDeLasBandejas_YEntraEnProtocolos()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var service = BuildService(enfermera.ExternalSubject);
        var started = (await service.StartNursingAssessmentAsync(new StartNursingAssessmentCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId, 1))).Value;

        var withoutAssessment = await service.ActivateUrgentProtocolAsync(ActivateCommand(enfermera, eventId, started));
        var saved = (await service.SaveNursingAssessmentAsync(SaveCommand(enfermera, eventId, started))).Value;
        var tooLong = await service.ActivateUrgentProtocolAsync(ActivateCommand(enfermera, eventId, saved, new string('a', UrgentProtocolActivation.MaxNoteLength + 1)));
        var stale = await service.ActivateUrgentProtocolAsync(ActivateCommand(enfermera, eventId, saved - 1));
        var activated = await service.ActivateUrgentProtocolAsync(ActivateCommand(enfermera, eventId, saved, "  Desaturación brusca.  "));
        var again = await service.ActivateUrgentProtocolAsync(ActivateCommand(enfermera, eventId, activated.Value));

        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutAssessment.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, tooLong.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        Assert.True(activated.Ok);
        Assert.Equal(ApplicationFailureCode.Conflict, again.Error!.Code);

        var detail = await DetailAsync(enfermera, eventId);
        Assert.Equal(ClinicalEventStatus.ProtocoloUrgente, detail.Status);
        Assert.Equal(SystemProfile.Enfermeria, detail.UrgentProtocol!.Profile);
        Assert.Equal("Desaturación brusca.", detail.UrgentProtocol.ActivationNote);
        Assert.True(detail.UrgentProtocol.ActivatedByCurrentAccount);
        Assert.Empty(detail.UrgentProtocol.Entries);
        Assert.Empty((await service.ListPendingChangesAsync(new ListPendingChangesCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, DailyChangeClassification.Ordinario))).Value!);
        Assert.Equal(eventId, Assert.Single(await ProtocolsAsync(enfermera)).EventId);
        Assert.Equal(1, await CountAuditAsync(eventId, "URGENT_PROTOCOL_ACTIVATE"));

        // La valoración sigue en borrador, pero ya no se edita.
        var saveDuring = await service.SaveNursingAssessmentAsync(SaveCommand(enfermera, eventId, detail.Revision));
        Assert.Equal(ApplicationFailureCode.Conflict, saveDuring.Error!.Code);
    }

    [Fact]
    public async Task ProtocoloUrgente_DesdeSeguimiento_RegistraActuacionEvolucionYContacto_ConAutoria()
    {
        var (enfermera, companera, eventId) = await SeedOwnEventAsync();
        var service = BuildService(enfermera.ExternalSubject);
        var other = BuildService(companera.ExternalSubject);
        var followed = (await service.StartFollowUpAsync(StartFollowUpCommand(enfermera, eventId, await StartAndSaveAsync(enfermera, eventId)))).Value;
        var revision = (await service.ActivateUrgentProtocolAsync(ActivateCommand(enfermera, eventId, followed))).Value;
        Assert.Empty(await FollowUpsAsync(enfermera));
        var contactedAt = DateTimeOffset.UtcNow.AddMinutes(-10);

        revision = (await service.RecordUrgentProtocolEntryAsync(EntryCommand(enfermera, eventId, revision, UrgentProtocolEntryType.Actuacion, "Oxigenoterapia a 3 l/min."))).Value;
        var stale = await other.RecordUrgentProtocolEntryAsync(EntryCommand(companera, eventId, revision - 1, UrgentProtocolEntryType.Evolucion, "x"));
        var emptyEvolution = await other.RecordUrgentProtocolEntryAsync(EntryCommand(companera, eventId, revision, UrgentProtocolEntryType.Evolucion, " "));
        revision = (await other.RecordUrgentProtocolEntryAsync(EntryCommand(companera, eventId, revision, UrgentProtocolEntryType.Evolucion, "Satura 94 % con O₂."))).Value;
        var withoutService = await service.RecordUrgentProtocolEntryAsync(EntryCommand(enfermera, eventId, revision, UrgentProtocolEntryType.Contacto, contactedAt: contactedAt));
        var future = await service.RecordUrgentProtocolEntryAsync(EntryCommand(enfermera, eventId, revision, UrgentProtocolEntryType.Contacto,
            service: "112", contactedAt: DateTimeOffset.UtcNow.AddHours(1)));
        var withoutTime = await service.RecordUrgentProtocolEntryAsync(EntryCommand(enfermera, eventId, revision, UrgentProtocolEntryType.Contacto, service: "112"));
        var contact = await service.RecordUrgentProtocolEntryAsync(EntryCommand(enfermera, eventId, revision, UrgentProtocolEntryType.Contacto,
            " Pide ambulancia. ", " 112 ", contactedAt));

        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, emptyEvolution.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutService.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, future.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutTime.Error!.Code);
        Assert.True(contact.Ok);

        var entries = (await DetailAsync(companera, eventId)).UrgentProtocol!.Entries;
        Assert.Equal(new[] { UrgentProtocolEntryType.Actuacion, UrgentProtocolEntryType.Evolucion, UrgentProtocolEntryType.Contacto },
            entries.Select(e => e.Type));
        Assert.Equal(new[] { false, true, false }, entries.Select(e => e.ByCurrentAccount));
        Assert.Equal("112", entries[2].Service);
        Assert.Equal("Pide ambulancia.", entries[2].Text);
        Assert.True(Math.Abs((entries[2].ContactedAt!.Value - contactedAt).TotalSeconds) < 1);
        var listed = Assert.Single(await ProtocolsAsync(companera));
        Assert.Equal(UrgentProtocolEntryType.Contacto, listed.LastEntryType);
        foreach (var code in new[] { "URGENT_PROTOCOL_ACTION", "URGENT_PROTOCOL_EVOLUTION", "URGENT_PROTOCOL_CONTACT" })
        {
            Assert.Equal(1, await CountAuditAsync(eventId, code));
        }
    }

    [Fact]
    public async Task ProtocoloUrgente_SeCierraDeFormaIdempotente_YCierraLaValoracion()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var service = BuildService(enfermera.ExternalSubject);
        var revision = (await service.ActivateUrgentProtocolAsync(ActivateCommand(enfermera, eventId, await StartAndSaveAsync(enfermera, eventId)))).Value;
        var operationId = Guid.NewGuid();

        var closed = await service.CloseClinicalEventAsync(CloseCommand(enfermera, eventId, revision, operationId));
        var repeated = await service.CloseClinicalEventAsync(CloseCommand(enfermera, eventId, revision, operationId));
        var entryAfter = await service.RecordUrgentProtocolEntryAsync(EntryCommand(enfermera, eventId, closed.Value, UrgentProtocolEntryType.Actuacion, "Tarde."));

        Assert.True(closed.Ok);
        Assert.Equal(closed.Value, repeated.Value);
        Assert.Equal(ApplicationFailureCode.Conflict, entryAfter.Error!.Code);
        Assert.Equal(ClinicalEventStatus.Cerrado, (await DetailAsync(enfermera, eventId)).Status);
        Assert.Empty(await ProtocolsAsync(enfermera));
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal("CERRADA", await connection.ExecuteScalarAsync<string>(
            "SELECT estado_codigo FROM dbo.valoraciones_enfermeria WHERE evento_id = @EventId", new { EventId = eventId }));
    }

    [Fact]
    public async Task ProtocoloUrgente_FueraDeAmbitoOConOtroPerfil_SeDeniega_YLaBaseDeDatosLoProtege()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var revision = await StartAndSaveAsync(enfermera, eventId);
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        var medica = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, enfermera.CenterId, enfermera.UnitId);

        var byOutsider = await BuildService(outsider.ExternalSubject).ActivateUrgentProtocolAsync(ActivateCommand(outsider, eventId, revision));
        var byMedica = await BuildService(medica.ExternalSubject).ActivateUrgentProtocolAsync(ActivateCommand(medica, eventId, revision));
        var listByMedica = await BuildService(medica.ExternalSubject).ListUrgentProtocolsAsync(new ListUrgentProtocolsCommand(medica.ProfileScopeId, medica.CenterId));
        Assert.Equal(ApplicationFailureCode.AccessDenied, byOutsider.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byMedica.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, listByMedica.Error!.Code);

        var activated = (await BuildService(enfermera.ExternalSubject).ActivateUrgentProtocolAsync(ActivateCommand(enfermera, eventId, revision))).Value;
        Assert.True((await BuildService(enfermera.ExternalSubject).RecordUrgentProtocolEntryAsync(
            EntryCommand(enfermera, eventId, activated, UrgentProtocolEntryType.Actuacion, "Vía periférica."))).Ok);
        Assert.Empty(await ProtocolsAsync(outsider));

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        foreach (var (sql, expected) in new[]
        {
            ("UPDATE dbo.protocolos_urgentes SET nota_activacion = 'x' WHERE evento_id = @EventId", "URGENT_PROTOCOL_IMMUTABLE"),
            ("DELETE FROM dbo.protocolos_urgentes WHERE evento_id = @EventId", "URGENT_PROTOCOL_IMMUTABLE"),
            ("UPDATE r SET texto = 'x' FROM dbo.protocolo_urgente_registros r JOIN dbo.protocolos_urgentes p ON p.id = r.protocolo_id WHERE p.evento_id = @EventId", "URGENT_PROTOCOL_ENTRY_IMMUTABLE"),
            ("DELETE r FROM dbo.protocolo_urgente_registros r JOIN dbo.protocolos_urgentes p ON p.id = r.protocolo_id WHERE p.evento_id = @EventId", "URGENT_PROTOCOL_ENTRY_IMMUTABLE"),
            ("INSERT INTO dbo.protocolo_urgente_registros (id, protocolo_id, tipo_codigo, texto, registrado_por_cuenta_id, registrado_en) SELECT NEWID(), p.id, 'CONTACTO', 'Sin servicio', p.activado_por_cuenta_id, SYSUTCDATETIME() FROM dbo.protocolos_urgentes p WHERE p.evento_id = @EventId", "CK_pur_tipo"),
            ("INSERT INTO dbo.protocolo_urgente_registros (id, protocolo_id, tipo_codigo, servicio_contactado, contactado_en, registrado_por_cuenta_id, registrado_en) SELECT NEWID(), p.id, 'CONTACTO', '112', DATEADD(HOUR, 1, SYSUTCDATETIME()), p.activado_por_cuenta_id, SYSUTCDATETIME() FROM dbo.protocolos_urgentes p WHERE p.evento_id = @EventId", "CK_pur_contacto_no_futuro"),
            ("UPDATE dbo.eventos_asistenciales SET estado_codigo = 'EN_VALORACION', revision = revision + 1 WHERE id = @EventId", "CLINICAL_EVENT_TRANSITION_INVALID"),
            ("UPDATE dbo.eventos_asistenciales SET estado_codigo = 'ESCALADO_MEDICINA', revision = revision + 1 WHERE id = @EventId", "CLINICAL_EVENT_TRANSITION_INVALID"),
        })
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, new { EventId = eventId }));
            Assert.Contains(expected, ex.Message);
        }
    }

    /// <summary>Datos automáticos de prueba: en la aplicación los reúne la Web (ReferralReportBuilder, con su
    /// propio test); aquí basta con que sean secciones automáticas.</summary>
    internal static readonly IReadOnlyList<ReferralReportSection> ReferralSections =
    [
        new("Identificación del residente y del centro", true, ["Nombre: Residente de prueba"]),
        new("Evolución", true, ["Sin datos registrados."]),
    ];

    internal const string ReferralReason = "Desaturación que no remonta con oxigenoterapia.";

    internal static string ReferralHash(string reason = ReferralReason) =>
        ReferralReportContent.Compose(ReferralSections, new ReferralReportInput(reason, null)).Hash();

    internal static SignReferralReportCommand SignCommand(
        SeededProfile seed, Guid eventId, int revision, Guid operationId, string? reason = ReferralReason, string? hash = null) =>
        new(seed.ProfileScopeId, seed.CenterId, eventId, revision, operationId, ReferralSections, reason, null, hash ?? ReferralHash());

    internal static RecordFamilyCallAttemptCommand CallCommand(
        SeededProfile seed, Guid eventId, int revision, string? contact = "Su hija, contacto de referencia",
        DateTimeOffset? calledAt = null, FamilyCallResult? result = FamilyCallResult.NoContesta, string? note = null) =>
        new(seed.ProfileScopeId, seed.CenterId, eventId, revision, contact, calledAt ?? DateTimeOffset.UtcNow.AddMinutes(-2), result, note);

    internal static async Task<(Guid Id, byte[] Pdf, string PdfHash)> ReadReportAsync(Guid eventId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.QuerySingleAsync<(Guid, byte[], string)>(
            "SELECT id, pdf, huella_pdf FROM dbo.informes_derivacion WHERE evento_id = @EventId", new { EventId = eventId });
    }

    private static async Task<int> ActivatedProtocolAsync(SeededProfile seed, Guid eventId) =>
        (await BuildService(seed.ExternalSubject).ActivateUrgentProtocolAsync(
            ActivateCommand(seed, eventId, await StartAndSaveAsync(seed, eventId)))).Value;

    [Fact]
    public async Task Derivacion_ExigeMotivoYLaHuellaDeLaVistaPrevia_EsIdempotente_YElEventoSigueEnElProtocolo()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var service = BuildService(enfermera.ExternalSubject);
        var revision = await ActivatedProtocolAsync(enfermera, eventId);
        var operationId = Guid.NewGuid();

        var withoutReason = await service.SignReferralReportAsync(SignCommand(enfermera, eventId, revision, Guid.NewGuid(), " ", "x"));
        var changed = await service.SignReferralReportAsync(SignCommand(enfermera, eventId, revision, Guid.NewGuid(), hash: new string('0', 64)));
        var stale = await service.SignReferralReportAsync(SignCommand(enfermera, eventId, revision - 1, Guid.NewGuid()));
        var signed = await service.SignReferralReportAsync(SignCommand(enfermera, eventId, revision, operationId));
        var repeated = await service.SignReferralReportAsync(SignCommand(enfermera, eventId, revision, operationId));
        var second = await service.SignReferralReportAsync(SignCommand(enfermera, eventId, signed.Value, Guid.NewGuid()));

        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutReason.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, changed.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        Assert.True(signed.Ok);
        Assert.Equal(signed.Value, repeated.Value);
        Assert.Equal(ApplicationFailureCode.Conflict, second.Error!.Code);

        var detail = await DetailAsync(enfermera, eventId);
        Assert.Equal(ClinicalEventStatus.ProtocoloUrgente, detail.Status);
        Assert.Equal(SystemProfile.Enfermeria, detail.Referral!.Profile);
        Assert.Equal(ReferralReason, detail.Referral.Reason);
        Assert.True(detail.Referral.SignedByCurrentAccount);
        Assert.Equal(ReferralHash(), detail.Referral.ContentHash);
        Assert.Empty(detail.Referral.CallAttempts);
        Assert.Equal(eventId, Assert.Single(await ProtocolsAsync(enfermera)).EventId);

        var (reportId, pdf, pdfHash) = await ReadReportAsync(eventId);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(pdf)), pdfHash);
        Assert.Equal(1, await CountAuditAsync(reportId, "REFERRAL_REPORT_SIGN"));

        var downloaded = await service.DownloadReferralReportAsync(new DownloadReferralReportCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId));
        Assert.Equal(pdf, downloaded.Value!.Content);
        Assert.Equal(1, await CountAuditAsync(reportId, "REFERRAL_REPORT_DOWNLOAD"));

        // Tras firmar se sigue documentando en el protocolo.
        Assert.True((await service.RecordUrgentProtocolEntryAsync(
            EntryCommand(enfermera, eventId, signed.Value, UrgentProtocolEntryType.Evolucion, "Sale en ambulancia."))).Ok);
    }

    [Fact]
    public async Task Derivacion_ElCierreExigeActualizacionRelevante_YAlMenosUnIntentoDeLlamada()
    {
        var (enfermera, companera, eventId) = await SeedOwnEventAsync();
        var (otra, _, notReferredId) = await SeedOwnEventAsync();
        var service = BuildService(enfermera.ExternalSubject);
        var other = BuildService(companera.ExternalSubject);
        var notReferred = await ActivatedProtocolAsync(otra, notReferredId);
        var revision = (await service.SignReferralReportAsync(
            SignCommand(enfermera, eventId, await ActivatedProtocolAsync(enfermera, eventId), Guid.NewGuid()))).Value;

        var callWithoutReport = await BuildService(otra.ExternalSubject).RecordFamilyCallAttemptAsync(CallCommand(otra, notReferredId, notReferred));
        var closeWithoutCall = await service.CloseClinicalEventAsync(CloseCommand(enfermera, eventId, revision, Guid.NewGuid(),
            FamilyCommunicationDecision.Preparar, FamilyCommunicationType.Relevante, "Ha sido trasladado a Urgencias."));
        var future = await service.RecordFamilyCallAttemptAsync(CallCommand(enfermera, eventId, revision, calledAt: DateTimeOffset.UtcNow.AddHours(1)));
        var withoutContact = await service.RecordFamilyCallAttemptAsync(CallCommand(enfermera, eventId, revision, " "));
        var withoutResult = await service.RecordFamilyCallAttemptAsync(CallCommand(enfermera, eventId, revision, result: null));
        revision = (await service.RecordFamilyCallAttemptAsync(CallCommand(enfermera, eventId, revision))).Value;
        var stale = await other.RecordFamilyCallAttemptAsync(CallCommand(companera, eventId, revision - 1));
        revision = (await other.RecordFamilyCallAttemptAsync(CallCommand(companera, eventId, revision, "Su hija", result: FamilyCallResult.Contactado,
            note: " Informada del traslado. "))).Value;

        Assert.Equal(ApplicationFailureCode.InvalidInput, callWithoutReport.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, closeWithoutCall.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, future.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutContact.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutResult.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);

        var attempts = (await DetailAsync(companera, eventId)).Referral!.CallAttempts;
        Assert.Equal(new[] { FamilyCallResult.NoContesta, FamilyCallResult.Contactado }, attempts.Select(a => a.Result));
        Assert.Equal(new[] { false, true }, attempts.Select(a => a.ByCurrentAccount));
        Assert.Equal("Informada del traslado.", attempts[1].Note);

        var noComunicar = await service.CloseClinicalEventAsync(CloseCommand(enfermera, eventId, revision, Guid.NewGuid()));
        var ordinaria = await service.CloseClinicalEventAsync(CloseCommand(enfermera, eventId, revision, Guid.NewGuid(),
            FamilyCommunicationDecision.Preparar, FamilyCommunicationType.Ordinaria, "Ha sido trasladado a Urgencias."));
        var relevante = await service.CloseClinicalEventAsync(CloseCommand(enfermera, eventId, revision, Guid.NewGuid(),
            FamilyCommunicationDecision.Preparar, FamilyCommunicationType.Relevante, "Ha sido trasladado a Urgencias."));
        var notReferredClosed = await BuildService(otra.ExternalSubject).CloseClinicalEventAsync(CloseCommand(otra, notReferredId, notReferred, Guid.NewGuid()));

        Assert.Equal(ApplicationFailureCode.InvalidInput, noComunicar.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, ordinaria.Error!.Code);
        Assert.True(relevante.Ok);
        Assert.True(notReferredClosed.Ok);
        var closed = await DetailAsync(enfermera, eventId);
        Assert.Equal(ClinicalEventStatus.Cerrado, closed.Status);
        Assert.Equal(FamilyCommunicationType.Relevante, closed.Closure!.Communication!.Type);
        Assert.Equal(2, attempts.Count);
    }

    [Fact]
    public async Task Derivacion_OtroPerfilOFueraDeAmbito_NoFirmaNiDescarga_YLaBaseDeDatosLaProtege()
    {
        var (enfermera, companera, eventId) = await SeedOwnEventAsync();
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        var medica = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, enfermera.CenterId, enfermera.UnitId);
        var revision = await ActivatedProtocolAsync(enfermera, eventId);

        var byOutsider = await BuildService(outsider.ExternalSubject).SignReferralReportAsync(SignCommand(outsider, eventId, revision, Guid.NewGuid()));
        var byMedica = await BuildService(medica.ExternalSubject).SignReferralReportAsync(SignCommand(medica, eventId, revision, Guid.NewGuid()));
        Assert.Equal(ApplicationFailureCode.AccessDenied, byOutsider.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byMedica.Error!.Code);

        revision = (await BuildService(enfermera.ExternalSubject).SignReferralReportAsync(SignCommand(enfermera, eventId, revision, Guid.NewGuid()))).Value;
        Assert.True((await BuildService(enfermera.ExternalSubject).RecordFamilyCallAttemptAsync(CallCommand(enfermera, eventId, revision))).Ok);

        DownloadReferralReportCommand Download(SeededProfile seed) => new(seed.ProfileScopeId, seed.CenterId, eventId);
        Assert.True((await BuildService(companera.ExternalSubject).DownloadReferralReportAsync(Download(companera))).Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied,
            (await BuildService(outsider.ExternalSubject).DownloadReferralReportAsync(Download(outsider))).Error!.Code);
        // Un ámbito de Medicina solo ve eventos escalados: este no lo es.
        Assert.Equal(ApplicationFailureCode.AccessDenied,
            (await BuildService(medica.ExternalSubject).DownloadReferralReportAsync(Download(medica))).Error!.Code);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        foreach (var (sql, expected) in new[]
        {
            ("UPDATE dbo.informes_derivacion SET motivo = 'x' WHERE evento_id = @EventId", "REFERRAL_REPORT_IMMUTABLE"),
            ("DELETE FROM dbo.informes_derivacion WHERE evento_id = @EventId", "REFERRAL_REPORT_IMMUTABLE"),
            ("UPDATE dbo.intentos_llamada_familia SET nota = 'x' WHERE evento_id = @EventId", "FAMILY_CALL_ATTEMPT_IMMUTABLE"),
            ("DELETE FROM dbo.intentos_llamada_familia WHERE evento_id = @EventId", "FAMILY_CALL_ATTEMPT_IMMUTABLE"),
            ("INSERT INTO dbo.intentos_llamada_familia (id, informe_id, evento_id, contacto, llamado_en, resultado_codigo, registrado_por_cuenta_id, registrado_en) SELECT NEWID(), d.id, d.evento_id, 'Hija', SYSUTCDATETIME(), 'OTRO', d.firmado_por_cuenta_id, SYSUTCDATETIME() FROM dbo.informes_derivacion d WHERE d.evento_id = @EventId", "CK_ilf_resultado"),
            ("INSERT INTO dbo.intentos_llamada_familia (id, informe_id, evento_id, contacto, llamado_en, resultado_codigo, registrado_por_cuenta_id, registrado_en) SELECT NEWID(), d.id, d.evento_id, 'Hija', DATEADD(HOUR, 1, SYSUTCDATETIME()), 'CONTACTADO', d.firmado_por_cuenta_id, SYSUTCDATETIME() FROM dbo.informes_derivacion d WHERE d.evento_id = @EventId", "CK_ilf_no_futuro"),
        })
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, new { EventId = eventId }));
            Assert.Contains(expected, ex.Message);
        }
    }
}

file sealed class FixedSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
