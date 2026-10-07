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

    /// <summary>ENF-18/MED-20: la ficha muestra los eventos abiertos del residente con la regla de las bandejas:
    /// Enfermería ve los de sus unidades; Medicina, solo los escalados (y sus eventos propios). Los cerrados no
    /// entran, y otro centro no ve nada.</summary>
    [Fact]
    public async Task EventosAbiertos_EnfermeriaVeLosSuyos_MedicinaSoloLosEscalados_YLosCerradosNoEntran()
    {
        var (enfermera, medica, residentId) = await SeedResidentAsync();
        var nursing = BuildService(enfermera.ExternalSubject);
        var closedId = await RegisterAsync(enfermera, residentId, "Caída sin lesiones en el baño.");
        var closeRevision = await StartAndSaveAsync(enfermera, closedId);
        Assert.True((await nursing.CloseClinicalEventAsync(new CloseClinicalEventCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, closedId, closeRevision, Guid.NewGuid(),
            FamilyCommunicationDecision.NoComunicar, null, null))).Ok);
        var openId = await RegisterAsync(enfermera, residentId, "Inapetencia en la cena.");
        var escalatedId = await RegisterAsync(enfermera, residentId, "Disnea de esfuerzo.");
        var escalateRevision = await StartAndSaveAsync(enfermera, escalatedId);
        Assert.True((await nursing.EscalateClinicalEventAsync(EscalateCommand(enfermera, escalatedId, escalateRevision))).Ok);

        var nurseEvents = await OpenEventsAsync(enfermera, residentId, SystemProfile.Enfermeria);
        Assert.Equal([escalatedId, openId], nurseEvents.Select(e => e.EventId));
        Assert.Equal(ClinicalEventStatus.EscaladoMedicina, nurseEvents[0].Status);
        Assert.True(nurseEvents[0].Escalated);
        Assert.Equal(ClinicalEventStatus.Pendiente, nurseEvents[1].Status);
        Assert.Equal("Inapetencia en la cena.", nurseEvents[1].Observation);
        Assert.Equal(SystemProfile.Enfermeria, nurseEvents[1].AuthorProfile);

        Assert.Equal([escalatedId], (await OpenEventsAsync(medica, residentId, SystemProfile.Medicina)).Select(e => e.EventId));

        var otroCentro = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        Assert.Empty(await OpenEventsAsync(otroCentro, residentId, SystemProfile.Enfermeria));
        var auxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, enfermera.CenterId, enfermera.UnitId);
        var denied = await ListOpenEvents(auxiliar).ExecuteAsync(
            new ListOpenEventsCommand(auxiliar.ProfileScopeId, auxiliar.CenterId, residentId, SystemProfile.Auxiliar));
        Assert.Equal(ApplicationFailureCode.AccessDenied, denied.Error!.Code);
    }

    private static ListOpenEvents ListOpenEvents(SeededProfile profile) =>
        new(new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new SqlChangeInboxDirectory(TestDatabase.ConnectionFactory),
            new FixedHistorialSessionIdentityProvider(profile.ExternalSubject));

    private static async Task<IReadOnlyList<OpenEventSummary>> OpenEventsAsync(SeededProfile profile, ResidentId residentId, SystemProfile perfil)
    {
        var result = await ListOpenEvents(profile).ExecuteAsync(
            new ListOpenEventsCommand(profile.ProfileScopeId, profile.CenterId, residentId, perfil));
        Assert.True(result.Ok, result.Error?.Message);
        return result.Value!;
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

    /// <summary>DIR-06 y DIR-07: Dirección Clínica lee la línea temporal y el historial de eventos cerrados de un residente de su
    /// ámbito solo con permiso clínico, finalidad y justificación, y cada lectura deja su fila de auditoría; ve todos los eventos
    /// (también los escalados). Sin permiso o con el ámbito de otro centro no recibe nada ni deja auditoría.</summary>
    [Fact]
    public async Task Direccion_LeeLineaTemporalYEventosCerrados_Auditados_ConTodosLosEventosDeSuAmbito()
    {
        var (enfermera, _, residentId) = await SeedResidentAsync();
        await SignBaselineAsync(enfermera, residentId, BaselineReason.Alta);
        var nursing = BuildService(enfermera.ExternalSubject);
        var closedId = await RegisterAsync(enfermera, residentId, "Caída sin lesiones en el baño.");
        Assert.True((await nursing.CloseClinicalEventAsync(new CloseClinicalEventCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, closedId, await StartAndSaveAsync(enfermera, closedId), Guid.NewGuid(),
            FamilyCommunicationDecision.NoComunicar, null, null))).Ok);
        var escalatedId = await RegisterAsync(enfermera, residentId, "Disnea de esfuerzo.");
        Assert.True((await nursing.EscalateClinicalEventAsync(
            EscalateCommand(enfermera, escalatedId, await StartAndSaveAsync(enfermera, escalatedId)))).Ok);

        var direccion = await SeedFixture.AddProfileToCenterAsync(
            SystemProfile.DireccionClinica, enfermera.CenterId, enfermera.UnitId, ["CLINICAL_DETAIL_READ"]);
        var sinPermiso = await SeedFixture.AddProfileToCenterAsync(SystemProfile.DireccionClinica, enfermera.CenterId, enfermera.UnitId, []);
        var otroCentro = await SeedFixture.CreateProfileAsync(SystemProfile.DireccionClinica, ["CLINICAL_DETAIL_READ"]);
        Task<ApplicationResult<DirectionBaselineRead>> Read(SeededProfile who, string type) =>
            new ReadDirectionBaseline(
                    new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory), new FixedHistorialSessionIdentityProvider(who.ExternalSubject),
                    _baselines, new SqlChangeInboxDirectory(TestDatabase.ConnectionFactory))
                .ExecuteAsync(new ReadDirectionBaselineCommand(
                    who.ProfileScopeId, who.CenterId, residentId, type, "CONTINUIDAD_ASISTENCIAL",
                    Guid.NewGuid(), "Revisión de continuidad (prueba)."));
        async Task<int> AuditedAsync(string type)
        {
            using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
            return await Dapper.SqlMapper.ExecuteScalarAsync<int>(connection,
                "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE residente_id = @Id AND accion_codigo = 'CLINICAL_DETAIL_READ' AND tipo_recurso = @type",
                new { Id = residentId.Value, type });
        }

        foreach (var (who, type) in new[] { (sinPermiso, "RESIDENT_TIMELINE"), (otroCentro, "RESIDENT_TIMELINE"), (sinPermiso, "CLOSED_EVENTS_HISTORY"), (otroCentro, "CLOSED_EVENTS_HISTORY") })
        {
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await Read(who, type)).Error!.Code);
        }
        // Enfermería no entra por esta lectura aunque el residente sea de su ámbito.
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Read(enfermera, "RESIDENT_TIMELINE")).Error!.Code);
        Assert.Equal(0, await AuditedAsync("RESIDENT_TIMELINE") + await AuditedAsync("CLOSED_EVENTS_HISTORY"));

        var timeline = await Read(direccion, "RESIDENT_TIMELINE");
        Assert.True(timeline.Ok, timeline.Error?.Message);
        Assert.Empty(timeline.Value!.Headers);
        Assert.Null(timeline.Value.ClosedEvents);
        Assert.Equal(new[] { closedId, escalatedId }.Order(), timeline.Value.Timeline!.OfType<TimelineEntry.EventRegistered>().Select(e => e.EventId!.Value).Order());
        Assert.Single(timeline.Value.Timeline!.OfType<TimelineEntry.BaselineSigned>());
        Assert.Equal(1, await AuditedAsync("RESIDENT_TIMELINE"));
        Assert.Equal(0, await AuditedAsync("CLOSED_EVENTS_HISTORY"));

        var closed = await Read(direccion, "CLOSED_EVENTS_HISTORY");
        Assert.True(closed.Ok, closed.Error?.Message);
        Assert.Null(closed.Value!.Timeline);
        Assert.Equal(closedId, Assert.Single(closed.Value.ClosedEvents!).EventId);
        Assert.Equal(1, await AuditedAsync("CLOSED_EVENTS_HISTORY"));
    }

    /// <summary>DIR-15: Dirección Clínica ve, auditado, el original, la corrección y la rectificación de una valoración con su motivo, y
    /// los basales firmados como versiones vinculadas; sin permiso no ve nada ni deja auditoría.</summary>
    [Fact]
    public async Task Direccion_LeeCorreccionesYRectificaciones_Auditadas_ConLosBasalesComoVersionesVinculadas()
    {
        var (enfermera, _, residentId) = await SeedResidentAsync();
        await SignBaselineAsync(enfermera, residentId, BaselineReason.Alta);
        await SignBaselineAsync(enfermera, residentId, BaselineReason.RevisionProgramada);
        var eventId = await RegisterAsync(enfermera, residentId, "Disnea nocturna.");
        var revision = await StartAndSaveAsync(enfermera, eventId);
        Assert.True((await BuildService(enfermera.ExternalSubject).CloseClinicalEventAsync(new CloseClinicalEventCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, eventId, revision, Guid.NewGuid(), FamilyCommunicationDecision.NoComunicar, null, null))).Ok);
        Assert.True((await BuildService(enfermera.ExternalSubject).CorrectNursingAssessmentAsync(new CorrectNursingAssessmentCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, eventId, 0, "Hallazgo anotado en el lado equivocado.", "Crepitantes en base izquierda.", null,
            "Se incorpora a 45º.", null, null, 37.8m, 130, 80, 92, 22, 93, RespiratorySupportCode.AireAmbiente, null, null, null, null, null))).Ok);
        Assert.True((await BuildService(enfermera.ExternalSubject, TimeSpan.Zero).RectifyAssessmentAsync(new RectifyAssessmentCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, eventId, 0, "La crepitación era en la base izquierda.", "Aclaración posterior.",
            SystemProfile.Enfermeria))).Ok);

        // Con nombre visible en la cuenta, Dirección lo ve junto al perfil de quien guardó, corrigió y rectificó.
        using (var nameConnection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await Dapper.SqlMapper.ExecuteAsync(nameConnection, "UPDATE dbo.cuentas SET nombre_visible = N'Marta Ficticia' WHERE id = @Id", new { Id = enfermera.AccountId.Value });
        }
        var direccion = await SeedFixture.AddProfileToCenterAsync(
            SystemProfile.DireccionClinica, enfermera.CenterId, enfermera.UnitId, ["CLINICAL_DETAIL_READ"]);
        var sinPermiso = await SeedFixture.AddProfileToCenterAsync(SystemProfile.DireccionClinica, enfermera.CenterId, enfermera.UnitId, []);
        Task<ApplicationResult<DirectionBaselineRead>> Read(SeededProfile who, string type = "ASSESSMENT_AMENDMENTS") =>
            new ReadDirectionBaseline(
                    new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory), new FixedHistorialSessionIdentityProvider(who.ExternalSubject),
                    _baselines, new SqlChangeInboxDirectory(TestDatabase.ConnectionFactory))
                .ExecuteAsync(new ReadDirectionBaselineCommand(
                    who.ProfileScopeId, who.CenterId, residentId, type, "TRAZABILIDAD_DOCUMENTAL", Guid.NewGuid(), "Verificación documental (prueba)."));
        async Task<int> AuditedAsync()
        {
            using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
            return await Dapper.SqlMapper.ExecuteScalarAsync<int>(connection,
                "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE residente_id = @Id AND accion_codigo = 'CLINICAL_DETAIL_READ' AND tipo_recurso = 'ASSESSMENT_AMENDMENTS'",
                new { Id = residentId.Value });
        }

        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Read(sinPermiso)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Read(enfermera)).Error!.Code);
        Assert.Equal(0, await AuditedAsync());

        var result = await Read(direccion);
        Assert.True(result.Ok, result.Error?.Message);
        var amendments = result.Value!.Amendments!;
        var item = Assert.Single(amendments.Assessments);
        Assert.Equal(eventId, item.EventId);
        Assert.Equal(SystemProfile.Enfermeria, item.Profile);
        Assert.IsType<TimelineEntry.NursingAssessmentSaved>(item.Original);
        var correction = Assert.IsType<TimelineEntry.NursingAssessmentCorrected>(Assert.Single(item.Corrections));
        Assert.Equal("Crepitantes en base izquierda.", correction.Content.Findings);
        Assert.Equal("Hallazgo anotado en el lado equivocado.", correction.Reason);
        Assert.Equal("Aclaración posterior.", Assert.Single(item.Rectifications).Reason);
        Assert.Equal([2, 1], amendments.Baselines.Select(b => b.VersionNumber));
        Assert.Equal("Marta Ficticia", item.Original!.AuthorName);
        Assert.Equal("Marta Ficticia", correction.AuthorName);
        Assert.Equal("Marta Ficticia", item.Rectifications[0].AuthorName);
        Assert.Equal(1, amendments.Baselines[0].ReplacesVersionNumber);
        Assert.Equal(1, await AuditedAsync());

        // La línea temporal de DIR-06 no lleva nombres: solo la lectura de correcciones y rectificaciones los pide.
        var timeline = await Read(direccion, "RESIDENT_TIMELINE");
        Assert.True(timeline.Ok, timeline.Error?.Message);
        Assert.All(timeline.Value!.Timeline!, entry => Assert.Null(entry.AuthorName));
    }

    /// <summary>DIR-14: Dirección Clínica ve, auditado, quién hizo qué sobre el residente (solo acciones clínicas, no las administrativas),
    /// con el nombre de la cuenta, su perfil, la unidad y la finalidad de las lecturas; la propia lectura aparece la primera. Sin permiso no
    /// ve nada ni deja auditoría.</summary>
    [Fact]
    public async Task Direccion_LeeLaTrazabilidadClinica_Auditada_SoloConAccionesClinicas()
    {
        var (enfermera, _, residentId) = await SeedResidentAsync();
        using (var nameConnection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await Dapper.SqlMapper.ExecuteAsync(nameConnection, "UPDATE dbo.cuentas SET nombre_visible = N'Marta Ficticia' WHERE id = @Id", new { Id = enfermera.AccountId.Value });
        }
        await SignBaselineAsync(enfermera, residentId, BaselineReason.Alta);
        var eventId = await RegisterAsync(enfermera, residentId, "Disnea nocturna.");
        Assert.True((await BuildService(enfermera.ExternalSubject).CloseClinicalEventAsync(new CloseClinicalEventCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, eventId, await StartAndSaveAsync(enfermera, eventId), Guid.NewGuid(),
            FamilyCommunicationDecision.NoComunicar, null, null))).Ok);

        var direccion = await SeedFixture.AddProfileToCenterAsync(
            SystemProfile.DireccionClinica, enfermera.CenterId, enfermera.UnitId, ["CLINICAL_DETAIL_READ"]);
        var sinPermiso = await SeedFixture.AddProfileToCenterAsync(SystemProfile.DireccionClinica, enfermera.CenterId, enfermera.UnitId, []);
        Task<ApplicationResult<DirectionBaselineRead>> Read(SeededProfile who) =>
            new ReadDirectionBaseline(
                    new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory), new FixedHistorialSessionIdentityProvider(who.ExternalSubject),
                    _baselines, new SqlChangeInboxDirectory(TestDatabase.ConnectionFactory))
                .ExecuteAsync(new ReadDirectionBaselineCommand(
                    who.ProfileScopeId, who.CenterId, residentId, "CLINICAL_TRACEABILITY", "INCIDENCIA_RECLAMACION", Guid.NewGuid(), "Reclamación familiar (prueba)."));

        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Read(sinPermiso)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Read(enfermera)).Error!.Code);

        var result = await Read(direccion);
        Assert.True(result.Ok, result.Error?.Message);
        Assert.Empty(result.Value!.Headers);
        var page = result.Value.Traceability!;
        Assert.False(page.Truncated);
        var actions = page.Entries.Select(e => e.Action).ToList();
        // Lo clínico y la propia lectura, y nada administrativo (el alta del residente también está en la auditoría).
        Assert.Contains("BASELINE_DRAFT_CREATE", actions);
        Assert.Contains("BASELINE_SIGN", actions);
        Assert.Contains("CLINICAL_EVENT_REGISTER", actions);
        Assert.Contains("NURSING_ASSESSMENT_SAVE", actions);
        Assert.Contains("CLINICAL_EVENT_CLOSE", actions);
        Assert.DoesNotContain("RESIDENT_CREATE", actions);
        Assert.All(actions, action => Assert.Contains(action, ClinicalTraceability.ActionCodes));
        var own = page.Entries[0];
        Assert.Equal("CLINICAL_DETAIL_READ", own.Action);
        Assert.Equal("CLINICAL_TRACEABILITY", own.ResourceType);
        Assert.Equal("INCIDENCIA_RECLAMACION", own.Purpose);
        Assert.Equal(SystemProfile.DireccionClinica, own.ActorProfile);
        var closed = page.Entries.Single(e => e.Action == "CLINICAL_EVENT_CLOSE");
        Assert.Equal("Marta Ficticia", closed.ActorName);
        Assert.Equal(SystemProfile.Enfermeria, closed.ActorProfile);
        Assert.NotNull(closed.UnitName);
        Assert.Equal([.. page.Entries.Select(e => e.OccurredAt).OrderByDescending(t => t)], page.Entries.Select(e => e.OccurredAt));

        // Otro residente del mismo centro no se mezcla: la trazabilidad es la de este residente.
        var otherResident = await new SqlResidentRepository(TestDatabase.ConnectionFactory).CreateWithInitialLocationAsync(new CreateResidentInput(
            enfermera.AccountId, SystemProfile.Enfermeria, enfermera.CenterId, enfermera.UnitId, "Otro Residente", new DateOnly(1941, 1, 1),
            DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));
        await RegisterAsync(enfermera, otherResident.ResidentId, "Otra cosa.");
        var again = await Read(direccion);
        Assert.Equal(1, again.Value!.Traceability!.Entries.Count(e => e.Action == "CLINICAL_EVENT_REGISTER"));
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
