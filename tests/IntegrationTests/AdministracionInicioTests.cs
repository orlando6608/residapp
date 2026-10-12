using Dapper;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Families;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;
using static ResidApp.IntegrationTests.PortalFamiliarTests;

namespace ResidApp.IntegrationTests;

/// <summary>ADM-01, el inicio de Administración: las cifras de lo que hay que atender (residentes sin habitación, autorizaciones de familiares, solicitudes
/// de cita y citas). Solo cifras, de las unidades del ámbito (residentes y autorizaciones) o del centro (citas). Cada prueba crea su propio centro. Las
/// lecturas de eventos_auditoria llevan NOLOCK (lección de la suite).</summary>
public class AdministracionInicioTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private static readonly AppointmentModalitySetting[] AllModalities =
        [.. Enum.GetValues<AppointmentTeam>().SelectMany(team => Enum.GetValues<AppointmentModality>().Select(m => new AppointmentModalitySetting(team, m)))];

    private static AccountAdministrationAccess AccessOf(SeededProfile profile) => new(profile.ProfileScopeId, profile.AccountId, profile.CenterId);

    private static AdministrationOverviewPeriod NowPeriod() => AdministrationOverviewApplicationService.PeriodAt(DateTimeOffset.UtcNow, TimeZoneInfo.Local);

    private static Task<AdministrationOverview> OverviewAsync(SeededProfile admin, AdministrationOverviewPeriod? period = null) =>
        new SqlAdministrationOverview(TestDatabase.ConnectionFactory).ReadAsync(AccessOf(admin), period ?? NowPeriod());

    private static async Task ActivateAsync(SeededProfile admin, ResidentId residentId, Guid linkId, int expected, DateOnly? validUntil)
    {
        var result = await Build(admin.ExternalSubject).ChangeFamilyAuthorizationAsync(new ChangeFamilyAuthorizationCommand(
            admin.ProfileScopeId, admin.CenterId, residentId, linkId, FamilyAuthorizationChange.Activar, validUntil, null, expected));
        Assert.True(result.Ok, result.Error?.Message);
    }

    /// <summary>Una Activa que ya ha vencido solo puede venir del paso del tiempo: se inserta directamente (la aplicación no deja activar con una fecha pasada).</summary>
    private static async Task InsertExpiredAsync(SeededProfile admin, Guid linkId, int number)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.familiares_autorizaciones_cambios
                (id, centro_id, vinculo_id, numero, estado_codigo, valida_hasta, motivo, registrado_por_cuenta_id, registrado_por_perfil, registrado_en)
            VALUES (NEWID(), @CenterId, @LinkId, @Number, 'ACTIVA', @Yesterday, NULL, @AccountId, 'ADMINISTRACION', SYSUTCDATETIME())
            """, new
        {
            CenterId = admin.CenterId.Value, LinkId = linkId, Number = number, Yesterday = Today.AddDays(-1).ToDateTime(TimeOnly.MinValue),
            AccountId = admin.AccountId.Value,
        });
    }

    [Fact]
    public async Task SinResidentesNiSolicitudes_NoHayNadaPendiente()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);

        var overview = await OverviewAsync(admin);

        Assert.Equal(new AdministrationOverview(0, 0, 0, 0, 0, 0, 0, 0), overview);
        Assert.True(overview.NothingPending);
    }

    [Fact]
    public async Task ResidentesSinHabitacion_YAutorizaciones_SoloLasDelAmbito_MandaElUltimoCambio_YLasFronterasDeLasFechas()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var otherCenter = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var otherUnit = await AddUnitAsync(admin.CenterId);
        var otherUnitAdmin = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, admin.CenterId, otherUnit);
        var room = (await BuildEstructura(admin.ExternalSubject).CreateRoomAsync(
            new CreateRoomCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), admin.UnitId, "Habitación inicio"))).Value;
        await new SqlResidentRepository(TestDatabase.ConnectionFactory).CreateWithInitialLocationAsync(new CreateResidentInput(
            admin.AccountId, SystemProfile.Administracion, admin.CenterId, admin.UnitId, "Residente con habitación", new DateOnly(1940, 5, 20),
            DocumentedSexCode.NoConsta, null, null, null, room, null, Guid.NewGuid()));
        var resident = await CreateResidentAsync(admin, "Residente sin habitación");
        var outsider = await CreateResidentAsync(admin, "Residente de otra unidad", otherUnit);
        await AddFamilyAsync(otherUnitAdmin, outsider, "Familiar ajeno");

        // Ocho vínculos del mismo residente, cada uno en un estado distinto de la última autorización.
        var noChange = await AddFamilyAsync(admin, resident, "Sin cambios");
        var opened = await AddFamilyAsync(admin, resident, "Solo abierta");
        var active = await AddFamilyAsync(admin, resident, "Activa sin fecha");
        var in30 = await AddFamilyAsync(admin, resident, "Caduca en 30");
        var in31 = await AddFamilyAsync(admin, resident, "Caduca en 31");
        var today = await AddFamilyAsync(admin, resident, "Caduca hoy");
        var expired = await AddFamilyAsync(admin, resident, "Ya caducada");
        var suspended = await AddFamilyAsync(admin, resident, "Suspendida");
        foreach (var link in new[] { opened, active, in30, in31, today, expired, suspended })
        {
            await ChangeAsync(admin, resident, link, FamilyAuthorizationChange.Abrir, 0);
        }

        await ActivateAsync(admin, resident, active, 1, null);
        await ActivateAsync(admin, resident, in30, 1, Today.AddDays(AdministrationOverviewRules.ExpiringSoonDays));
        await ActivateAsync(admin, resident, in31, 1, Today.AddDays(AdministrationOverviewRules.ExpiringSoonDays + 1));
        await ActivateAsync(admin, resident, today, 1, Today);
        await InsertExpiredAsync(admin, expired, 2);
        await ActivateAsync(admin, resident, suspended, 1, null);
        await ChangeAsync(admin, resident, suspended, FamilyAuthorizationChange.Suspender, 2, "Petición del familiar.");

        var overview = await OverviewAsync(admin);
        var elsewhere = await OverviewAsync(otherUnitAdmin);
        var empty = await OverviewAsync(otherCenter);

        // Sin cambios y solo abierta están pendientes; el que ya pasó de Pendiente a Activa o Suspendida no (manda el último cambio).
        Assert.Equal(1, overview.ResidentsWithoutRoom);
        Assert.Equal(2, overview.AuthorizationsPending);
        // Caduca en 30 días y hoy cuentan como «pronto»; la de 31 días, no; la vencida ayer es caducada y no «pronto».
        Assert.Equal(2, overview.AuthorizationsExpiringSoon);
        Assert.Equal(1, overview.AuthorizationsExpired);
        Assert.False(overview.NothingPending);
        // Otra unidad del mismo centro y otro centro no se mezclan con lo anterior.
        Assert.Equal(new AdministrationOverview(1, 1, 0, 0, 0, 0, 0, 0), elsewhere);
        Assert.Equal(new AdministrationOverview(0, 0, 0, 0, 0, 0, 0, 0), empty);
    }

    [Fact]
    public async Task UnFamiliarDesvinculado_NoCuentaComoPendiente()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await CreateResidentAsync(admin, "Residente desvinculo inicio");
        var link = await AddFamilyAsync(admin, resident, "Familiar que se va");
        Assert.Equal(1, (await OverviewAsync(admin)).AuthorizationsPending);

        var unlinked = await Build(admin.ExternalSubject).UnlinkFamilyMemberAsync(
            new UnlinkFamilyMemberCommand(admin.ProfileScopeId, admin.CenterId, resident, link, "Ya no es familiar del residente."));
        Assert.True(unlinked.Ok, unlinked.Error?.Message);

        Assert.Equal(0, (await OverviewAsync(admin)).AuthorizationsPending);
    }

    private static async Task<IReadOnlyList<ResidentAttention>> AttentionAsync(SeededProfile admin) =>
        await new SqlAdministracionResidentDirectory(TestDatabase.ConnectionFactory).ListAttentionAsync(admin.ProfileScopeId, admin.CenterId, Today);

    [Fact]
    public async Task ListaDeResidentes_FiltraPorMotivoDeAtencion_ConLaMismaReglaQueElInicio_SoloEnElAmbito()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var otherUnit = await AddUnitAsync(admin.CenterId);
        var otherUnitAdmin = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, admin.CenterId, otherUnit);
        var room = (await BuildEstructura(admin.ExternalSubject).CreateRoomAsync(
            new CreateRoomCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), admin.UnitId, "Habitación atención"))).Value;
        async Task<ResidentId> WithRoomAsync(string name) => (await new SqlResidentRepository(TestDatabase.ConnectionFactory).CreateWithInitialLocationAsync(
            new CreateResidentInput(admin.AccountId, SystemProfile.Administracion, admin.CenterId, admin.UnitId, name, new DateOnly(1940, 5, 20),
                DocumentedSexCode.NoConsta, null, null, null, room, null, Guid.NewGuid()))).ResidentId;
        var noRoomPending = await CreateResidentAsync(admin, "Sin habitación y pendiente");
        await AddFamilyAsync(admin, noRoomPending, "Familiar pendiente");
        var soon = await WithRoomAsync("Caduca pronto");
        var soonLink = await AddFamilyAsync(admin, soon, "Familiar que caduca");
        await ChangeAsync(admin, soon, soonLink, FamilyAuthorizationChange.Abrir, 0);
        await ActivateAsync(admin, soon, soonLink, 1, Today.AddDays(5));
        var endsToday = await WithRoomAsync("Caduca hoy");
        var endsTodayLink = await AddFamilyAsync(admin, endsToday, "Familiar que caduca hoy");
        await ChangeAsync(admin, endsToday, endsTodayLink, FamilyAuthorizationChange.Abrir, 0);
        await ActivateAsync(admin, endsToday, endsTodayLink, 1, Today);
        var expired = await WithRoomAsync("Caducada");
        var expiredLink = await AddFamilyAsync(admin, expired, "Familiar caducada");
        await ChangeAsync(admin, expired, expiredLink, FamilyAuthorizationChange.Abrir, 0);
        await InsertExpiredAsync(admin, expiredLink, 2);
        var fine = await WithRoomAsync("Todo en orden");
        var fineLink = await AddFamilyAsync(admin, fine, "Familiar al día");
        await ChangeAsync(admin, fine, fineLink, FamilyAuthorizationChange.Abrir, 0);
        await ActivateAsync(admin, fine, fineLink, 1, null);
        var noFamily = await WithRoomAsync("Sin familiares");
        var outsider = await CreateResidentAsync(admin, "De otra unidad", otherUnit);
        await AddFamilyAsync(otherUnitAdmin, outsider, "Familiar ajeno");

        var attention = await AttentionAsync(admin);
        var mine = attention.ToDictionary(a => a.ResidentId);
        var elsewhere = await AttentionAsync(otherUnitAdmin);

        Assert.Equal(6, attention.Count);
        // El día de «válida hasta» la autorización aún vale: está a punto de caducar, no caducada.
        Assert.Equal((false, false, false, true), (mine[endsToday].WithoutRoom, mine[endsToday].AuthorizationPending, mine[endsToday].AuthorizationExpired, mine[endsToday].AuthorizationExpiringSoon));
        Assert.False(mine.ContainsKey(outsider));
        Assert.Equal((true, true, false, false), (mine[noRoomPending].WithoutRoom, mine[noRoomPending].AuthorizationPending, mine[noRoomPending].AuthorizationExpired, mine[noRoomPending].AuthorizationExpiringSoon));
        Assert.Equal((false, false, false, true), (mine[soon].WithoutRoom, mine[soon].AuthorizationPending, mine[soon].AuthorizationExpired, mine[soon].AuthorizationExpiringSoon));
        Assert.Equal((false, false, true, false), (mine[expired].WithoutRoom, mine[expired].AuthorizationPending, mine[expired].AuthorizationExpired, mine[expired].AuthorizationExpiringSoon));
        Assert.Equal((false, false, false, false), (mine[fine].WithoutRoom, mine[fine].AuthorizationPending, mine[fine].AuthorizationExpired, mine[fine].AuthorizationExpiringSoon));
        Assert.Equal((false, false, false, false), (mine[noFamily].WithoutRoom, mine[noFamily].AuthorizationPending, mine[noFamily].AuthorizationExpired, mine[noFamily].AuthorizationExpiringSoon));
        Assert.Equal([noRoomPending], attention.Where(a => a.Matches(ResidentAttentionKind.SinHabitacion)).Select(a => a.ResidentId));
        Assert.Equal([noRoomPending], attention.Where(a => a.Matches(ResidentAttentionKind.AutorizacionPendiente)).Select(a => a.ResidentId));
        Assert.Equal([expired], attention.Where(a => a.Matches(ResidentAttentionKind.AutorizacionCaducada)).Select(a => a.ResidentId));
        Assert.Equal(new[] { soon, endsToday }.OrderBy(id => id.Value), attention.Where(a => a.Matches(ResidentAttentionKind.AutorizacionPorCaducar)).Select(a => a.ResidentId).OrderBy(id => id.Value));
        // La lista y el inicio cuentan lo mismo: cada residente señalado suma al menos una unidad del contador correspondiente.
        var overview = await OverviewAsync(admin);
        Assert.Equal(1, overview.ResidentsWithoutRoom);
        Assert.Equal(1, overview.AuthorizationsPending);
        Assert.Equal(1, overview.AuthorizationsExpired);
        Assert.Equal(2, overview.AuthorizationsExpiringSoon);
        Assert.Equal((true, true), (Assert.Single(elsewhere).WithoutRoom, elsewhere[0].AuthorizationPending));
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var denied = await Build(nurse.ExternalSubject).ListResidentAttentionAsync(new AdministracionQuery(nurse.ProfileScopeId, nurse.CenterId), Today);
        Assert.Equal(ApplicationFailureCode.AccessDenied, denied.Error!.Code);
    }

    private static async Task ConfigureAsync(Setup setup)
    {
        var settings = new AppointmentServiceApplicationService(Resolver(setup.Admin.ExternalSubject), new SqlAppointmentServiceRepository(TestDatabase.ConnectionFactory));
        var saved = await settings.SaveSettingsAsync(new SaveAppointmentServiceCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, 0, AppointmentServiceMode.SolicitudPrevia, 1, 30, AllModalities));
        Assert.True(saved.Ok, saved.Error?.Message);
        foreach (var team in Enum.GetValues<AppointmentTeam>())
        {
            for (var day = 1; day <= 7; day++)
            {
                var added = await settings.AddWindowAsync(new AddAppointmentWindowCommand(
                    setup.Admin.ProfileScopeId, setup.Admin.CenterId, team, day, new TimeOnly(8, 0), new TimeOnly(20, 0), 30));
                Assert.True(added.Ok, added.Error?.Message);
            }
        }
    }

    private static AdministrationAccessResolver Resolver(string subject) =>
        new(new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedOverviewSessionIdentityProvider(subject));

    private static FamilyAppointmentRequestsApplicationService Requests(string subject) => new(
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedOverviewSessionIdentityProvider(subject),
        new SqlFamilyAppointmentRequests(TestDatabase.ConnectionFactory));

    private static FamilyAppointmentsApplicationService Bookings(string subject) => new(
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedOverviewSessionIdentityProvider(subject),
        new SqlFamilyAppointments(TestDatabase.ConnectionFactory));

    private static async Task<FamilyAppointmentRequest> SendAsync(string subject, Guid linkId, AppointmentTeam team)
    {
        var query = await QueryAsync(subject);
        var sent = await Requests(subject).CreateAsync(new CreateFamilyAppointmentRequestCommand(
            query.AmbitoPerfilId, query.CentroId, linkId, team, AppointmentModality.Presencial, AppointmentRequestReason.Resultados, null, [],
            AppointmentDayPart.Indiferente, Guid.NewGuid()));
        Assert.True(sent.Ok, sent.Error?.Message);
        return sent.Value!;
    }

    /// <summary>Propone a la solicitud un hueco de dentro de más de dos días y devuelve su hora de inicio.</summary>
    private static async Task<DateTimeOffset> ProposeAsync(Setup setup, Guid requestId)
    {
        var requests = new AppointmentRequestsApplicationService(Resolver(setup.Admin.ExternalSubject), new SqlAppointmentRequestRepository(TestDatabase.ConnectionFactory));
        var slots = await requests.ListProposalSlotsAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId, requestId);
        Assert.True(slots.Ok, slots.Error?.Message);
        var slot = slots.Value!.First(s => s.StartsAt >= DateTimeOffset.UtcNow.AddDays(2));
        var proposed = await requests.ProposeAsync(new ProposeAppointmentCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, requestId, slot.StartsAt, null));
        Assert.True(proposed.Ok, proposed.Error?.Message);
        return slot.StartsAt;
    }

    private static async Task<(string Subject, Guid LinkId)> OtherFamilyAsync(Setup setup, string name)
    {
        var resident = await CreateResidentAsync(setup.Admin, name);
        var linkId = await AddFamilyAsync(setup.Admin, resident, "Familiar " + name);
        var subject = $"portal-{Guid.NewGuid():N}"[..20];
        var grant = await Build(setup.Admin.ExternalSubject).GrantFamilyPortalAccessAsync(
            new GrantFamilyPortalAccessCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, resident, linkId, subject));
        Assert.True(grant.Ok, grant.Error?.Message);
        await ChangeAsync(setup.Admin, resident, linkId, FamilyAuthorizationChange.Abrir, 0);
        await ChangeAsync(setup.Admin, resident, linkId, FamilyAuthorizationChange.Activar, 1);
        return (subject, linkId);
    }

    [Fact]
    public async Task SolicitudesDeCita_YCitas_SeCuentanDelCentro_ConSusFronteras_YLasCanceladasNoCuentan()
    {
        var setup = await SetupAsync("Residente Inicio Citas");
        var (otherSubject, otherLink) = await OtherFamilyAsync(setup, "Residente Inicio Citas Dos");
        var (thirdSubject, thirdLink) = await OtherFamilyAsync(setup, "Residente Inicio Citas Tres");
        await ConfigureAsync(setup);
        await SendAsync(setup.Subject, setup.LinkId, AppointmentTeam.Medico);
        var proposedRequest = await SendAsync(setup.Subject, setup.LinkId, AppointmentTeam.Enfermeria);
        var proposedStart = await ProposeAsync(setup, proposedRequest.Id);
        var acceptedRequest = await SendAsync(otherSubject, otherLink, AppointmentTeam.Medico);
        var citaStart = await ProposeAsync(setup, acceptedRequest.Id);
        var accepted = await Requests(otherSubject).AcceptAsync(await QueryAsync(otherSubject), otherLink, acceptedRequest.Id);
        Assert.True(accepted.Ok, accepted.Error?.Message);
        // Una solicitud cerrada por el centro tampoco cuenta.
        var closedRequest = await SendAsync(thirdSubject, thirdLink, AppointmentTeam.Medico);
        var closed = await new AppointmentRequestsApplicationService(Resolver(setup.Admin.ExternalSubject), new SqlAppointmentRequestRepository(TestDatabase.ConnectionFactory))
            .CloseAsync(new CloseAppointmentRequestCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, closedRequest.Id, "Atendido por teléfono."));
        Assert.True(closed.Ok, closed.Error?.Message);

        // La solicitud aceptada ya no está abierta: queda una pendiente y una propuesta esperando; y una cita confirmada.
        var now = DateTimeOffset.UtcNow;
        AdministrationOverviewPeriod Around(DateTimeOffset todayStart, DateTimeOffset tomorrowStart, DateTimeOffset comingEnd, DateTimeOffset? at = null) =>
            new(Today, at ?? now, todayStart, tomorrowStart, comingEnd);
        var inToday = await OverviewAsync(setup.Admin, Around(citaStart.AddHours(-1), citaStart.AddHours(1), citaStart.AddDays(7)));
        var inComing = await OverviewAsync(setup.Admin, Around(citaStart.AddDays(-1), citaStart.AddHours(-1), citaStart.AddDays(7)));
        // Fronteras: el inicio del día cuenta (incluido) y el fin no (excluido).
        var startsToday = await OverviewAsync(setup.Admin, Around(citaStart, citaStart.AddDays(1), citaStart.AddDays(8)));
        var endsToday = await OverviewAsync(setup.Admin, Around(citaStart.AddDays(-1), citaStart, citaStart.AddDays(7)));
        var endsComing = await OverviewAsync(setup.Admin, Around(citaStart.AddDays(-2), citaStart.AddDays(-1), citaStart));
        // Una propuesta cuya hora ya empezó no está esperando a la familia.
        var proposalStarted = await OverviewAsync(setup.Admin, Around(citaStart.AddDays(-1), citaStart.AddDays(1), citaStart.AddDays(8), proposedStart.AddMinutes(1)));
        var cancelled = await Bookings(otherSubject).CancelAsync(await QueryAsync(otherSubject), otherLink, accepted.Value!.AppointmentId!.Value);
        Assert.True(cancelled.Ok, cancelled.Error?.Message);
        var afterCancel = await OverviewAsync(setup.Admin, Around(citaStart.AddHours(-1), citaStart.AddHours(1), citaStart.AddDays(7)));
        var otherCenter = await OverviewAsync(await SeedFixture.CreateProfileAsync(SystemProfile.Administracion), Around(citaStart.AddHours(-1), citaStart.AddHours(1), citaStart.AddDays(7)));

        Assert.Equal((1, 1, 1, 0), (inToday.AppointmentRequestsPending, inToday.AppointmentRequestsAwaitingFamily, inToday.AppointmentsToday, inToday.AppointmentsComing));
        Assert.Equal((0, 1), (inComing.AppointmentsToday, inComing.AppointmentsComing));
        Assert.Equal((1, 0), (startsToday.AppointmentsToday, startsToday.AppointmentsComing));
        Assert.Equal((0, 1), (endsToday.AppointmentsToday, endsToday.AppointmentsComing));
        Assert.Equal((0, 0), (endsComing.AppointmentsToday, endsComing.AppointmentsComing));
        Assert.Equal(0, proposalStarted.AppointmentRequestsAwaitingFamily);
        Assert.Equal(1, proposalStarted.AppointmentRequestsPending);
        Assert.Equal((0, 0), (afterCancel.AppointmentsToday, afterCancel.AppointmentsComing));
        Assert.Equal(new AdministrationOverview(0, 0, 0, 0, 0, 0, 0, 0), otherCenter);
    }

    [Fact]
    public async Task SoloAdministracionDelCentro_LeeElInicio()
    {
        var setup = await SetupAsync("Residente Inicio Acceso");
        var service = new AdministrationOverviewApplicationService(
            Resolver(setup.Admin.ExternalSubject), new SqlAdministrationOverview(TestDatabase.ConnectionFactory));
        var nurseService = new AdministrationOverviewApplicationService(
            Resolver(setup.Nurse.ExternalSubject), new SqlAdministrationOverview(TestDatabase.ConnectionFactory));

        var admin = await service.ReadAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId);
        var nurse = await nurseService.ReadAsync(setup.Nurse.ProfileScopeId, setup.Nurse.CenterId);
        var wrongScope = await service.ReadAsync(setup.Nurse.ProfileScopeId, setup.Admin.CenterId);
        // Aunque la capa de aplicación se saltara, la propia consulta exige ser Administración del centro.
        await Assert.ThrowsAsync<AccessDeniedException>(() => new SqlAdministrationOverview(TestDatabase.ConnectionFactory).ReadAsync(AccessOf(setup.Nurse), NowPeriod()));

        Assert.True(admin.Ok, admin.Error?.Message);
        Assert.Equal((1, 0), (admin.Value!.ResidentsWithoutRoom, admin.Value.AuthorizationsPending));
        Assert.Equal(ApplicationFailureCode.AccessDenied, nurse.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, wrongScope.Error!.Code);
    }
}

file sealed class FixedOverviewSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
