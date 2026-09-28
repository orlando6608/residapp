using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Recorre la misma cadena que AuxiliarApplicationServiceTests, pero para el vertical Enfermería
/// (grupo E1): identidad de sesión -> IProfileScopeDirectoryProvider/IEnfermeriaResidentDirectory ->
/// EnfermeriaApplicationService.</summary>
public class EnfermeriaApplicationServiceTests
{
    private static EnfermeriaApplicationService BuildService(string externalSubject)
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
            new ListPendingFamilyCommunications(scopes, changeInbox, session));
    }

    /// <summary>Un residente en la unidad de dos profesionales de Enfermería y un evento propio de la
    /// primera, para recorrer la valoración y la concurrencia entre ambas.</summary>
    private static async Task<(SeededProfile Enfermera, SeededProfile Companera, Guid EventId)> SeedOwnEventAsync(
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
    private static async Task<int> StartAndSaveAsync(SeededProfile seed, Guid eventId)
    {
        var service = BuildService(seed.ExternalSubject);
        var started = await service.StartNursingAssessmentAsync(new StartNursingAssessmentCommand(seed.ProfileScopeId, seed.CenterId, eventId, 1));
        return (await service.SaveNursingAssessmentAsync(SaveCommand(seed, eventId, started.Value))).Value;
    }

    private static CloseClinicalEventCommand CloseCommand(
        SeededProfile seed, Guid eventId, int revision, Guid operationId,
        FamilyCommunicationDecision? decision = FamilyCommunicationDecision.NoComunicar,
        FamilyCommunicationType? type = null, string? text = null) =>
        new(seed.ProfileScopeId, seed.CenterId, eventId, revision, operationId, decision, type, text);

    private static async Task<int> CountAuditAsync(Guid resourceId, string actionCode)
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
}

file sealed class FixedSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
