using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Families;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.PortalFamiliarTests;

namespace ResidApp.IntegrationTests;

/// <summary>Citas en modo solicitud previa (script 0057): la familia solicita, Administración propone y reserva el hueco, y solo la aceptación crea la
/// cita. Cada prueba crea su propio centro. Las lecturas de eventos_auditoria llevan NOLOCK (lección de la suite).</summary>
public class CitasSolicitudesTests
{
    private static readonly AppointmentModalitySetting[] AllModalities =
        [.. Enum.GetValues<AppointmentTeam>().SelectMany(team => Enum.GetValues<AppointmentModality>().Select(m => new AppointmentModalitySetting(team, m)))];

    private static AdministrationAccessResolver Resolver(string subject) =>
        new(new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedSolicitudesSessionIdentityProvider(subject));

    private static AppointmentServiceApplicationService Settings(string subject) =>
        new(Resolver(subject), new SqlAppointmentServiceRepository(TestDatabase.ConnectionFactory));

    private static AppointmentRequestsApplicationService AdminRequests(string subject) =>
        new(Resolver(subject), new SqlAppointmentRequestRepository(TestDatabase.ConnectionFactory));

    private static FamilyAppointmentRequestsApplicationService Requests(string subject) => new(
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedSolicitudesSessionIdentityProvider(subject),
        new SqlFamilyAppointmentRequests(TestDatabase.ConnectionFactory));

    private static FamilyAppointmentsApplicationService Bookings(string subject) => new(
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedSolicitudesSessionIdentityProvider(subject),
        new SqlFamilyAppointments(TestDatabase.ConnectionFactory));

    /// <summary>Un centro con el servicio en el modo indicado (solicitud previa por defecto), las modalidades dadas y, para los dos equipos, una franja todos
    /// los días de 08:00 a 20:00 en citas de 30 minutos: hay huecos sea cual sea el día en que se ejecute la prueba.</summary>
    internal static async Task ConfigureAsync(
        Setup setup, AppointmentServiceMode mode = AppointmentServiceMode.SolicitudPrevia, IReadOnlyCollection<AppointmentModalitySetting>? modalities = null)
    {
        var settings = Settings(setup.Admin.ExternalSubject);
        var version = (await settings.ReadAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId)).Value!.Version;
        var saved = await settings.SaveSettingsAsync(new SaveAppointmentServiceCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, version, mode, 1, 30, modalities ?? AllModalities));
        Assert.True(saved.Ok, saved.Error?.Message);
        if (version > 0)
        {
            return;
        }

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

    private static async Task<ApplicationResult<FamilyAppointmentRequest>> SendAsync(
        string subject, Guid linkId, AppointmentTeam team = AppointmentTeam.Medico, AppointmentModality modality = AppointmentModality.Presencial,
        AppointmentRequestReason reason = AppointmentRequestReason.Resultados, string? comment = null, int[]? days = null,
        AppointmentDayPart part = AppointmentDayPart.Indiferente, Guid? operationId = null)
    {
        var query = await QueryAsync(subject);
        return await Requests(subject).CreateAsync(new CreateFamilyAppointmentRequestCommand(
            query.AmbitoPerfilId, query.CentroId, linkId, team, modality, reason, comment, days ?? [], part, operationId ?? Guid.NewGuid()));
    }

    internal static async Task<FamilyAppointmentRequest> SentAsync(string subject, Guid linkId, AppointmentTeam team = AppointmentTeam.Medico)
    {
        var sent = await SendAsync(subject, linkId, team);
        Assert.True(sent.Ok, sent.Error?.Message);
        return sent.Value!;
    }

    private static async Task<ApplicationResult<FamilyAppointmentRequest>> ReadAsync(string subject, Guid linkId, Guid requestId) =>
        await Requests(subject).ReadAsync(await QueryAsync(subject), linkId, requestId);

    internal static async Task<IReadOnlyList<AppointmentSlot>> ProposalSlotsAsync(Setup setup, Guid requestId, DateTimeOffset? from = null)
    {
        var slots = await AdminRequests(setup.Admin.ExternalSubject).ListProposalSlotsAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId, requestId);
        Assert.True(slots.Ok, slots.Error?.Message);
        return from is { } start ? slots.Value!.Where(s => s.StartsAt >= start).ToList() : slots.Value!;
    }

    internal static Task<ApplicationResult<bool>> ProposeAsync(Setup setup, Guid requestId, AppointmentSlot slot, string? professional = null) =>
        AdminRequests(setup.Admin.ExternalSubject).ProposeAsync(new ProposeAppointmentCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, requestId, slot.StartsAt, professional));

    internal static async Task<AdministrationAppointmentRequest> AdminViewAsync(Setup setup, Guid requestId)
    {
        var read = await AdminRequests(setup.Admin.ExternalSubject).ReadAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId, requestId);
        Assert.True(read.Ok, read.Error?.Message);
        return read.Value!;
    }

    private static async Task<ApplicationResult<FamilyAppointmentRequest>> AcceptAsync(string subject, Guid linkId, Guid requestId) =>
        await Requests(subject).AcceptAsync(await QueryAsync(subject), linkId, requestId);

    private static async Task<ApplicationResult<FamilyAppointmentRequest>> OtherTimeAsync(string subject, Guid linkId, Guid requestId) =>
        await Requests(subject).RequestAnotherTimeAsync(await QueryAsync(subject), linkId, requestId);

    private static async Task<ApplicationResult<bool>> CancelAsync(string subject, Guid linkId, Guid requestId) =>
        await Requests(subject).CancelAsync(await QueryAsync(subject), linkId, requestId);

    /// <summary>Un hueco de dentro de más de dos días: la familia aún puede cancelar la cita (antelación mínima de una hora).</summary>
    internal static async Task<AppointmentSlot> FarSlotAsync(Setup setup, Guid requestId, int skip = 0) =>
        (await ProposalSlotsAsync(setup, requestId, DateTimeOffset.UtcNow.AddDays(2))).Skip(skip).First();

    internal static async Task<(string Subject, Guid LinkId, Guid ResidentId)> OtherResidentFamilyAsync(Setup setup, string name)
    {
        var resident = await AdministracionResidentesTests.CreateResidentAsync(setup.Admin, name);
        var linkId = await AddFamilyAsync(setup.Admin, resident, "Familiar " + name);
        var subject = $"portal-{Guid.NewGuid():N}"[..20];
        var grant = await AdministracionResidentesTests.Build(setup.Admin.ExternalSubject).GrantFamilyPortalAccessAsync(
            new GrantFamilyPortalAccessCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, resident, linkId, subject));
        Assert.True(grant.Ok, grant.Error?.Message);
        await ChangeAsync(setup.Admin, resident, linkId, FamilyAuthorizationChange.Abrir, 0);
        await ChangeAsync(setup.Admin, resident, linkId, FamilyAuthorizationChange.Activar, 1);
        return (subject, linkId, resident.Value);
    }

    internal static Task<int> AuditAsync(Guid requestId, string action) => CountAsync(
        "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE recurso_id = @requestId AND accion_codigo = @action", new { requestId, action });

    internal static Task<int> SavedAsync(Guid requestId, string status) => CountAsync(
        "SELECT COUNT(*) FROM dbo.citas_solicitudes WHERE id = @requestId AND estado_codigo = @status", new { requestId, status });

    [Fact]
    public async Task Solicitar_QuedaPendienteConLoQuePidio_YLaMismaOperacionNoDuplica()
    {
        var setup = await SetupAsync("Residente Solicitud Crear");
        await ConfigureAsync(setup);
        var operationId = Guid.NewGuid();

        var sent = await SendAsync(setup.Subject, setup.LinkId, AppointmentTeam.Medico, AppointmentModality.Telefonica, AppointmentRequestReason.Medicacion,
            "  Quiero hablar del tratamiento nuevo.  ", [5, 1, 3], AppointmentDayPart.Tarde, operationId);
        var repeated = await SendAsync(setup.Subject, setup.LinkId, AppointmentTeam.Medico, AppointmentModality.Telefonica, AppointmentRequestReason.Medicacion,
            "  Quiero hablar del tratamiento nuevo.  ", [5, 1, 3], AppointmentDayPart.Tarde, operationId);
        var reused = await SendAsync(setup.Subject, setup.LinkId, AppointmentTeam.Enfermeria, AppointmentModality.Telefonica, operationId: operationId);

        Assert.True(sent.Ok, sent.Error?.Message);
        var request = sent.Value!;
        Assert.Equal(AppointmentRequestStatus.Pendiente, request.Status);
        Assert.Equal(AppointmentTeam.Medico, request.Team);
        Assert.Equal(AppointmentModality.Telefonica, request.Modality);
        Assert.Equal(AppointmentRequestReason.Medicacion, request.Reason);
        Assert.Equal("Quiero hablar del tratamiento nuevo.", request.Comment);
        Assert.Equal([1, 3, 5], request.Availability.Days);
        Assert.Equal(AppointmentDayPart.Tarde, request.Availability.DayPart);
        Assert.True(request.CanCancel);
        Assert.False(request.CanAccept);
        Assert.False(request.CanRequestAnotherTime);
        Assert.Null(request.ProposedStartsAt);
        Assert.Null(request.AppointmentId);
        var change = Assert.Single(request.Changes);
        Assert.Equal(AppointmentRequestAction.Creada, change.Action);
        Assert.False(change.ByCenter);

        Assert.True(repeated.Ok, repeated.Error?.Message);
        Assert.Equal(request.Id, repeated.Value!.Id);
        Assert.Equal(ApplicationFailureCode.Conflict, reused.Error!.Code);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_solicitudes WHERE residente_id = @id", new { id = setup.ResidentId.Value }));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_solicitudes_cambios WHERE solicitud_id = @id", new { id = request.Id }));
        Assert.Equal(1, await AuditAsync(request.Id, "APPOINTMENT_REQUEST_CREATE"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.citas WHERE residente_id = @id", new { id = setup.ResidentId.Value }));
    }

    [Fact]
    public async Task Solicitar_ExigeLaSolicitudPrevia_LaModalidadDelEquipo_Y_DatosValidos()
    {
        var setup = await SetupAsync("Residente Solicitud Modos");
        await ConfigureAsync(setup, AppointmentServiceMode.ReservaDirecta);
        var direct = await SendAsync(setup.Subject, setup.LinkId);
        await ConfigureAsync(setup, AppointmentServiceMode.Desactivado);
        var disabled = await SendAsync(setup.Subject, setup.LinkId);
        await ConfigureAsync(setup, AppointmentServiceMode.SolicitudPrevia, [new AppointmentModalitySetting(AppointmentTeam.Medico, AppointmentModality.Presencial)]);
        var otherTeam = await SendAsync(setup.Subject, setup.LinkId, AppointmentTeam.Enfermeria, AppointmentModality.Presencial);
        var otherModality = await SendAsync(setup.Subject, setup.LinkId, AppointmentTeam.Medico, AppointmentModality.Telefonica);
        var longComment = await SendAsync(setup.Subject, setup.LinkId, comment: new string('x', 301));
        var badDay = await SendAsync(setup.Subject, setup.LinkId, days: [8]);
        var noOperation = await SendAsync(setup.Subject, setup.LinkId, operationId: Guid.Empty);

        Assert.Equal(ApplicationFailureCode.Conflict, direct.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, disabled.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, otherTeam.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, otherModality.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, longComment.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, badDay.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, noOperation.Error!.Code);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.citas_solicitudes WHERE residente_id = @id", new { id = setup.ResidentId.Value }));
        Assert.True((await SendAsync(setup.Subject, setup.LinkId, AppointmentTeam.Medico, AppointmentModality.Presencial)).Ok);
    }

    [Fact]
    public async Task Solicitar_UnaAbiertaPorEquipo_YNingunaConUnaCitaFuturaDeEseEquipo()
    {
        var setup = await SetupAsync("Residente Solicitud Limite");
        await ConfigureAsync(setup);
        var first = await SentAsync(setup.Subject, setup.LinkId, AppointmentTeam.Medico);

        var second = await SendAsync(setup.Subject, setup.LinkId, AppointmentTeam.Medico);
        var otherTeam = await SendAsync(setup.Subject, setup.LinkId, AppointmentTeam.Enfermeria);
        Assert.Equal(ApplicationFailureCode.Conflict, second.Error!.Code);
        Assert.True(otherTeam.Ok, otherTeam.Error?.Message);

        Assert.True((await CancelAsync(setup.Subject, setup.LinkId, first.Id)).Ok);
        var afterCancel = await SendAsync(setup.Subject, setup.LinkId, AppointmentTeam.Medico);
        Assert.True(afterCancel.Ok, afterCancel.Error?.Message);

        // Con una cita futura confirmada del mismo equipo tampoco (la regla de la reserva directa).
        var other = await OtherResidentFamilyAsync(setup, "Residente Solicitud Limite Dos");
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.citas
                    (id, centro_id, residente_id, vinculo_id, cuenta_id, equipo_codigo, modalidad_codigo, inicio_en, fin_en, estado_codigo, operacion_id, creada_en)
                SELECT NEWID(), @center, @resident, @link, account.id, 'MEDICO', 'PRESENCIAL', DATEADD(DAY, 5, SYSUTCDATETIME()), DATEADD(MINUTE, 30, DATEADD(DAY, 5, SYSUTCDATETIME())),
                       'CONFIRMADA', NEWID(), SYSUTCDATETIME()
                  FROM dbo.cuentas account WHERE account.sujeto_externo = @subject
                """, new { center = setup.Admin.CenterId.Value, resident = other.ResidentId, link = other.LinkId, subject = other.Subject });
        }

        var withAppointment = await SendAsync(other.Subject, other.LinkId, AppointmentTeam.Medico);
        Assert.Equal(ApplicationFailureCode.Conflict, withAppointment.Error!.Code);
    }

    [Fact]
    public async Task Proponer_ReservaElHueco_NoSeOfreceAOtraSolicitud_YPedirOtroHorarioLoLibera()
    {
        var setup = await SetupAsync("Residente Solicitud Propuesta Uno");
        await ConfigureAsync(setup);
        var other = await OtherResidentFamilyAsync(setup, "Residente Solicitud Propuesta Dos");
        var requestA = await SentAsync(setup.Subject, setup.LinkId);
        var requestB = await SentAsync(other.Subject, other.LinkId);
        var slotsA = await ProposalSlotsAsync(setup, requestA.Id);
        var slot = slotsA[0];

        var proposed = await ProposeAsync(setup, requestA.Id, slot, "  Dra. Ficticia  ");

        Assert.True(proposed.Ok, proposed.Error?.Message);
        var slotsB = await ProposalSlotsAsync(setup, requestB.Id);
        Assert.DoesNotContain(slotsB, s => s.StartsAt == slot.StartsAt);
        var family = (await ReadAsync(setup.Subject, setup.LinkId, requestA.Id)).Value!;
        Assert.Equal(AppointmentRequestStatus.Propuesta, family.Status);
        Assert.Equal(slot.StartsAt, family.ProposedStartsAt);
        Assert.Equal(slot.EndsAt, family.ProposedEndsAt);
        Assert.True(family.CanAccept);
        Assert.True(family.CanRequestAnotherTime);
        Assert.True(family.CanCancel);
        Assert.DoesNotContain("Dra. Ficticia", JsonSerializer.Serialize(family));
        var admin = await AdminViewAsync(setup, requestA.Id);
        Assert.Equal("Dra. Ficticia", admin.InternalProfessional);
        Assert.Equal(AppointmentRequestStatus.Propuesta, admin.Status);
        Assert.Equal(1, await AuditAsync(requestA.Id, "APPOINTMENT_REQUEST_PROPOSE"));

        // Una propuesta sobre una solicitud que ya no está pendiente, o sobre un hueco que no existe, no se acepta.
        Assert.Equal(ApplicationFailureCode.Conflict, (await ProposeAsync(setup, requestA.Id, slotsA[1])).Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, (await ProposeAsync(setup, requestB.Id, slot)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, (await ProposeAsync(setup, requestB.Id, new AppointmentSlot(slot.StartsAt.AddMinutes(7), slot.EndsAt))).Error!.Code);

        var another = await OtherTimeAsync(setup.Subject, setup.LinkId, requestA.Id);
        Assert.True(another.Ok, another.Error?.Message);
        Assert.Equal(AppointmentRequestStatus.Pendiente, another.Value!.Status);
        Assert.Null(another.Value.ProposedStartsAt);
        Assert.Contains(await ProposalSlotsAsync(setup, requestB.Id), s => s.StartsAt == slot.StartsAt);
        Assert.Null((await AdminViewAsync(setup, requestA.Id)).InternalProfessional);
        Assert.Equal(1, await AuditAsync(requestA.Id, "APPOINTMENT_REQUEST_OTHER_TIME"));
        Assert.True((await ProposeAsync(setup, requestB.Id, slot)).Ok);
        Assert.Equal(
            [AppointmentRequestAction.Creada, AppointmentRequestAction.Propuesta, AppointmentRequestAction.OtroHorario],
            (await AdminViewAsync(setup, requestA.Id)).Changes.Select(c => c.Action));
    }

    [Fact]
    public async Task LaPropuesta_CaducaALas48Horas_LiberaElHueco_YAdministracionVuelveAProponer()
    {
        var setup = await SetupAsync("Residente Solicitud Caduca Uno");
        await ConfigureAsync(setup);
        var other = await OtherResidentFamilyAsync(setup, "Residente Solicitud Caduca Dos");
        var request = await SentAsync(setup.Subject, setup.LinkId);
        var otherRequest = await SentAsync(other.Subject, other.LinkId);
        // Una cita de dentro de más de tres días: gana el límite de 48 horas desde que se propuso (CJ, portal 6.4).
        var slot = (await ProposalSlotsAsync(setup, request.Id, DateTimeOffset.UtcNow.AddDays(3))).First();
        Assert.True((await ProposeAsync(setup, request.Id, slot)).Ok);
        var proposedAt = await ProposedAtAsync(request.Id);
        var alive = (await ReadAsync(setup.Subject, setup.LinkId, request.Id)).Value!;
        Assert.Equal(proposedAt.AddHours(48), alive.ProposalExpiresAt);
        Assert.True(alive.CanAccept);
        Assert.Equal(proposedAt.AddHours(48), (await AdminViewAsync(setup, request.Id)).ProposalExpiresAt);
        Assert.Equal(proposedAt.AddHours(48), await StoredExpiryAsync(request.Id));
        // Al entrar en el portal, la familia que la envió la ve destacada (CJ, tercera 6.3); la otra familia, no.
        var portalQuery = await QueryAsync(setup.Subject);
        Assert.True(Assert.Single((await Portal(setup.Subject).ListResidentsAsync(portalQuery)).Value!).ProposalAwaitingAnswer);
        Assert.Equal(request.Id, (await Portal(setup.Subject).ReadResidentAsync(portalQuery, setup.LinkId)).Value!.ProposalRequestId);
        Assert.False(Assert.Single((await Portal(other.Subject).ListResidentsAsync(await QueryAsync(other.Subject))).Value!).ProposalAwaitingAnswer);
        Assert.DoesNotContain(await ProposalSlotsAsync(setup, otherRequest.Id), s => s.StartsAt == slot.StartsAt);
        // Mientras viva, Administración no puede proponer otra.
        Assert.Equal(ApplicationFailureCode.Conflict, (await ProposeAsync(setup, request.Id, await FarSlotAsync(setup, otherRequest.Id, 5))).Error!.Code);

        // Pasan 49 horas sin respuesta.
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync(
                "UPDATE dbo.citas_solicitudes SET propuesta_en = DATEADD(HOUR, -49, SYSUTCDATETIME()) WHERE id = @id", new { id = request.Id });
        }

        var expired = (await ReadAsync(setup.Subject, setup.LinkId, request.Id)).Value!;
        Assert.True(expired.ProposalExpired);
        Assert.False(expired.CanAccept);
        Assert.True((await AdminViewAsync(setup, request.Id)).ProposalExpired);
        Assert.Equal(ApplicationFailureCode.Conflict, (await AcceptAsync(setup.Subject, setup.LinkId, request.Id)).Error!.Code);
        Assert.Contains(await ProposalSlotsAsync(setup, otherRequest.Id), s => s.StartsAt == slot.StartsAt);
        Assert.False(Assert.Single((await Portal(setup.Subject).ListResidentsAsync(portalQuery)).Value!).ProposalAwaitingAnswer);
        Assert.Null((await Portal(setup.Subject).ReadResidentAsync(portalQuery, setup.LinkId)).Value!.ProposalRequestId);

        var again = await FarSlotAsync(setup, request.Id, 3);
        var reproposed = await ProposeAsync(setup, request.Id, again);

        Assert.True(reproposed.Ok, reproposed.Error?.Message);
        var renewed = (await ReadAsync(setup.Subject, setup.LinkId, request.Id)).Value!;
        Assert.Equal(again.StartsAt, renewed.ProposedStartsAt);
        Assert.True(renewed.CanAccept);
        Assert.Equal(
            [AppointmentRequestAction.Creada, AppointmentRequestAction.Propuesta, AppointmentRequestAction.Propuesta],
            (await AdminViewAsync(setup, request.Id)).Changes.Select(c => c.Action));
        Assert.Equal(2, await AuditAsync(request.Id, "APPOINTMENT_REQUEST_PROPOSE"));
        Assert.True((await AcceptAsync(setup.Subject, setup.LinkId, request.Id)).Ok);
    }

    [Fact]
    public async Task LaPropuesta_CaducaVeinticuatroHorasAntesDeLaCita_YNoSeOfrecenHuecosAMenosDe24Horas()
    {
        var setup = await SetupAsync("Residente Solicitud Caduca Antes");
        await ConfigureAsync(setup);
        var request = await SentAsync(setup.Subject, setup.LinkId);
        var now = DateTimeOffset.UtcNow;
        var slots = await ProposalSlotsAsync(setup, request.Id);
        Assert.All(slots, s => Assert.True(s.StartsAt > now.AddHours(24)));
        // Un hueco de dentro de menos de 48 horas: caduca 24 horas antes de la cita, antes que las 48 horas desde la propuesta.
        var near = slots.First(s => s.StartsAt < now.AddHours(47));

        Assert.True((await ProposeAsync(setup, request.Id, near)).Ok);

        var read = (await ReadAsync(setup.Subject, setup.LinkId, request.Id)).Value!;
        Assert.Equal(near.StartsAt.AddHours(-24), read.ProposalExpiresAt);
        Assert.Equal(near.StartsAt.AddHours(-24), await StoredExpiryAsync(request.Id));
        Assert.False(read.ProposalExpired);
    }

    private static async Task<DateTimeOffset> ProposedAtAsync(Guid requestId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return new DateTimeOffset(DateTime.SpecifyKind(
            await connection.QuerySingleAsync<DateTime>("SELECT propuesta_en FROM dbo.citas_solicitudes WHERE id = @requestId", new { requestId }), DateTimeKind.Utc));
    }

    /// <summary>La columna calculada propuesta_caduca_en (script 0070), que usan las consultas: tiene que coincidir con la regla del dominio.</summary>
    private static async Task<DateTimeOffset> StoredExpiryAsync(Guid requestId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return new DateTimeOffset(DateTime.SpecifyKind(
            await connection.QuerySingleAsync<DateTime>("SELECT propuesta_caduca_en FROM dbo.citas_solicitudes WHERE id = @requestId", new { requestId }), DateTimeKind.Utc));
    }

    [Fact]
    public async Task Aceptar_CreaLaCitaConfirmada_OcupaElHueco_YRepetirNoDuplica()
    {
        var setup = await SetupAsync("Residente Solicitud Aceptar");
        await ConfigureAsync(setup);
        var request = await SentAsync(setup.Subject, setup.LinkId);
        var slot = await FarSlotAsync(setup, request.Id);
        Assert.True((await ProposeAsync(setup, request.Id, slot)).Ok);

        var accepted = await AcceptAsync(setup.Subject, setup.LinkId, request.Id);
        var repeated = await AcceptAsync(setup.Subject, setup.LinkId, request.Id);

        Assert.True(accepted.Ok, accepted.Error?.Message);
        Assert.Equal(AppointmentRequestStatus.Aceptada, accepted.Value!.Status);
        var appointmentId = accepted.Value.AppointmentId!.Value;
        Assert.True(repeated.Ok, repeated.Error?.Message);
        Assert.Equal(appointmentId, repeated.Value!.AppointmentId);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas WHERE residente_id = @id", new { id = setup.ResidentId.Value }));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.citas WHERE id = @id AND estado_codigo = 'CONFIRMADA' AND inicio_en = @start AND equipo_codigo = 'MEDICO'",
            new { id = appointmentId, start = slot.StartsAt.UtcDateTime }));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_cambios WHERE cita_id = @id AND accion_codigo = 'CREADA'", new { id = appointmentId }));
        Assert.Equal(1, await AuditAsync(request.Id, "APPOINTMENT_REQUEST_ACCEPT"));
        Assert.Equal(0, await AuditAsync(appointmentId, "APPOINTMENT_BOOK"));
        var admin = await AdminViewAsync(setup, request.Id);
        Assert.Equal(AppointmentRequestStatus.Aceptada, admin.Status);
        Assert.Equal(appointmentId, admin.AppointmentId);

        // La cita es una cita normal: la familia la ve y la cancela, y el hueco se libera para proponerlo a otra solicitud.
        var home = (await Bookings(setup.Subject).ReadHomeAsync(await QueryAsync(setup.Subject), setup.LinkId)).Value!;
        var appointment = Assert.Single(home.Appointments);
        Assert.Equal(appointmentId, appointment.Id);
        Assert.True(appointment.CanCancel);
        Assert.False(appointment.CanReschedule);
        Assert.True((await Bookings(setup.Subject).CancelAsync(await QueryAsync(setup.Subject), setup.LinkId, appointmentId)).Ok);

        // Ya aceptada: ni otro horario ni cancelar la solicitud.
        Assert.Equal(ApplicationFailureCode.Conflict, (await OtherTimeAsync(setup.Subject, setup.LinkId, request.Id)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, (await CancelAsync(setup.Subject, setup.LinkId, request.Id)).Error!.Code);
    }

    [Fact]
    public async Task Aceptar_ConLaPropuestaCaducada_OConElHuecoTomado_Falla_SinDejarNada()
    {
        var setup = await SetupAsync("Residente Solicitud Fallos");
        await ConfigureAsync(setup);
        var expired = await SentAsync(setup.Subject, setup.LinkId, AppointmentTeam.Medico);
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                UPDATE dbo.citas_solicitudes
                   SET estado_codigo = 'PROPUESTA', propuesta_inicio_en = DATEADD(HOUR, -2, SYSUTCDATETIME()), propuesta_fin_en = DATEADD(MINUTE, -90, SYSUTCDATETIME()),
                       propuesta_en = DATEADD(HOUR, -30, SYSUTCDATETIME())
                 WHERE id = @id
                """, new { id = expired.Id });
        }

        var read = (await ReadAsync(setup.Subject, setup.LinkId, expired.Id)).Value!;
        Assert.True(read.ProposalExpired);
        Assert.False(read.CanAccept);
        Assert.True(read.CanRequestAnotherTime);
        Assert.True((await AdminViewAsync(setup, expired.Id)).ProposalExpired);
        Assert.Equal(ApplicationFailureCode.Conflict, (await AcceptAsync(setup.Subject, setup.LinkId, expired.Id)).Error!.Code);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.citas WHERE residente_id = @id", new { id = setup.ResidentId.Value }));
        Assert.Equal(1, await SavedAsync(expired.Id, "PROPUESTA"));
        Assert.True((await OtherTimeAsync(setup.Subject, setup.LinkId, expired.Id)).Ok);

        // Otra cita ocupa el hueco propuesto (por ejemplo, una reserva directa de antes del cambio de modo): no se acepta y la solicitud sigue propuesta.
        var slot = await FarSlotAsync(setup, expired.Id);
        Assert.True((await ProposeAsync(setup, expired.Id, slot)).Ok);
        var other = await OtherResidentFamilyAsync(setup, "Residente Solicitud Hueco Tomado");
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.citas
                    (id, centro_id, residente_id, vinculo_id, cuenta_id, equipo_codigo, modalidad_codigo, inicio_en, fin_en, estado_codigo, operacion_id, creada_en)
                SELECT NEWID(), @center, @resident, @link, account.id, 'MEDICO', 'PRESENCIAL', @start, @end, 'CONFIRMADA', NEWID(), SYSUTCDATETIME()
                  FROM dbo.cuentas account WHERE account.sujeto_externo = @subject
                """, new
            {
                center = setup.Admin.CenterId.Value, resident = other.ResidentId, link = other.LinkId, subject = other.Subject,
                start = slot.StartsAt.UtcDateTime, end = slot.EndsAt.UtcDateTime,
            });
        }

        var taken = await AcceptAsync(setup.Subject, setup.LinkId, expired.Id);
        Assert.Equal(ApplicationFailureCode.Conflict, taken.Error!.Code);
        Assert.Equal(1, await SavedAsync(expired.Id, "PROPUESTA"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.citas WHERE residente_id = @id", new { id = setup.ResidentId.Value }));
    }

    [Fact]
    public async Task Cancelar_YCerrar_LiberanElHueco_ConHistorial_YLaFamiliaNoVeElMotivoInterno()
    {
        var setup = await SetupAsync("Residente Solicitud Cierre Uno");
        await ConfigureAsync(setup);
        var other = await OtherResidentFamilyAsync(setup, "Residente Solicitud Cierre Dos");
        var pending = await SentAsync(setup.Subject, setup.LinkId, AppointmentTeam.Medico);
        var proposedRequest = await SentAsync(other.Subject, other.LinkId, AppointmentTeam.Medico);
        var slot = (await ProposalSlotsAsync(setup, proposedRequest.Id))[0];
        Assert.True((await ProposeAsync(setup, proposedRequest.Id, slot, "Enfermera Ficticia")).Ok);

        Assert.True((await CancelAsync(setup.Subject, setup.LinkId, pending.Id)).Ok);
        Assert.Equal(ApplicationFailureCode.Conflict, (await CancelAsync(setup.Subject, setup.LinkId, pending.Id)).Error!.Code);
        Assert.Equal(AppointmentRequestStatus.Cancelada, (await ReadAsync(setup.Subject, setup.LinkId, pending.Id)).Value!.Status);
        Assert.Equal(1, await AuditAsync(pending.Id, "APPOINTMENT_REQUEST_CANCEL"));

        var admin = AdminRequests(setup.Admin.ExternalSubject);
        var emptyReason = await admin.CloseAsync(new CloseAppointmentRequestCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, proposedRequest.Id, "   "));
        var closed = await admin.CloseAsync(new CloseAppointmentRequestCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, proposedRequest.Id, "Motivo interno secreto"));
        var closedAgain = await admin.CloseAsync(new CloseAppointmentRequestCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, proposedRequest.Id, "Otra vez"));

        Assert.Equal(ApplicationFailureCode.InvalidInput, emptyReason.Error!.Code);
        Assert.True(closed.Ok, closed.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, closedAgain.Error!.Code);
        var family = (await ReadAsync(other.Subject, other.LinkId, proposedRequest.Id)).Value!;
        Assert.Equal(AppointmentRequestStatus.Cerrada, family.Status);
        Assert.False(family.CanCancel);
        Assert.False(family.CanAccept);
        Assert.Null(family.ProposedStartsAt);
        Assert.True(family.Changes.Last().ByCenter);
        var familyText = JsonSerializer.Serialize(family);
        Assert.DoesNotContain("Motivo interno secreto", familyText);
        Assert.DoesNotContain("Enfermera Ficticia", familyText);
        var adminView = await AdminViewAsync(setup, proposedRequest.Id);
        Assert.Contains(adminView.Changes, c => c.Action == AppointmentRequestAction.Cerrada && c.Detail == "Motivo interno secreto");
        Assert.Equal(1, await AuditAsync(proposedRequest.Id, "APPOINTMENT_REQUEST_CLOSE"));
        Assert.Contains(await ProposalSlotsAsync(setup, (await SentAsync(setup.Subject, setup.LinkId, AppointmentTeam.Medico)).Id), s => s.StartsAt == slot.StartsAt);
        Assert.Equal(ApplicationFailureCode.Conflict, (await AcceptAsync(other.Subject, other.LinkId, proposedRequest.Id)).Error!.Code);
    }

    [Fact]
    public async Task LaFamilia_SoloVeYTocaSusSolicitudes_ConAutorizacionActiva_YAdministracionSoloLasDeSuCentro()
    {
        var setup = await SetupAsync("Residente Solicitud Aislamiento");
        await ConfigureAsync(setup);
        var request = await SentAsync(setup.Subject, setup.LinkId);
        var relative = await AddFamilyWithPortalAsync(setup, "Otra Familiar de Aislamiento");
        var outsider = await SetupAsync("Residente Solicitud Ajeno");
        await ConfigureAsync(outsider);

        var relativeList = (await Requests(relative.Subject).ListAsync(await QueryAsync(relative.Subject), relative.LinkId)).Value!;
        Assert.Empty(relativeList);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await ReadAsync(relative.Subject, relative.LinkId, request.Id)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await AcceptAsync(relative.Subject, relative.LinkId, request.Id)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await OtherTimeAsync(relative.Subject, relative.LinkId, request.Id)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await CancelAsync(relative.Subject, relative.LinkId, request.Id)).Error!.Code);
        // El vínculo ajeno o inexistente no se distingue de uno sin acceso.
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await ReadAsync(outsider.Subject, setup.LinkId, request.Id)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await ReadAsync(setup.Subject, Guid.NewGuid(), request.Id)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await SendAsync(outsider.Subject, setup.LinkId)).Error!.Code);
        Assert.Equal(1, await SavedAsync(request.Id, "PENDIENTE"));

        // Administración de otro centro: ni la ve ni la toca; el personal que no es Administración tampoco.
        var foreignAdmin = AdminRequests(outsider.Admin.ExternalSubject);
        Assert.Empty((await foreignAdmin.ListAsync(outsider.Admin.ProfileScopeId, outsider.Admin.CenterId, false)).Value!);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await foreignAdmin.ReadAsync(outsider.Admin.ProfileScopeId, outsider.Admin.CenterId, request.Id)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await foreignAdmin.ListProposalSlotsAsync(outsider.Admin.ProfileScopeId, outsider.Admin.CenterId, request.Id)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await foreignAdmin.CloseAsync(
            new CloseAppointmentRequestCommand(outsider.Admin.ProfileScopeId, outsider.Admin.CenterId, request.Id, "Intruso"))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await AdminRequests(setup.Nurse.ExternalSubject).ListAsync(setup.Nurse.ProfileScopeId, setup.Nurse.CenterId, false)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Requests(setup.Admin.ExternalSubject).ListAsync(
            new FamilyPortalQuery(setup.Admin.ProfileScopeId, setup.Admin.CenterId), setup.LinkId)).Error!.Code);
        Assert.Equal(1, await SavedAsync(request.Id, "PENDIENTE"));

        // Con la autorización suspendida o revocada la familia no ve ni toca nada, sin rastro.
        await ChangeAsync(setup.Admin, setup.ResidentId, setup.LinkId, FamilyAuthorizationChange.Suspender, 2, "Prueba");
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await ReadAsync(setup.Subject, setup.LinkId, request.Id)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await SendAsync(setup.Subject, setup.LinkId, AppointmentTeam.Enfermeria)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await CancelAsync(setup.Subject, setup.LinkId, request.Id)).Error!.Code);
        Assert.Equal(1, await SavedAsync(request.Id, "PENDIENTE"));
    }

    [Fact]
    public async Task CambiarDeModo_ConservaLoAbierto_PeroNoAdmiteSolicitudesNuevas()
    {
        var setup = await SetupAsync("Residente Solicitud Cambio Modo");
        await ConfigureAsync(setup);
        var other = await OtherResidentFamilyAsync(setup, "Residente Solicitud Cambio Modo Dos");
        var proposed = await SentAsync(setup.Subject, setup.LinkId);
        var pending = await SentAsync(other.Subject, other.LinkId);
        var slot = await FarSlotAsync(setup, proposed.Id);
        Assert.True((await ProposeAsync(setup, proposed.Id, slot)).Ok);

        await ConfigureAsync(setup, AppointmentServiceMode.Desactivado);
        var accepted = await AcceptAsync(setup.Subject, setup.LinkId, proposed.Id);
        var newOne = await SendAsync(setup.Subject, setup.LinkId, AppointmentTeam.Enfermeria);
        await ConfigureAsync(setup, AppointmentServiceMode.ReservaDirecta);
        var closed = await AdminRequests(setup.Admin.ExternalSubject).CloseAsync(
            new CloseAppointmentRequestCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, pending.Id, "Cambio de modo"));

        Assert.True(accepted.Ok, accepted.Error?.Message);
        Assert.Equal(AppointmentRequestStatus.Aceptada, accepted.Value!.Status);
        Assert.Equal(ApplicationFailureCode.Conflict, newOne.Error!.Code);
        Assert.True(closed.Ok, closed.Error?.Message);
        Assert.Single((await Bookings(setup.Subject).ReadHomeAsync(await QueryAsync(setup.Subject), setup.LinkId)).Value!.Appointments);
    }

    [Fact]
    public async Task DosPropuestasDelMismoHuecoALaVez_SoloUnaLoConsigue_YDosAceptacionesSeguidasCreanUnaCita()
    {
        var setup = await SetupAsync("Residente Solicitud Carrera Uno");
        await ConfigureAsync(setup);
        var other = await OtherResidentFamilyAsync(setup, "Residente Solicitud Carrera Dos");
        var requestA = await SentAsync(setup.Subject, setup.LinkId);
        var requestB = await SentAsync(other.Subject, other.LinkId);
        var slot = await FarSlotAsync(setup, requestA.Id);

        var proposals = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(i => ProposeAsync(setup, i % 2 == 0 ? requestA.Id : requestB.Id, slot)));

        Assert.Equal(1, proposals.Count(r => r.Ok));
        Assert.All(proposals.Where(r => !r.Ok), r => Assert.Equal(ApplicationFailureCode.Conflict, r.Error!.Code));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.citas_solicitudes WHERE centro_id = @center AND estado_codigo = 'PROPUESTA' AND propuesta_inicio_en = @start",
            new { center = setup.Admin.CenterId.Value, start = slot.StartsAt.UtcDateTime }));

        var winner = await SavedAsync(requestA.Id, "PROPUESTA") == 1 ? (setup.Subject, setup.LinkId, requestA.Id) : (other.Subject, other.LinkId, requestB.Id);
        var accepts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => AcceptAsync(winner.Item1, winner.Item2, winner.Item3)));

        Assert.All(accepts, r => Assert.True(r.Ok, r.Error?.Message));
        Assert.Single(accepts.Select(r => r.Value!.AppointmentId).Distinct());
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.citas WHERE centro_id = @center AND equipo_codigo = 'MEDICO' AND inicio_en = @start AND estado_codigo <> 'CANCELADA'",
            new { center = setup.Admin.CenterId.Value, start = slot.StartsAt.UtcDateTime }));
    }

    [Fact]
    public async Task LaBaseDeDatos_ImpideEditarElHistorial_BorrarSolicitudes_CambiarLaIdentidad_YLasTransicionesInvalidas()
    {
        var setup = await SetupAsync("Residente Solicitud Triggers");
        await ConfigureAsync(setup);
        var request = await SentAsync(setup.Subject, setup.LinkId);
        Assert.True((await CancelAsync(setup.Subject, setup.LinkId, request.Id)).Ok);
        var other = await SentAsync(setup.Subject, setup.LinkId);

        async Task ForbiddenAsync(string sql, string code, object? parameters = null)
        {
            using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
            var error = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, parameters));
            Assert.Contains(code, error.Message);
        }

        await ForbiddenAsync("UPDATE dbo.citas_solicitudes_cambios SET detalle = 'x' WHERE solicitud_id = @id", "APPOINTMENT_REQUEST_CHANGE_IMMUTABLE", new { id = request.Id });
        await ForbiddenAsync("DELETE FROM dbo.citas_solicitudes_cambios WHERE solicitud_id = @id", "APPOINTMENT_REQUEST_CHANGE_IMMUTABLE", new { id = request.Id });
        await ForbiddenAsync("DELETE FROM dbo.citas_solicitudes WHERE id = @id", "APPOINTMENT_REQUEST_NO_DELETE", new { id = request.Id });
        await ForbiddenAsync("UPDATE dbo.citas_solicitudes SET equipo_codigo = 'ENFERMERIA' WHERE id = @id", "APPOINTMENT_REQUEST_IDENTITY_IMMUTABLE", new { id = other.Id });
        await ForbiddenAsync("UPDATE dbo.citas_solicitudes SET comentario = 'otro' WHERE id = @id", "APPOINTMENT_REQUEST_IDENTITY_IMMUTABLE", new { id = other.Id });
        await ForbiddenAsync("UPDATE dbo.citas_solicitudes SET estado_codigo = 'CERRADA' WHERE id = @id", "APPOINTMENT_REQUEST_TRANSITION_INVALID", new { id = request.Id });
        await ForbiddenAsync("""
            INSERT INTO dbo.citas_solicitudes
                (id, centro_id, residente_id, vinculo_id, cuenta_id, equipo_codigo, modalidad_codigo, motivo_codigo, disponibilidad_dias, disponibilidad_franja,
                 estado_codigo, propuesta_inicio_en, propuesta_fin_en, propuesta_en, operacion_id, creada_en)
            SELECT NEWID(), centro_id, residente_id, vinculo_id, cuenta_id, 'ENFERMERIA', 'PRESENCIAL', 'OTRO', '', 'INDIFERENTE', 'PROPUESTA',
                   SYSUTCDATETIME(), DATEADD(MINUTE, 30, SYSUTCDATETIME()), SYSUTCDATETIME(), NEWID(), SYSUTCDATETIME()
              FROM dbo.citas_solicitudes WHERE id = @id
            """, "APPOINTMENT_REQUEST_TRANSITION_INVALID", new { id = other.Id });
        await ForbiddenAsync("""
            INSERT INTO dbo.citas_solicitudes
                (id, centro_id, residente_id, vinculo_id, cuenta_id, equipo_codigo, modalidad_codigo, motivo_codigo, disponibilidad_dias, disponibilidad_franja,
                 estado_codigo, operacion_id, creada_en)
            SELECT NEWID(), centro_id, residente_id, vinculo_id, cuenta_id, equipo_codigo, 'PRESENCIAL', 'OTRO', '', 'INDIFERENTE', 'PENDIENTE', NEWID(), SYSUTCDATETIME()
              FROM dbo.citas_solicitudes WHERE id = @id
            """, "UX_citas_solicitudes_abierta", new { id = other.Id });
        Assert.Equal(1, await SavedAsync(request.Id, "CANCELADA"));
        Assert.Equal(1, await SavedAsync(other.Id, "PENDIENTE"));
    }

    /// <summary>Otra familiar del mismo residente, con su propia cuenta y su autorización Activa.</summary>
    private static async Task<(string Subject, Guid LinkId)> AddFamilyWithPortalAsync(Setup setup, string name)
    {
        var linkId = await AddFamilyAsync(setup.Admin, setup.ResidentId, name);
        var subject = $"portal-{Guid.NewGuid():N}"[..20];
        var grant = await AdministracionResidentesTests.Build(setup.Admin.ExternalSubject).GrantFamilyPortalAccessAsync(
            new GrantFamilyPortalAccessCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, linkId, subject));
        Assert.True(grant.Ok, grant.Error?.Message);
        await ChangeAsync(setup.Admin, setup.ResidentId, linkId, FamilyAuthorizationChange.Abrir, 0);
        await ChangeAsync(setup.Admin, setup.ResidentId, linkId, FamilyAuthorizationChange.Activar, 1);
        return (subject, linkId);
    }
}

file sealed class FixedSolicitudesSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
