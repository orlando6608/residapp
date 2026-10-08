using Dapper;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Supervision;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.EnfermeriaApplicationServiceTests;

namespace ResidApp.IntegrationTests;

/// <summary>Dirección Clínica, bloque 1 (DIR-01 a DIR-04 y DIR-17): supervisión operativa en solo lectura. Cada prueba
/// crea su propio centro, así que los contadores son exactos aunque la base se comparta con otras pruebas.</summary>
public class DireccionSupervisionTests
{
    private static DireccionApplicationService BuildDireccion(string externalSubject) => new(
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory),
        new SqlSupervisionDirectory(TestDatabase.ConnectionFactory),
        new FixedDireccionSessionIdentityProvider(externalSubject),
        new SqlEnfermeriaResidentDirectory(TestDatabase.ConnectionFactory), new SqlProcessDeadlineRepository(TestDatabase.ConnectionFactory));

    /// <summary>Un centro con una unidad, una cuenta de Dirección y una de Enfermería, y un residente.</summary>
    private static async Task<(SeededProfile Direccion, SeededProfile Enfermera, ResidentId ResidentId)> SeedCenterAsync()
    {
        var direccion = await SeedFixture.CreateProfileAsync(SystemProfile.DireccionClinica);
        var admin = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, direccion.CenterId, direccion.UnitId);
        var enfermera = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, direccion.CenterId, direccion.UnitId);
        var resident = await new SqlResidentRepository(TestDatabase.ConnectionFactory).CreateWithInitialLocationAsync(new CreateResidentInput(
            admin.AccountId, SystemProfile.Administracion, direccion.CenterId, direccion.UnitId,
            "Residente Supervisión Dirección", new DateOnly(1941, 1, 1), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));
        return (direccion, enfermera, resident.ResidentId);
    }

    private static async Task<Guid> RegisterAsync(SeededProfile enfermera, ResidentId residentId, string text)
    {
        var registered = await BuildService(enfermera.ExternalSubject).RegisterClinicalEventAsync(new RegisterClinicalEventCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, residentId, text, DailyChangeClassification.Ordinario, null, Guid.NewGuid()));
        Assert.True(registered.Ok, registered.Error?.Message);
        return registered.Value!.EventId;
    }

    private static SupervisionQuery Query(SeededProfile seed) => new(seed.ProfileScopeId, seed.CenterId);

    [Fact]
    public async Task Panel_ContabilizaLosEpisodiosAbiertosDeLaUnidad_ConSuDenominador()
    {
        var (direccion, enfermera, residentId) = await SeedCenterAsync();
        await RegisterAsync(enfermera, residentId, "Tos productiva.");
        var inAssessment = await RegisterAsync(enfermera, residentId, "Mareo al levantarse.");
        var escalated = await RegisterAsync(enfermera, residentId, "Disnea progresiva.");
        var followUp = await RegisterAsync(enfermera, residentId, "Diarrea de dos días.");
        var service = BuildService(enfermera.ExternalSubject);
        Assert.True((await service.StartNursingAssessmentAsync(new StartNursingAssessmentCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, inAssessment, 1))).Ok);
        var revision = await StartAndSaveAsync(enfermera, escalated);
        Assert.True((await service.EscalateClinicalEventAsync(EscalateCommand(enfermera, escalated, revision))).Ok);
        revision = await StartAndSaveAsync(enfermera, followUp);
        var startedFollowUp = await service.StartFollowUpAsync(new StartFollowUpCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, followUp, revision, DateOnly.FromDateTime(DateTime.Today).AddDays(-3), null, "Vigilar."));
        Assert.True(startedFollowUp.Ok, startedFollowUp.Error?.Message);

        var panel = await BuildDireccion(direccion.ExternalSubject).ReadPanelAsync(Query(direccion));

        var unit = Assert.Single(panel.Value!);
        Assert.Equal(direccion.UnitId, unit.UnitId);
        Assert.Equal(4, unit.Open);
        Assert.Equal(1, unit.WithoutAssessment);
        Assert.Equal(1, unit.InAssessment);
        Assert.Equal(1, unit.Escalated);
        Assert.Equal(1, unit.FollowUps);
        Assert.Equal(1, unit.FollowUpsOverdue);
        Assert.Equal(0, unit.UrgentProtocols);
    }

    [Fact]
    public async Task Pendientes_SeFiltranPorTipoYUnidad_YLosCerradosSalen()
    {
        var (direccion, enfermera, residentId) = await SeedCenterAsync();
        await RegisterAsync(enfermera, residentId, "Tos productiva.");
        var escalated = await RegisterAsync(enfermera, residentId, "Disnea progresiva.");
        var service = BuildService(enfermera.ExternalSubject);
        var revision = await StartAndSaveAsync(enfermera, escalated);
        Assert.True((await service.EscalateClinicalEventAsync(EscalateCommand(enfermera, escalated, revision))).Ok);
        var dir = BuildDireccion(direccion.ExternalSubject);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var all = (await dir.ListPendingAsync(new ListSupervisionPendingQuery(direccion.ProfileScopeId, direccion.CenterId, today))).Value!;
        var onlyEscalated = (await dir.ListPendingAsync(new ListSupervisionPendingQuery(
            direccion.ProfileScopeId, direccion.CenterId, today, SupervisionPendingType.Escalado))).Value!;
        var otherUnit = (await dir.ListPendingAsync(new ListSupervisionPendingQuery(
            direccion.ProfileScopeId, direccion.CenterId, today, UnitId: Guid.NewGuid()))).Value!;

        Assert.Equal(2, all.TotalOpen);
        Assert.Equal(2, all.Episodes.Count);
        Assert.Equal(escalated, Assert.Single(onlyEscalated.Episodes).EventId);
        Assert.Equal(2, onlyEscalated.TotalOpen);
        Assert.Empty(otherUnit.Episodes);
        Assert.Equal("Residente Supervisión Dirección", all.Episodes[0].ResidentDisplayName);
    }

    [Fact]
    public async Task Episodio_MuestraSusHitosSinTexto()
    {
        var (direccion, enfermera, residentId) = await SeedCenterAsync();
        var eventId = await RegisterAsync(enfermera, residentId, "Observación clínica que Dirección no debe ver.");
        var revision = await StartAndSaveAsync(enfermera, eventId);
        Assert.True((await BuildService(enfermera.ExternalSubject).EscalateClinicalEventAsync(EscalateCommand(enfermera, eventId, revision))).Ok);

        var detail = (await BuildDireccion(direccion.ExternalSubject).FindEpisodeAsync(
            new FindSupervisionEpisodeQuery(direccion.ProfileScopeId, direccion.CenterId, eventId))).Value!;

        Assert.Equal(
            [SupervisionMilestoneKind.Registrado, SupervisionMilestoneKind.ValoracionIniciada, SupervisionMilestoneKind.Escalado],
            detail.Milestones.Select(m => m.Kind));
        Assert.Equal(ClinicalEventStatus.EscaladoMedicina, detail.Episode.Status);
        // Garantía estructural: los tipos de supervisión no tienen ningún campo de texto clínico.
        var textProperties = new[] { typeof(SupervisionEpisode), typeof(SupervisionMilestone), typeof(SupervisionEpisodeDetail) }
            .SelectMany(t => t.GetProperties()).Where(p => p.PropertyType == typeof(string)).Select(p => p.Name);
        Assert.Equal(["ResidentDisplayName", "UnitName"], textProperties.Order());
    }

    [Fact]
    public async Task OtrosPerfiles_NoEntran_YUnEpisodioAjenoNoSeRevela()
    {
        var (direccion, enfermera, residentId) = await SeedCenterAsync();
        var eventId = await RegisterAsync(enfermera, residentId, "Tos productiva.");
        var (otraDireccion, _, _) = await SeedCenterAsync();

        var asNurse = await BuildDireccion(enfermera.ExternalSubject).ReadPanelAsync(Query(enfermera));
        var foreignEpisode = await BuildDireccion(otraDireccion.ExternalSubject).FindEpisodeAsync(
            new FindSupervisionEpisodeQuery(otraDireccion.ProfileScopeId, otraDireccion.CenterId, eventId));
        var ghostEpisode = await BuildDireccion(direccion.ExternalSubject).FindEpisodeAsync(
            new FindSupervisionEpisodeQuery(direccion.ProfileScopeId, direccion.CenterId, Guid.NewGuid()));
        var foreignScope = await BuildDireccion(otraDireccion.ExternalSubject).ReadScopeAsync(Query(direccion));

        Assert.False(asNurse.Ok);
        Assert.False(foreignEpisode.Ok);
        Assert.False(ghostEpisode.Ok);
        Assert.False(foreignScope.Ok);
        // El mismo mensaje neutro: no se distingue un episodio ajeno de uno inexistente.
        Assert.Equal(foreignEpisode.Error!.Message, ghostEpisode.Error!.Message);
    }

    [Fact]
    public async Task Indicadores_CuentanLoDelPeriodoEnElAmbito_ConSusDenominadores()
    {
        var (direccion, enfermera, residentId) = await SeedCenterAsync();
        var companera = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, direccion.CenterId, direccion.UnitId);
        var medica = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, direccion.CenterId, direccion.UnitId);
        var service = BuildService(enfermera.ExternalSubject);
        // Cerrado por Enfermería.
        var closed = await RegisterAsync(enfermera, residentId, "Tos productiva.");
        var revision = await StartAndSaveAsync(enfermera, closed);
        Assert.True((await service.CloseClinicalEventAsync(CloseCommand(enfermera, closed, revision, Guid.NewGuid()))).Ok);
        // Escalado, con una indicación médica sin leer.
        var escalated = await RegisterAsync(enfermera, residentId, "Disnea progresiva.");
        revision = await StartAndSaveAsync(enfermera, escalated);
        Assert.True((await service.EscalateClinicalEventAsync(EscalateCommand(enfermera, escalated, revision))).Ok);
        revision = await MedicinaApplicationServiceTests.StartAndSaveMedicalAsync(medica, escalated);
        var indication = await MedicinaApplicationServiceTests.BuildMedicina(medica.ExternalSubject).RegisterMedicalIndicationAsync(
            MedicinaApplicationServiceTests.Indication(medica, escalated, revision));
        Assert.True(indication.Ok, indication.Error?.Message);
        // Protocolo urgente.
        var urgent = await RegisterAsync(enfermera, residentId, "Desaturación brusca.");
        revision = await StartAndSaveAsync(enfermera, urgent);
        Assert.True((await service.ActivateUrgentProtocolAsync(ActivateCommand(enfermera, urgent, revision))).Ok);
        // Seguimiento transferido y recibido por la compañera.
        var followUp = await RegisterAsync(enfermera, residentId, "Diarrea de dos días.");
        revision = await StartAndSaveAsync(enfermera, followUp);
        revision = (await service.StartFollowUpAsync(StartFollowUpCommand(enfermera, followUp, revision))).Value;
        revision = (await service.RecordFollowUpActionAsync(new RecordFollowUpActionCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, followUp, revision, FollowUpActionType.Transferencia,
            "Revisar a las 8.", EquipoEntranteId: await TransferTeamData.CreateAsync(enfermera, "Turno de noche")))).Value;
        var transfer = (await DetailAsync(companera, followUp)).FollowUp!.PendingTransfer!;
        Assert.True((await BuildService(companera.ExternalSubject).RecordFollowUpActionAsync(new RecordFollowUpActionCommand(
            companera.ProfileScopeId, companera.CenterId, followUp, revision, FollowUpActionType.Recepcion,
            TransferenciaId: transfer.Id))).Ok);
        // Un episodio de otro centro no cuenta.
        var (_, otraEnfermera, otroResidente) = await SeedCenterAsync();
        await RegisterAsync(otraEnfermera, otroResidente, "Tos productiva.");
        var dir = BuildDireccion(direccion.ExternalSubject);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await dir.ReadIndicatorsAsync(new ReadSupervisionIndicatorsQuery(direccion.ProfileScopeId, direccion.CenterId, today, today));
        var yesterday = (await dir.ReadIndicatorsAsync(new ReadSupervisionIndicatorsQuery(
            direccion.ProfileScopeId, direccion.CenterId, today.AddDays(-1), today.AddDays(-1)))).Value!;

        Assert.True(result.Ok, result.Error?.Message);
        var unit = Assert.Single(result.Value!.Units);
        Assert.Equal(direccion.UnitId, unit.UnitId);
        var c = unit.Counts;
        Assert.Equal((4, 4, 0, 1), (c.Registered, c.FromNursing, c.FromAuxiliar, c.Closed));
        Assert.Equal((1, 1, 0), (c.Escalated, c.UrgentProtocols, c.Referrals));
        Assert.Equal((1, 0, 1), (c.IndicationsIssued, c.IndicationsRead, c.IndicationsUnresolved));
        Assert.Equal((1, 1, 0), (c.NursingTransfers, c.NursingTransfersReceived, c.MedicalTransfers));
        Assert.Equal(c, result.Value.Total);
        Assert.Equal(c, Assert.Single(result.Value.Months).Counts);
        Assert.Equal(0, yesterday.Total.Registered + yesterday.Total.Closed + yesterday.Total.IndicationsIssued + yesterday.Total.NursingTransfers);
    }

    [Fact]
    public async Task Derivaciones_MuestranElEstadoDelProcesoDeLosProtocolosAbiertosDelAmbito_SinContenido()
    {
        var (direccion, enfermera, residentId) = await SeedCenterAsync();
        var service = BuildService(enfermera.ExternalSubject);
        var unsigned = await RegisterAsync(enfermera, residentId, "Desaturación brusca.");
        Assert.True((await service.ActivateUrgentProtocolAsync(ActivateCommand(enfermera, unsigned, await StartAndSaveAsync(enfermera, unsigned)))).Ok);
        var signed = await RegisterAsync(enfermera, residentId, "Dolor torácico.");
        var revision = (await service.ActivateUrgentProtocolAsync(ActivateCommand(enfermera, signed, await StartAndSaveAsync(enfermera, signed)))).Value;
        revision = (await service.SignReferralReportAsync(SignCommand(enfermera, signed, revision, Guid.NewGuid()))).Value;
        var called = await service.RecordFamilyCallAttemptAsync(CallCommand(enfermera, signed, revision));
        Assert.True(called.Ok, called.Error?.Message);
        var withoutProtocol = await RegisterAsync(enfermera, residentId, "Tos productiva.");
        var (otherDirection, otherNurse, otherResident) = await SeedCenterAsync();
        var foreign = await RegisterAsync(otherNurse, otherResident, "Disnea.");
        Assert.True((await BuildService(otherNurse.ExternalSubject).ActivateUrgentProtocolAsync(
            ActivateCommand(otherNurse, foreign, await StartAndSaveAsync(otherNurse, foreign)))).Ok);

        var result = await BuildDireccion(direccion.ExternalSubject).ListReferralsAsync(Query(direccion));
        var asNurse = await BuildDireccion(enfermera.ExternalSubject).ListReferralsAsync(Query(enfermera));
        var foreignList = (await BuildDireccion(otherDirection.ExternalSubject).ListReferralsAsync(Query(otherDirection))).Value!;

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal([signed, unsigned], result.Value!.Select(r => r.EventId));   // el más reciente primero
        var withReport = result.Value![0];
        Assert.True(withReport.ReportSigned);
        Assert.Equal((SystemProfile.Enfermeria, SystemProfile.Enfermeria, 1), (withReport.ProtocolProfile, withReport.ReportProfile, withReport.FamilyCallAttempts));
        Assert.NotNull(withReport.LastCallAt);
        var withoutReport = result.Value[1];
        Assert.Equal((false, null, 0, null), (withoutReport.ReportSigned, withoutReport.ReportProfile, withoutReport.FamilyCallAttempts, withoutReport.LastCallAt));
        Assert.DoesNotContain(result.Value, r => r.EventId == withoutProtocol);
        Assert.Equal(foreign, Assert.Single(foreignList).EventId);
        Assert.Equal(ApplicationFailureCode.AccessDenied, asNurse.Error!.Code);
        // Garantía estructural: nada de texto clínico, del contacto ni del contenido del informe (solo nombres de residente y unidad).
        Assert.Equal(["ResidentDisplayName", "UnitName"], typeof(SupervisionReferral).GetProperties()
            .Where(p => p.PropertyType == typeof(string)).Select(p => p.Name).Order());
    }

    [Fact]
    public async Task Indicadores_SeguimientosConLaFechaVencidaEnElPeriodo_SiguenElPlanVigenteYElCierre()
    {
        var (direccion, enfermera, residentId) = await SeedCenterAsync();
        var service = BuildService(enfermera.ExternalSubject);
        var today = DateOnly.FromDateTime(DateTime.Today);
        async Task<(Guid EventId, int Revision)> FollowUpAsync(string text, DateOnly due)
        {
            var id = await RegisterAsync(enfermera, residentId, text);
            var revision = await StartAndSaveAsync(enfermera, id);
            return (id, (await service.StartFollowUpAsync(StartFollowUpCommand(enfermera, id, revision, due))).Value);
        }

        var overdue = await FollowUpAsync("Seguimiento vencido.", today.AddDays(-1));
        var notDue = await FollowUpAsync("Seguimiento con plazo.", today.AddDays(7));
        var rescheduled = await FollowUpAsync("Seguimiento reprogramado hoy.", today.AddDays(-1));
        Assert.True((await service.RecordFollowUpActionAsync(new RecordFollowUpActionCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, rescheduled.EventId, rescheduled.Revision, FollowUpActionType.Reprogramacion,
            "Persiste el cuadro.", today.AddDays(7)))).Ok);
        // Escalar a Medicina no cierra el episodio ni el seguimiento (CJ, 2026-10-07): sigue abierto y pendiente.
        var escalated = await FollowUpAsync("Seguimiento escalado a Medicina.", today.AddDays(-1));
        Assert.True((await service.EscalateClinicalEventAsync(EscalateCommand(enfermera, escalated.EventId, escalated.Revision))).Ok);
        var closed = await FollowUpAsync("Seguimiento cerrado hoy.", today.AddDays(-1));
        Assert.True((await service.CloseClinicalEventAsync(CloseCommand(enfermera, closed.EventId, closed.Revision, Guid.NewGuid()))).Ok);
        var dir = BuildDireccion(direccion.ExternalSubject);

        var inToday = (await dir.ReadIndicatorsAsync(new ReadSupervisionIndicatorsQuery(direccion.ProfileScopeId, direccion.CenterId, today, today))).Value!.Total;
        var inTomorrow = (await dir.ReadIndicatorsAsync(new ReadSupervisionIndicatorsQuery(
            direccion.ProfileScopeId, direccion.CenterId, today.AddDays(1), today.AddDays(1)))).Value!.Total;
        var before = (await dir.ReadIndicatorsAsync(new ReadSupervisionIndicatorsQuery(
            direccion.ProfileScopeId, direccion.CenterId, today.AddDays(-5), today.AddDays(-3)))).Value!.Total;

        // Hoy: el vencido, el escalado, el reprogramado hoy (empezó el día con el plan vencido) y el cerrado hoy (ese día seguía vencido); el de plazo, no.
        Assert.Equal((5, 4), (inToday.FollowUpsOpen, inToday.FollowUpsOverdue));
        // Mañana: ya no está abierto el cerrado; el reprogramado tiene plazo; siguen vencidos el primero y el escalado.
        Assert.Equal((4, 2), (inTomorrow.FollowUpsOpen, inTomorrow.FollowUpsOverdue));
        Assert.Equal((0, 0), (before.FollowUpsOpen, before.FollowUpsOverdue));
        _ = notDue;
    }

    [Fact]
    public async Task Derivaciones_LasDeEpisodiosCerradosSoloSalenSiSePideElPeriodo()
    {
        var (direccion, enfermera, residentId) = await SeedCenterAsync();
        var service = BuildService(enfermera.ExternalSubject);
        var open = await RegisterAsync(enfermera, residentId, "Desaturación brusca.");
        Assert.True((await service.ActivateUrgentProtocolAsync(ActivateCommand(enfermera, open, await StartAndSaveAsync(enfermera, open)))).Ok);
        var closed = await RegisterAsync(enfermera, residentId, "Dolor torácico.");
        var revision = (await service.ActivateUrgentProtocolAsync(ActivateCommand(enfermera, closed, await StartAndSaveAsync(enfermera, closed)))).Value;
        revision = (await service.SignReferralReportAsync(SignCommand(enfermera, closed, revision, Guid.NewGuid()))).Value;
        revision = (await service.RecordFamilyCallAttemptAsync(CallCommand(enfermera, closed, revision))).Value;
        var closing = await service.CloseClinicalEventAsync(CloseCommand(enfermera, closed, revision, Guid.NewGuid(),
            FamilyCommunicationDecision.Preparar, FamilyCommunicationType.Relevante, "Ha sido trasladado a Urgencias."));
        Assert.True(closing.Ok, closing.Error?.Message);
        var dir = BuildDireccion(direccion.ExternalSubject);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var onlyOpen = (await dir.ListReferralsAsync(Query(direccion))).Value!;
        var withClosed = (await dir.ListReferralsAsync(Query(direccion), today, today)).Value!;
        var otherPeriod = (await dir.ListReferralsAsync(Query(direccion), today.AddDays(-10), today.AddDays(-5))).Value!;
        var half = await dir.ListReferralsAsync(Query(direccion), today, null);
        var reversed = await dir.ListReferralsAsync(Query(direccion), today, today.AddDays(-1));

        Assert.Equal([open], onlyOpen.Select(r => r.EventId));
        Assert.Equal([closed, open], withClosed.Select(r => r.EventId));   // el más reciente primero
        Assert.True(withClosed[0].Closed);
        Assert.NotNull(withClosed[0].ClosedAt);
        Assert.False(withClosed[1].Closed);
        Assert.Equal([open], otherPeriod.Select(r => r.EventId));
        Assert.Equal(ApplicationFailureCode.InvalidInput, half.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, reversed.Error!.Code);
    }

    [Fact]
    public async Task Calidad_MideLosHitosDelProceso_PorUnidad_SinSalirseDelAmbito()
    {
        var (direccion, enfermera, residentId) = await SeedCenterAsync();
        var medica = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, direccion.CenterId, direccion.UnitId);
        var service = BuildService(enfermera.ExternalSubject);
        // Escalado, valorado por Medicina y con una indicación sin leer: valoración médica hecha; lectura y realización, pendientes.
        var escalated = await RegisterAsync(enfermera, residentId, "Disnea progresiva.");
        var revision = await StartAndSaveAsync(enfermera, escalated);
        Assert.True((await service.EscalateClinicalEventAsync(EscalateCommand(enfermera, escalated, revision))).Ok);
        revision = await MedicinaApplicationServiceTests.StartAndSaveMedicalAsync(medica, escalated);
        Assert.True((await MedicinaApplicationServiceTests.BuildMedicina(medica.ExternalSubject).RegisterMedicalIndicationAsync(
            MedicinaApplicationServiceTests.Indication(medica, escalated, revision))).Ok);
        // Protocolo urgente con informe firmado y una llamada: informe y llamada hechos a los pocos segundos.
        var urgent = await RegisterAsync(enfermera, residentId, "Desaturación brusca.");
        revision = (await service.ActivateUrgentProtocolAsync(ActivateCommand(enfermera, urgent, await StartAndSaveAsync(enfermera, urgent)))).Value;
        revision = (await service.SignReferralReportAsync(SignCommand(enfermera, urgent, revision, Guid.NewGuid()))).Value;
        Assert.True((await service.RecordFamilyCallAttemptAsync(CallCommand(enfermera, urgent, revision))).Ok);
        // Un protocolo sin informe todavía: pendiente (en plazo, de momento).
        var unsigned = await RegisterAsync(enfermera, residentId, "Dolor torácico.");
        Assert.True((await service.ActivateUrgentProtocolAsync(ActivateCommand(enfermera, unsigned, await StartAndSaveAsync(enfermera, unsigned)))).Ok);
        // Otro centro: no cuenta.
        var (_, otraEnfermera, otroResidente) = await SeedCenterAsync();
        var foreign = await RegisterAsync(otraEnfermera, otroResidente, "Disnea.");
        Assert.True((await BuildService(otraEnfermera.ExternalSubject).ActivateUrgentProtocolAsync(
            ActivateCommand(otraEnfermera, foreign, await StartAndSaveAsync(otraEnfermera, foreign)))).Ok);
        var dir = BuildDireccion(direccion.ExternalSubject);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var result = await dir.ReadProcessQualityAsync(new ReadSupervisionIndicatorsQuery(direccion.ProfileScopeId, direccion.CenterId, today, today));
        var asNurse = await BuildDireccion(enfermera.ExternalSubject).ReadProcessQualityAsync(
            new ReadSupervisionIndicatorsQuery(enfermera.ProfileScopeId, enfermera.CenterId, today, today));
        var reversed = await dir.ReadProcessQualityAsync(new ReadSupervisionIndicatorsQuery(direccion.ProfileScopeId, direccion.CenterId, today, today.AddDays(-1)));

        Assert.True(result.Ok, result.Error?.Message);
        var total = result.Value!.Total;
        // Los hitos empezaron hace segundos: todos dentro de plazo, ninguno vencido ni a punto de vencer.
        Assert.Equal(new MilestoneCount(1, 0, 0), total[ProcessMilestone.ValoracionMedica]);
        Assert.Equal(new MilestoneCount(2, 0, 0), total[ProcessMilestone.InformeDerivacion]);
        Assert.Equal(new MilestoneCount(2, 0, 0), total[ProcessMilestone.LlamadaFamilia]);
        Assert.Equal(new MilestoneCount(1, 0, 0), total[ProcessMilestone.LecturaIndicacion]);
        Assert.Equal(new MilestoneCount(1, 0, 0), total[ProcessMilestone.RealizacionIndicacion]);
        Assert.Equal(new MilestoneCount(0, 0, 0), total[ProcessMilestone.RecepcionTransferencia]);
        Assert.Empty(result.Value.Exceptions);
        var unit = Assert.Single(result.Value.Units);
        Assert.Equal(direccion.UnitId, unit.UnitId);
        Assert.Equal(total[ProcessMilestone.InformeDerivacion], unit.Counts[ProcessMilestone.InformeDerivacion]);
        Assert.Equal(ApplicationFailureCode.AccessDenied, asNurse.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, reversed.Error!.Code);

        // Los mismos hitos vistos desde la bandeja de Enfermería y de Medicina (avisos al equipo responsable): cada uno ve los de su ámbito.
        var inbox = new SqlChangeInboxDirectory(TestDatabase.ConnectionFactory);
        var from = DateTime.UtcNow.AddDays(-1);
        var to = DateTime.UtcNow.AddMinutes(5);
        var byNursing = await inbox.ListMilestoneFactsAsync(enfermera.ProfileScopeId, enfermera.CenterId, from, to);
        var byMedicine = await inbox.ListMilestoneFactsAsync(medica.ProfileScopeId, medica.CenterId, from, to);
        var foreignScope = await inbox.ListMilestoneFactsAsync(enfermera.ProfileScopeId, CenterId.From(Guid.NewGuid()), from, to);

        Assert.Equal(2, byNursing.Count(f => f.Milestone == ProcessMilestone.InformeDerivacion && f.Responsible == SystemProfile.Enfermeria));
        Assert.Equal(1, byNursing.Count(f => f.Milestone == ProcessMilestone.ValoracionMedica && f.Responsible == SystemProfile.Medicina));
        // Medicina solo ve los eventos escalados (o suyos): la valoración médica y las indicaciones, no los protocolos de Enfermería.
        Assert.Equal(1, byMedicine.Count(f => f.Milestone == ProcessMilestone.ValoracionMedica));
        Assert.Equal(1, byMedicine.Count(f => f.Milestone == ProcessMilestone.LecturaIndicacion));
        Assert.DoesNotContain(byMedicine, f => f.Milestone == ProcessMilestone.InformeDerivacion);
        Assert.Empty(foreignScope);
    }

    [Fact]
    public async Task Indicadores_OtrosPerfilesNoEntran_YUnPeriodoImposibleSeRechaza()
    {
        var (direccion, enfermera, _) = await SeedCenterAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var asNurse = await BuildDireccion(enfermera.ExternalSubject).ReadIndicatorsAsync(
            new ReadSupervisionIndicatorsQuery(enfermera.ProfileScopeId, enfermera.CenterId, today, today));
        var reversed = await BuildDireccion(direccion.ExternalSubject).ReadIndicatorsAsync(
            new ReadSupervisionIndicatorsQuery(direccion.ProfileScopeId, direccion.CenterId, today, today.AddDays(-1)));
        var tooLong = await BuildDireccion(direccion.ExternalSubject).ReadIndicatorsAsync(
            new ReadSupervisionIndicatorsQuery(direccion.ProfileScopeId, direccion.CenterId, today.AddDays(-366), today));

        Assert.Equal(ApplicationFailureCode.AccessDenied, asNurse.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, reversed.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, tooLong.Error!.Code);
        // Garantía estructural: los hechos y los indicadores no llevan texto (salvo nombres de centro y unidad), ni residente ni cuenta.
        var types = new[]
        {
            typeof(IndicatorEpisodeFact), typeof(IndicatorClosureFact), typeof(IndicatorIndicationFact), typeof(IndicatorTransferFact),
            typeof(SupervisionIndicatorCounts), typeof(SupervisionUnitIndicators), typeof(SupervisionMonthIndicators), typeof(SupervisionIndicators),
        };
        var properties = types.SelectMany(t => t.GetProperties()).ToList();
        Assert.Equal(["CenterName", "UnitName"], properties.Where(p => p.PropertyType == typeof(string)).Select(p => p.Name).Order());
        Assert.DoesNotContain(properties, p => p.PropertyType == typeof(ResidentId) || p.PropertyType == typeof(Guid));
    }

    [Fact]
    public async Task Ambito_ListaSusUnidadesYPermisosVigentes()
    {
        var direccion = await SeedFixture.CreateProfileAsync(SystemProfile.DireccionClinica, ["CLINICAL_DETAIL_READ"]);

        var scope = (await BuildDireccion(direccion.ExternalSubject).ReadScopeAsync(Query(direccion))).Value!;

        Assert.Equal(direccion.UnitId, Assert.Single(scope.Units).Id);
        Assert.Equal(["CLINICAL_DETAIL_READ"], scope.Permissions);
        Assert.False(scope.RestrictedToResidents);
    }

    [Fact]
    public async Task Residentes_SonLosDelAmbito_YRespetanLaRestriccionPorResidente()
    {
        var direccion = await SeedFixture.CreateProfileAsync(SystemProfile.DireccionClinica);
        var admin = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, direccion.CenterId, direccion.UnitId);
        var asignado = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Desplegable Asignado");
        var otro = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Desplegable Otro");
        var (otraDireccion, _, _) = await SeedCenterAsync();
        var dir = BuildDireccion(direccion.ExternalSubject);

        var todos = (await dir.ListResidentsAsync(Query(direccion))).Value!;
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.ambitos_perfil_residente (id, ambito_perfil_id, centro_id, residente_id, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @ProfileScopeId, @CenterId, @ResidentId, SYSUTCDATETIME(), @AccountId)
                """, new
            {
                direccion.ProfileScopeId, CenterId = direccion.CenterId.Value, ResidentId = asignado.Value,
                AccountId = admin.AccountId.Value,
            });
        }
        var restringidos = (await dir.ListResidentsAsync(Query(direccion))).Value!;
        var ajenos = await BuildDireccion(otraDireccion.ExternalSubject).ListResidentsAsync(Query(direccion));

        Assert.Equal(new[] { asignado.Value, otro.Value }.Order(), todos.Select(r => r.ResidentId.Value).Order());
        Assert.Equal("Residente Desplegable Asignado", Assert.Single(restringidos).DisplayName);
        Assert.False(ajenos.Ok);
    }

    [Fact]
    public async Task Residentes_OtrosPerfilesNoEntran_YDireccionSigueSinLaFichaDeEnfermeriaNiMedicina()
    {
        var (direccion, enfermera, residentId) = await SeedCenterAsync();

        var asNurse = await BuildDireccion(enfermera.ExternalSubject).ListResidentsAsync(Query(enfermera));
        // El listado admite ya el perfil Dirección, pero las puertas de Enfermería y Medicina (ficha, línea temporal y basal)
        // siguen exigiendo su propio perfil: la lectura clínica de Dirección es otro bloque.
        var enfermeria = BuildService(direccion.ExternalSubject);
        var lista = await enfermeria.ListScopeResidentsAsync(
            new ListScopeResidentsCommand(direccion.ProfileScopeId, direccion.CenterId, SystemProfile.DireccionClinica));
        var ficha = await enfermeria.FindScopeResidentAsync(
            new FindScopeResidentCommand(direccion.ProfileScopeId, direccion.CenterId, residentId, SystemProfile.DireccionClinica));

        Assert.Equal(ApplicationFailureCode.AccessDenied, asNurse.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, lista.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, ficha.Error!.Code);
    }

    [Fact]
    public async Task Ambito_ConSuUnicaAsignacionRevocada_SigueRestringido()
    {
        var direccion = await SeedFixture.CreateProfileAsync(SystemProfile.DireccionClinica);
        var admin = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, direccion.CenterId, direccion.UnitId);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Asignación Revocada");
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.ambitos_perfil_residente
                    (id, ambito_perfil_id, centro_id, residente_id, concedido_en, concedido_por_cuenta_id, revocado_en, revocado_por_cuenta_id)
                VALUES (NEWID(), @ProfileScopeId, @CenterId, @ResidentId, SYSUTCDATETIME(), @AccountId, SYSUTCDATETIME(), @AccountId)
                """, new
            {
                direccion.ProfileScopeId, CenterId = direccion.CenterId.Value, ResidentId = residentId.Value,
                AccountId = admin.AccountId.Value,
            });
        }

        var scope = (await BuildDireccion(direccion.ExternalSubject).ReadScopeAsync(Query(direccion))).Value!;

        // Como la evidencia de autorización: revocar la última asignación no amplía el ámbito a toda la unidad.
        Assert.True(scope.RestrictedToResidents);
    }
}

file sealed class FixedDireccionSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
