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

/// <summary>Citas con reserva directa (script 0054): la configuración de Administración, los huecos, la reserva de la familia (exclusión del hueco,
/// idempotencia, límite), reprogramar y cancelar, y la gestión de Administración. Cada prueba crea su propio centro.</summary>
public class CitasTests
{
    private static AppointmentServiceApplicationService Admin(string subject) => new(
        new AdministrationAccessResolver(new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedCitasSessionIdentityProvider(subject)),
        new SqlAppointmentServiceRepository(TestDatabase.ConnectionFactory));

    private static FamilyAppointmentsApplicationService Family(string subject) => new(
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedCitasSessionIdentityProvider(subject),
        new SqlFamilyAppointments(TestDatabase.ConnectionFactory));

    private static readonly AppointmentModalitySetting[] AllModalities =
        [.. Enum.GetValues<AppointmentTeam>().SelectMany(team => Enum.GetValues<AppointmentModality>().Select(m => new AppointmentModalitySetting(team, m)))];

    /// <summary>Un centro con el servicio en reserva directa, todas las modalidades y, para los dos equipos, una franja todos los días de 08:00 a 20:00 en
    /// citas de 30 minutos: hay huecos sea cual sea el día en que se ejecute la prueba.</summary>
    private static async Task ConfigureAsync(
        Setup setup, int noticeHours = 1, int horizonDays = 30, AppointmentServiceMode mode = AppointmentServiceMode.ReservaDirecta)
    {
        var admin = Admin(setup.Admin.ExternalSubject);
        var version = (await admin.ReadAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId)).Value!.Version;
        var saved = await admin.SaveSettingsAsync(new SaveAppointmentServiceCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, version, mode, noticeHours, horizonDays, AllModalities));
        Assert.True(saved.Ok, saved.Error?.Message);
        if (version > 0)
        {
            return;
        }

        foreach (var team in Enum.GetValues<AppointmentTeam>())
        {
            for (var day = 1; day <= 7; day++)
            {
                var added = await admin.AddWindowAsync(new AddAppointmentWindowCommand(
                    setup.Admin.ProfileScopeId, setup.Admin.CenterId, team, day, new TimeOnly(8, 0), new TimeOnly(20, 0), 30));
                Assert.True(added.Ok, added.Error?.Message);
            }
        }
    }

    private static async Task<IReadOnlyList<AppointmentSlot>> SlotsAsync(
        string subject, Guid linkId, AppointmentTeam team = AppointmentTeam.Medico, AppointmentModality modality = AppointmentModality.Presencial, Guid? rescheduling = null)
    {
        var result = await Family(subject).ListSlotsAsync(await QueryAsync(subject), linkId, team, modality, rescheduling);
        Assert.True(result.Ok, result.Error?.Message);
        return result.Value!;
    }

    private static async Task<ApplicationResult<FamilyAppointment>> BookAsync(
        string subject, Guid linkId, AppointmentSlot slot, AppointmentTeam team = AppointmentTeam.Medico, AppointmentModality modality = AppointmentModality.Presencial,
        Guid? operationId = null)
    {
        var (scopeId, centerId) = await ScopeOfAsync(subject);
        return await Family(subject).BookAsync(new BookFamilyAppointmentCommand(scopeId, centerId, linkId, team, modality, slot.StartsAt, operationId ?? Guid.NewGuid()));
    }

    private static async Task<(Guid ProfileScopeId, CenterId CenterId)> ScopeOfAsync(string subject)
    {
        var query = await QueryAsync(subject);
        return (query.AmbitoPerfilId, query.CentroId);
    }

    private static async Task<FamilyAppointmentsHome> HomeAsync(string subject, Guid linkId)
    {
        var result = await Family(subject).ReadHomeAsync(await QueryAsync(subject), linkId);
        Assert.True(result.Ok, result.Error?.Message);
        return result.Value!;
    }

    /// <summary>Otra familiar del mismo residente, con su propia cuenta y su autorización Activa.</summary>
    private static async Task<(string Subject, Guid LinkId)> AddFamilyWithPortalAsync(Setup setup, ResidentId? residentId = null, string name = "Otra Familiar")
    {
        var resident = residentId ?? setup.ResidentId;
        var linkId = await AddFamilyAsync(setup.Admin, resident, name);
        var subject = $"portal-{Guid.NewGuid():N}"[..20];
        var grant = await AdministracionResidentesTests.Build(setup.Admin.ExternalSubject).GrantFamilyPortalAccessAsync(
            new GrantFamilyPortalAccessCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, resident, linkId, subject));
        Assert.True(grant.Ok, grant.Error?.Message);
        await ChangeAsync(setup.Admin, resident, linkId, FamilyAuthorizationChange.Abrir, 0);
        await ChangeAsync(setup.Admin, resident, linkId, FamilyAuthorizationChange.Activar, 1);
        return (subject, linkId);
    }

    /// <summary>Una cita insertada directamente con la hora que se quiera (para probar lo que depende del momento).</summary>
    private static async Task<Guid> InsertAppointmentAsync(
        Setup setup, AppointmentTeam team, DateTime startsAtUtc, string status = "CONFIRMADA", AppointmentModality modality = AppointmentModality.Presencial)
    {
        var id = Guid.NewGuid();
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var accountId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.cuentas WHERE sujeto_externo = @subject", new { subject = setup.Subject });
        await connection.ExecuteAsync("""
            INSERT INTO dbo.citas
                (id, centro_id, residente_id, vinculo_id, cuenta_id, equipo_codigo, modalidad_codigo, inicio_en, fin_en, estado_codigo, operacion_id, creada_en)
            VALUES (@id, @center, @resident, @link, @account, @team, @modality, @start, DATEADD(MINUTE, 30, @start), @status, NEWID(), SYSUTCDATETIME())
            """, new
        {
            id, center = setup.Admin.CenterId.Value, resident = setup.ResidentId.Value, link = setup.LinkId, account = accountId, team = team.ToCode(),
            modality = modality.ToCode(), start = startsAtUtc, status,
        });
        return id;
    }

    [Fact]
    public async Task Administracion_ConfiguraElServicio_YLaFamiliaVeLosHuecosLibresEntreLaAntelacionYElHorizonte()
    {
        var setup = await SetupAsync("Residente Citas Config");
        var disabledHome = await HomeAsync(setup.Subject, setup.LinkId);
        var disabledSlots = await SlotsAsync(setup.Subject, setup.LinkId);

        await ConfigureAsync(setup, noticeHours: 6, horizonDays: 10);
        var now = DateTimeOffset.UtcNow;
        var home = await HomeAsync(setup.Subject, setup.LinkId);
        var slots = await SlotsAsync(setup.Subject, setup.LinkId);
        var view = (await Admin(setup.Admin.ExternalSubject).ReadAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId)).Value!;

        Assert.False(disabledHome.Settings.BookingEnabled);
        Assert.Empty(disabledHome.Modalities);
        Assert.Empty(disabledSlots);
        Assert.True(home.Settings.BookingEnabled);
        Assert.Equal(6, home.Settings.MinimumNoticeHours);
        Assert.Equal(AllModalities.Length, home.Modalities.Count);
        Assert.NotEmpty(slots);
        Assert.All(slots, s => Assert.InRange(s.StartsAt, now.AddHours(6).AddMinutes(-1), now.AddDays(10).AddMinutes(1)));
        Assert.Equal(slots.OrderBy(s => s.StartsAt).Select(s => s.StartsAt), slots.Select(s => s.StartsAt));
        Assert.Equal(14, view.Windows.Count);
        Assert.Equal(1, view.Version);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE cuenta_id = @a AND accion_codigo = 'APPOINTMENT_SERVICE_UPDATE'", new { a = setup.Admin.AccountId.Value }));
        Assert.Equal(14, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE cuenta_id = @a AND accion_codigo = 'APPOINTMENT_WINDOW_ADD'", new { a = setup.Admin.AccountId.Value }));
    }

    [Fact]
    public async Task LaConfiguracion_ControlaLaVersion_LosSolapes_LosBloqueos_YLaRetiradaDeFranjas()
    {
        var setup = await SetupAsync("Residente Citas Reglas");
        await ConfigureAsync(setup);
        var admin = Admin(setup.Admin.ExternalSubject);
        var scope = (setup.Admin.ProfileScopeId, setup.Admin.CenterId);
        var view = (await admin.ReadAsync(scope.ProfileScopeId, scope.CenterId)).Value!;

        var stale = await admin.SaveSettingsAsync(new SaveAppointmentServiceCommand(
            scope.ProfileScopeId, scope.CenterId, 0, AppointmentServiceMode.Desactivado, 24, 30, []));
        var unchanged = await admin.SaveSettingsAsync(new SaveAppointmentServiceCommand(
            scope.ProfileScopeId, scope.CenterId, view.Version, AppointmentServiceMode.ReservaDirecta, 1, 30, AllModalities));
        var requestOutOfRange = await admin.SaveSettingsAsync(new SaveAppointmentServiceCommand(
            scope.ProfileScopeId, scope.CenterId, view.Version, AppointmentServiceMode.SolicitudPrevia, 721, 30, []));
        var overlap = await admin.AddWindowAsync(new AddAppointmentWindowCommand(
            scope.ProfileScopeId, scope.CenterId, AppointmentTeam.Medico, 1, new TimeOnly(19, 0), new TimeOnly(21, 0), 30));
        var invalid = await admin.AddWindowAsync(new AddAppointmentWindowCommand(
            scope.ProfileScopeId, scope.CenterId, AppointmentTeam.Medico, 1, new TimeOnly(21, 0), new TimeOnly(21, 10), 30));

        // Un bloqueo de toda la jornada de mañana de Medicina en Presencial quita esos huecos; Telefónica y Enfermería quedan intactos.
        var before = await SlotsAsync(setup.Subject, setup.LinkId);
        var day = before[0].StartsAt;
        var blockStart = day.AddHours(-1);
        var blockEnd = day.AddHours(1);
        var blocked = await admin.AddBlockAsync(new AddAppointmentBlockCommand(
            scope.ProfileScopeId, scope.CenterId, AppointmentTeam.Medico, AppointmentModality.Presencial, blockStart, blockEnd, "Sesión clínica"));
        var withBlock = await SlotsAsync(setup.Subject, setup.LinkId);
        var byPhone = await SlotsAsync(setup.Subject, setup.LinkId, modality: AppointmentModality.Telefonica);
        var nurses = await SlotsAsync(setup.Subject, setup.LinkId, AppointmentTeam.Enfermeria);
        var blockId = (await admin.ReadAsync(scope.ProfileScopeId, scope.CenterId)).Value!.Blocks.Single().Id;
        var retiredBlock = await admin.RetireBlockAsync(new AppointmentTargetCommand(scope.ProfileScopeId, scope.CenterId, blockId));
        var afterBlock = await SlotsAsync(setup.Subject, setup.LinkId);
        var retireAgain = await admin.RetireBlockAsync(new AppointmentTargetCommand(scope.ProfileScopeId, scope.CenterId, blockId));

        var mondayMedicine = view.Windows.Single(w => w.Team == AppointmentTeam.Medico && w.IsoDayOfWeek == 1);
        Assert.True((await admin.RetireWindowAsync(new AppointmentTargetCommand(scope.ProfileScopeId, scope.CenterId, mondayMedicine.Id))).Ok);
        var withoutMonday = (await admin.ReadAsync(scope.ProfileScopeId, scope.CenterId)).Value!;
        var retireTwice = await admin.RetireWindowAsync(new AppointmentTargetCommand(scope.ProfileScopeId, scope.CenterId, mondayMedicine.Id));

        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        Assert.Equal(view.Version, unchanged.Value);
        Assert.Equal(ApplicationFailureCode.InvalidInput, requestOutOfRange.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, overlap.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, invalid.Error!.Code);
        Assert.True(blocked.Ok, blocked.Error?.Message);
        Assert.DoesNotContain(withBlock, s => s.StartsAt >= blockStart && s.StartsAt < blockEnd);
        Assert.Equal(before.Count, withBlock.Count + before.Count(s => s.StartsAt >= blockStart && s.StartsAt < blockEnd));
        Assert.Contains(byPhone, s => s.StartsAt == day);
        Assert.Contains(nurses, s => s.StartsAt == day);
        Assert.True(retiredBlock.Ok);
        Assert.Contains(afterBlock, s => s.StartsAt == day);
        Assert.Equal(ApplicationFailureCode.AccessDenied, retireAgain.Error!.Code);
        Assert.Equal(13, withoutMonday.Windows.Count);
        Assert.Equal(ApplicationFailureCode.AccessDenied, retireTwice.Error!.Code);
    }

    [Fact]
    public async Task Reserva_CreaLaCita_OcupaElHueco_ConHistorialYAuditoria_YRespetaElLimitePorEquipo()
    {
        var setup = await SetupAsync("Residente Citas Reserva");
        await ConfigureAsync(setup);
        var slots = await SlotsAsync(setup.Subject, setup.LinkId);
        var slot = slots[0];

        var booked = await BookAsync(setup.Subject, setup.LinkId, slot, modality: AppointmentModality.Telefonica);
        var after = await SlotsAsync(setup.Subject, setup.LinkId);
        var home = await HomeAsync(setup.Subject, setup.LinkId);
        var secondSameTeam = await BookAsync(setup.Subject, setup.LinkId, slots[3]);
        var otherTeam = await BookAsync(setup.Subject, setup.LinkId, slot, AppointmentTeam.Enfermeria);

        Assert.True(booked.Ok, booked.Error?.Message);
        var appointment = booked.Value!;
        Assert.Equal(AppointmentStatus.Confirmada, appointment.Status);
        Assert.Equal(AppointmentTeam.Medico, appointment.Team);
        Assert.Equal(AppointmentModality.Telefonica, appointment.Modality);
        Assert.Equal(slot.StartsAt.UtcDateTime, appointment.StartsAt.UtcDateTime);
        Assert.Equal(slot.EndsAt.UtcDateTime, appointment.EndsAt.UtcDateTime);
        Assert.True(appointment.CanCancel && appointment.CanReschedule);
        Assert.DoesNotContain(after, s => s.StartsAt == slot.StartsAt);
        Assert.Equal(slots.Count - 1, after.Count);
        Assert.Equal(AppointmentChangeAction.Creada, Assert.Single(appointment.Changes).Action);
        Assert.Equal(appointment.Id, home.Appointments.Single(a => a.Team == AppointmentTeam.Medico).Id);
        Assert.Equal(ApplicationFailureCode.Conflict, secondSameTeam.Error!.Code);
        Assert.True(otherTeam.Ok, otherTeam.Error?.Message);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE accion_codigo = 'APPOINTMENT_BOOK' AND recurso_id = @id AND perfil_activo = 'FAMILIAR'", new { id = appointment.Id }));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_cambios WHERE cita_id = @id AND accion_codigo = 'CREADA'", new { id = appointment.Id }));
    }

    [Fact]
    public async Task Reserva_RepetirLaMismaOperacion_DevuelveLaMismaCita_YOtraPeticionConLaMismaOperacionSeRechaza()
    {
        var setup = await SetupAsync("Residente Citas Idempotencia");
        await ConfigureAsync(setup);
        var slots = await SlotsAsync(setup.Subject, setup.LinkId);
        var operationId = Guid.NewGuid();

        var first = await BookAsync(setup.Subject, setup.LinkId, slots[0], operationId: operationId);
        var doublePress = await BookAsync(setup.Subject, setup.LinkId, slots[0], operationId: operationId);
        var reused = await BookAsync(setup.Subject, setup.LinkId, slots[2], operationId: operationId);

        Assert.True(first.Ok, first.Error?.Message);
        Assert.True(doublePress.Ok, doublePress.Error?.Message);
        Assert.Equal(first.Value!.Id, doublePress.Value!.Id);
        Assert.Equal(ApplicationFailureCode.Conflict, reused.Error!.Code);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas WHERE operacion_id = @operationId", new { operationId }));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_cambios WHERE cita_id = @id", new { id = first.Value.Id }));
    }

    [Fact]
    public async Task DosFamiliasQueReservanElMismoHuecoALaVez_SoloUnaLoConsigue()
    {
        var setup = await SetupAsync("Residente Citas Carrera Uno");
        await ConfigureAsync(setup);
        var otherResident = await AdministracionResidentesTests.CreateResidentAsync(setup.Admin, "Residente Citas Carrera Dos");
        var other = await AddFamilyWithPortalAsync(setup, otherResident, "Familiar Carrera");
        var slot = (await SlotsAsync(setup.Subject, setup.LinkId))[0];

        var results = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(i => i % 2 == 0
                ? BookAsync(setup.Subject, setup.LinkId, slot)
                : BookAsync(other.Subject, other.LinkId, slot)));

        Assert.Equal(1, results.Count(r => r.Ok));
        Assert.All(results.Where(r => !r.Ok), r => Assert.Equal(ApplicationFailureCode.Conflict, r.Error!.Code));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.citas WHERE centro_id = @center AND equipo_codigo = 'MEDICO' AND inicio_en = @start AND estado_codigo <> 'CANCELADA'",
            new { center = setup.Admin.CenterId.Value, start = slot.StartsAt.UtcDateTime }));
    }

    [Fact]
    public async Task ReprogramarYCancelar_LiberanElHueco_ConHistorial_YNoSeRepiten()
    {
        var setup = await SetupAsync("Residente Citas Cambios");
        await ConfigureAsync(setup);
        var slots = await SlotsAsync(setup.Subject, setup.LinkId);
        var appointment = (await BookAsync(setup.Subject, setup.LinkId, slots[0])).Value!;
        var family = Family(setup.Subject);
        var (scopeId, centerId) = await ScopeOfAsync(setup.Subject);
        var query = new FamilyPortalQuery(scopeId, centerId);

        var rescheduleSlots = await SlotsAsync(setup.Subject, setup.LinkId, rescheduling: appointment.Id);
        var moved = await family.RescheduleAsync(new RescheduleFamilyAppointmentCommand(scopeId, centerId, setup.LinkId, appointment.Id, slots[4].StartsAt));
        var afterMove = await SlotsAsync(setup.Subject, setup.LinkId);
        var toSameSlot = await family.RescheduleAsync(new RescheduleFamilyAppointmentCommand(scopeId, centerId, setup.LinkId, appointment.Id, slots[4].StartsAt));
        var toTaken = await family.RescheduleAsync(new RescheduleFamilyAppointmentCommand(scopeId, centerId, setup.LinkId, appointment.Id, DateTimeOffset.UtcNow.AddYears(1)));
        var cancelled = await family.CancelAsync(query, setup.LinkId, appointment.Id);
        var afterCancel = await SlotsAsync(setup.Subject, setup.LinkId);
        var cancelAgain = await family.CancelAsync(query, setup.LinkId, appointment.Id);
        var moveCancelled = await family.RescheduleAsync(new RescheduleFamilyAppointmentCommand(scopeId, centerId, setup.LinkId, appointment.Id, slots[6].StartsAt));
        var detail = (await family.ReadAppointmentAsync(query, setup.LinkId, appointment.Id)).Value!;

        Assert.Contains(rescheduleSlots, s => s.StartsAt == slots[0].StartsAt);
        Assert.True(moved.Ok, moved.Error?.Message);
        Assert.Equal(slots[4].StartsAt.UtcDateTime, moved.Value!.StartsAt.UtcDateTime);
        Assert.Contains(afterMove, s => s.StartsAt == slots[0].StartsAt);
        Assert.DoesNotContain(afterMove, s => s.StartsAt == slots[4].StartsAt);
        Assert.Equal(ApplicationFailureCode.Conflict, toSameSlot.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, toTaken.Error!.Code);
        Assert.True(cancelled.Ok, cancelled.Error?.Message);
        Assert.Contains(afterCancel, s => s.StartsAt == slots[4].StartsAt);
        Assert.Equal(ApplicationFailureCode.Conflict, cancelAgain.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, moveCancelled.Error!.Code);
        Assert.Equal(AppointmentStatus.Cancelada, detail.Status);
        Assert.False(detail.CanCancel);
        Assert.Equal(new[] { AppointmentChangeAction.Creada, AppointmentChangeAction.Reprogramada, AppointmentChangeAction.Cancelada }, detail.Changes.Select(c => c.Action));
        Assert.All(detail.Changes, c => Assert.False(c.ByCenter));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE accion_codigo = 'APPOINTMENT_RESCHEDULE' AND recurso_id = @id", new { id = appointment.Id }));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE accion_codigo = 'APPOINTMENT_CANCEL' AND recurso_id = @id", new { id = appointment.Id }));
    }

    [Fact]
    public async Task LaFamilia_NoCambiaUnaCitaDentroDeLaAntelacionMinima_YAdministracionSi()
    {
        var setup = await SetupAsync("Residente Citas Antelacion");
        await ConfigureAsync(setup, noticeHours: 24);
        var soon = await InsertAppointmentAsync(setup, AppointmentTeam.Medico, DateTime.UtcNow.AddHours(2));
        var (scopeId, centerId) = await ScopeOfAsync(setup.Subject);
        var family = Family(setup.Subject);

        var home = await HomeAsync(setup.Subject, setup.LinkId);
        var cancel = await family.CancelAsync(new FamilyPortalQuery(scopeId, centerId), setup.LinkId, soon);
        var move = await family.RescheduleAsync(new RescheduleFamilyAppointmentCommand(
            scopeId, centerId, setup.LinkId, soon, (await SlotsAsync(setup.Subject, setup.LinkId))[0].StartsAt));
        var byAdmin = await Admin(setup.Admin.ExternalSubject).CancelAsync(new CancelAppointmentCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, soon, "El equipo no puede atender ese día."));

        var appointment = Assert.Single(home.Appointments);
        Assert.False(appointment.CanCancel);
        Assert.False(appointment.CanReschedule);
        Assert.Equal(ApplicationFailureCode.Conflict, cancel.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, move.Error!.Code);
        Assert.True(byAdmin.Ok, byAdmin.Error?.Message);
    }

    [Fact]
    public async Task SinAutorizacionActiva_NiVinculoPropio_LaFamiliaNoVeNiReservaNiTocaCitasAjenas()
    {
        var setup = await SetupAsync("Residente Citas Acceso");
        await ConfigureAsync(setup);
        var other = await AddFamilyWithPortalAsync(setup);
        var slot = (await SlotsAsync(setup.Subject, setup.LinkId))[0];
        var appointment = (await BookAsync(setup.Subject, setup.LinkId, slot)).Value!;
        var (otherScope, otherCenter) = await ScopeOfAsync(other.Subject);

        // La otra familiar del mismo residente no ve la cita de la primera; sí ve que el hueco está ocupado.
        var otherHome = await HomeAsync(other.Subject, other.LinkId);
        var otherSlots = await SlotsAsync(other.Subject, other.LinkId);
        var foreignLink = await Family(other.Subject).ReadHomeAsync(new FamilyPortalQuery(otherScope, otherCenter), setup.LinkId);
        var foreignBook = await BookAsync(other.Subject, setup.LinkId, otherSlots[0]);
        var foreignCancel = await Family(other.Subject).CancelAsync(new FamilyPortalQuery(otherScope, otherCenter), other.LinkId, appointment.Id);
        var foreignRead = await Family(other.Subject).ReadAppointmentAsync(new FamilyPortalQuery(otherScope, otherCenter), other.LinkId, appointment.Id);
        var foreignReschedule = await Family(other.Subject).ListSlotsAsync(
            new FamilyPortalQuery(otherScope, otherCenter), other.LinkId, AppointmentTeam.Medico, AppointmentModality.Presencial, appointment.Id);

        // Suspendida la autorización, la primera ya no ve nada ni puede reservar.
        await ChangeAsync(setup.Admin, setup.ResidentId, setup.LinkId, FamilyAuthorizationChange.Suspender, 2, "Petición del familiar.");
        var suspendedHome = await Family(setup.Subject).ReadHomeAsync(await QueryAsync(setup.Subject), setup.LinkId);
        var suspendedSlots = await Family(setup.Subject).ListSlotsAsync(
            await QueryAsync(setup.Subject), setup.LinkId, AppointmentTeam.Medico, AppointmentModality.Presencial, null);
        var suspendedBook = await BookAsync(setup.Subject, setup.LinkId, otherSlots[0]);

        Assert.Empty(otherHome.Appointments);
        Assert.DoesNotContain(otherSlots, s => s.StartsAt == slot.StartsAt);
        Assert.Equal(ApplicationFailureCode.AccessDenied, foreignLink.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, foreignBook.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, foreignCancel.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, foreignRead.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, foreignReschedule.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, suspendedHome.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, suspendedSlots.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, suspendedBook.Error!.Code);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.citas WHERE centro_id = @center AND estado_codigo = 'CONFIRMADA'", new { center = setup.Admin.CenterId.Value }));
    }

    [Fact]
    public async Task ConElServicioDesactivado_NoSeReservaNiSeReprograma_PeroLasCitasYaConfirmadasSeVenYSeCancelan()
    {
        var setup = await SetupAsync("Residente Citas Desactivado");
        await ConfigureAsync(setup);
        var slots = await SlotsAsync(setup.Subject, setup.LinkId);
        var appointment = (await BookAsync(setup.Subject, setup.LinkId, slots[0])).Value!;
        await ConfigureAsync(setup, mode: AppointmentServiceMode.Desactivado);
        var (scopeId, centerId) = await ScopeOfAsync(setup.Subject);
        var family = Family(setup.Subject);

        var home = await HomeAsync(setup.Subject, setup.LinkId);
        var noSlots = await SlotsAsync(setup.Subject, setup.LinkId);
        var book = await BookAsync(setup.Subject, setup.LinkId, slots[3], AppointmentTeam.Enfermeria);
        var move = await family.RescheduleAsync(new RescheduleFamilyAppointmentCommand(scopeId, centerId, setup.LinkId, appointment.Id, slots[4].StartsAt));
        var cancel = await family.CancelAsync(new FamilyPortalQuery(scopeId, centerId), setup.LinkId, appointment.Id);

        var existing = Assert.Single(home.Appointments);
        Assert.True(existing.CanCancel);
        Assert.False(existing.CanReschedule);
        Assert.False(home.Settings.BookingEnabled);
        Assert.Empty(noSlots);
        Assert.Equal(ApplicationFailureCode.Conflict, book.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, move.Error!.Code);
        Assert.True(cancel.Ok, cancel.Error?.Message);
    }

    [Fact]
    public async Task CitasPasadasSinCerrar_SonLasConfirmadasQueYaEmpezaron_DeCualquierSemana_YSalenAlFinalizarlas()
    {
        var setup = await SetupAsync("Residente Citas Sin Cerrar");
        await ConfigureAsync(setup);
        var slots = await SlotsAsync(setup.Subject, setup.LinkId);
        Assert.True((await BookAsync(setup.Subject, setup.LinkId, slots[0])).Ok);
        var lastMonth = await InsertAppointmentAsync(setup, AppointmentTeam.Enfermeria, DateTime.UtcNow.AddDays(-20));
        var yesterday = await InsertAppointmentAsync(setup, AppointmentTeam.Medico, DateTime.UtcNow.AddDays(-1));
        await InsertAppointmentAsync(setup, AppointmentTeam.Medico, DateTime.UtcNow.AddDays(-3), status: "CANCELADA");
        await InsertAppointmentAsync(setup, AppointmentTeam.Enfermeria, DateTime.UtcNow.AddDays(-4), status: "FINALIZADA");
        var admin = Admin(setup.Admin.ExternalSubject);

        var before = (await admin.ListPastUnclosedAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId)).Value!;
        var finished = await admin.FinishAsync(new AppointmentTargetCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, yesterday));
        var after = (await admin.ListPastUnclosedAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId)).Value!;

        Assert.Equal([lastMonth, yesterday], before.Select(a => a.Id));
        Assert.True(finished.Ok, finished.Error?.Message);
        Assert.Equal([lastMonth], after.Select(a => a.Id));
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Admin(setup.Nurse.ExternalSubject).ListPastUnclosedAsync(
            setup.Nurse.ProfileScopeId, setup.Nurse.CenterId)).Error!.Code);
    }

    [Fact]
    public async Task Administracion_VeLaAgenda_ReprogramaCancelaYFinaliza_ConMotivoHistorialYSinFiltrarElMotivoALaFamilia()
    {
        var setup = await SetupAsync("Residente Citas Agenda");
        await ConfigureAsync(setup);
        var slots = await SlotsAsync(setup.Subject, setup.LinkId);
        var first = (await BookAsync(setup.Subject, setup.LinkId, slots[0])).Value!;
        var second = (await BookAsync(setup.Subject, setup.LinkId, slots[1], AppointmentTeam.Enfermeria)).Value!;
        var past = await InsertAppointmentAsync(setup, AppointmentTeam.Enfermeria, DateTime.UtcNow.AddDays(-2), modality: AppointmentModality.Telefonica);
        var admin = Admin(setup.Admin.ExternalSubject);
        var scope = (setup.Admin.ProfileScopeId, setup.Admin.CenterId);

        var agenda = (await admin.ListAgendaAsync(scope.ProfileScopeId, scope.CenterId, DateTimeOffset.UtcNow.AddDays(-5), DateTimeOffset.UtcNow.AddDays(60))).Value!;
        var rescheduleSlots = (await admin.ListRescheduleSlotsAsync(scope.ProfileScopeId, scope.CenterId, first.Id)).Value!;
        var withoutReason = await admin.RescheduleAsync(new RescheduleAppointmentCommand(scope.ProfileScopeId, scope.CenterId, first.Id, rescheduleSlots[2].StartsAt, "  "));
        var onTaken = await admin.RescheduleAsync(new RescheduleAppointmentCommand(scope.ProfileScopeId, scope.CenterId, first.Id, DateTimeOffset.UtcNow.AddYears(1), "Motivo."));
        var moved = await admin.RescheduleAsync(new RescheduleAppointmentCommand(
            scope.ProfileScopeId, scope.CenterId, first.Id, rescheduleSlots[2].StartsAt, "  La sala no está disponible.  "));
        var finishFuture = await admin.FinishAsync(new AppointmentTargetCommand(scope.ProfileScopeId, scope.CenterId, second.Id));
        var finishPast = await admin.FinishAsync(new AppointmentTargetCommand(scope.ProfileScopeId, scope.CenterId, past));
        var finishAgain = await admin.FinishAsync(new AppointmentTargetCommand(scope.ProfileScopeId, scope.CenterId, past));
        var cancelled = await admin.CancelAsync(new CancelAppointmentCommand(scope.ProfileScopeId, scope.CenterId, second.Id, "El equipo no puede atender."));
        var cancelWithoutReason = await admin.CancelAsync(new CancelAppointmentCommand(scope.ProfileScopeId, scope.CenterId, first.Id, null));
        var after = (await admin.ListAgendaAsync(scope.ProfileScopeId, scope.CenterId, DateTimeOffset.UtcNow.AddDays(-5), DateTimeOffset.UtcNow.AddDays(60))).Value!;
        var familyView = await HomeAsync(setup.Subject, setup.LinkId);

        Assert.Equal(3, agenda.Count);
        Assert.Equal(agenda.OrderBy(a => a.StartsAt).Select(a => a.Id), agenda.Select(a => a.Id));
        var row = agenda.Single(a => a.Id == first.Id);
        Assert.Equal(("Residente Citas Agenda", "Lucía Pérez", "Hija"), (row.ResidentName, row.FamilyName, row.Relationship));
        Assert.DoesNotContain(rescheduleSlots, s => s.StartsAt == slots[0].StartsAt);
        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutReason.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, onTaken.Error!.Code);
        Assert.True(moved.Ok, moved.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, finishFuture.Error!.Code);
        Assert.True(finishPast.Ok, finishPast.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, finishAgain.Error!.Code);
        Assert.True(cancelled.Ok, cancelled.Error?.Message);
        Assert.Equal(ApplicationFailureCode.InvalidInput, cancelWithoutReason.Error!.Code);
        var movedRow = after.Single(a => a.Id == first.Id);
        Assert.Equal(rescheduleSlots[2].StartsAt.UtcDateTime, movedRow.StartsAt.UtcDateTime);
        var change = movedRow.Changes.Last();
        Assert.Equal((AppointmentChangeAction.Reprogramada, SystemProfile.Administracion, "La sala no está disponible."), (change.Action, change.ActorProfile, change.Reason));
        Assert.Equal(AppointmentStatus.Cancelada, after.Single(a => a.Id == second.Id).Status);
        Assert.Equal(AppointmentStatus.Finalizada, after.Single(a => a.Id == past).Status);
        // La familia ve que el centro cambió la cita, no por qué.
        var familyMoved = familyView.Appointments.Single(a => a.Id == first.Id);
        Assert.True(familyMoved.Changes.Last().ByCenter);
        Assert.Equal(AppointmentStatus.Cancelada, familyView.Appointments.Single(a => a.Id == second.Id).Status);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE accion_codigo = 'APPOINTMENT_RESCHEDULE_ADMIN' AND recurso_id = @id AND perfil_activo = 'ADMINISTRACION'", new { id = first.Id }));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE accion_codigo = 'APPOINTMENT_CANCEL_ADMIN' AND recurso_id = @id", new { id = second.Id }));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE accion_codigo = 'APPOINTMENT_FINISH' AND recurso_id = @id", new { id = past }));
    }

    [Fact]
    public async Task SoloAdministracionDelCentro_ConfiguraYGestiona()
    {
        var setup = await SetupAsync("Residente Citas Ambito");
        await ConfigureAsync(setup);
        var appointment = (await BookAsync(setup.Subject, setup.LinkId, (await SlotsAsync(setup.Subject, setup.LinkId))[0])).Value!;
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var direction = await SeedFixture.AddProfileToCenterAsync(SystemProfile.DireccionClinica, setup.Admin.CenterId, setup.Admin.UnitId);

        foreach (var who in new[] { setup.Nurse, direction })
        {
            var admin = Admin(who.ExternalSubject);
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await admin.ReadAsync(who.ProfileScopeId, who.CenterId)).Error!.Code);
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await admin.CancelAsync(new CancelAppointmentCommand(who.ProfileScopeId, who.CenterId, appointment.Id, "Motivo."))).Error!.Code);
        }

        // Un administrador de otro centro con el identificador de una cita ajena: ni la ve ni la toca.
        var foreign = Admin(outsider.ExternalSubject);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await foreign.FinishAsync(new AppointmentTargetCommand(outsider.ProfileScopeId, outsider.CenterId, appointment.Id))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await foreign.CancelAsync(new CancelAppointmentCommand(outsider.ProfileScopeId, outsider.CenterId, appointment.Id, "Motivo."))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await foreign.RescheduleAsync(new RescheduleAppointmentCommand(outsider.ProfileScopeId, outsider.CenterId, appointment.Id, DateTimeOffset.UtcNow.AddDays(2), "Motivo."))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await foreign.ListRescheduleSlotsAsync(outsider.ProfileScopeId, outsider.CenterId, appointment.Id)).Error!.Code);
        Assert.Empty((await foreign.ListAgendaAsync(outsider.ProfileScopeId, outsider.CenterId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(60))).Value!);
        Assert.Equal(AppointmentStatus.Confirmada, (await HomeAsync(setup.Subject, setup.LinkId)).Appointments.Single().Status);
    }

    [Fact]
    public async Task LaBaseDeDatos_ImpideDobleHueco_CambiarLaIdentidadDeUnaCita_BorrarlaYReabrirUnaCerrada()
    {
        var setup = await SetupAsync("Residente Citas Base");
        await ConfigureAsync(setup);
        var start = DateTime.UtcNow.AddDays(3);
        var appointment = await InsertAppointmentAsync(setup, AppointmentTeam.Medico, start);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var doubleSlot = await Assert.ThrowsAsync<SqlException>(() => InsertAppointmentAsync(setup, AppointmentTeam.Medico, start));
        var otherTeamSameTime = await InsertAppointmentAsync(setup, AppointmentTeam.Enfermeria, start);
        var changeIdentity = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.citas SET modalidad_codigo = 'TELEFONICA' WHERE id = @appointment", new { appointment }));
        var delete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("DELETE FROM dbo.citas WHERE id = @appointment", new { appointment }));
        Assert.Equal(1, await connection.ExecuteAsync("UPDATE dbo.citas SET estado_codigo = 'CANCELADA' WHERE id = @appointment", new { appointment }));
        var reopen = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.citas SET estado_codigo = 'CONFIRMADA' WHERE id = @appointment", new { appointment }));
        // Cancelada libera el hueco: otra cita puede ocuparlo.
        var again = await InsertAppointmentAsync(setup, AppointmentTeam.Medico, start);
        await connection.ExecuteAsync("""
            INSERT INTO dbo.citas_cambios (id, centro_id, cita_id, numero, accion_codigo, actor_cuenta_id, actor_perfil_codigo, ocurrido_en)
            VALUES (NEWID(), @center, @appointment, 1, 'CREADA', @account, 'ADMINISTRACION', SYSUTCDATETIME())
            """, new { center = setup.Admin.CenterId.Value, appointment = again, account = setup.Admin.AccountId.Value });
        var changeLog = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.citas_cambios SET motivo = N'Otro' WHERE cita_id = @appointment", new { appointment = again }));
        var windowId = await connection.ExecuteScalarAsync<Guid>("SELECT TOP (1) id FROM dbo.citas_franjas WHERE centro_id = @center", new { center = setup.Admin.CenterId.Value });
        var changeWindow = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.citas_franjas SET duracion_minutos = 10 WHERE id = @windowId", new { windowId }));
        var deleteWindow = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("DELETE FROM dbo.citas_franjas WHERE id = @windowId", new { windowId }));

        Assert.Contains("UX_citas_hueco", doubleSlot.Message);
        Assert.NotEqual(Guid.Empty, otherTeamSameTime);
        Assert.Contains("APPOINTMENT_IDENTITY_IMMUTABLE", changeIdentity.Message);
        Assert.Contains("APPOINTMENT_NO_DELETE", delete.Message);
        Assert.Contains("APPOINTMENT_CLOSED", reopen.Message);
        Assert.NotEqual(appointment, again);
        Assert.Contains("APPOINTMENT_CHANGE_IMMUTABLE", changeLog.Message);
        Assert.Contains("APPOINTMENT_WINDOW_IMMUTABLE", changeWindow.Message);
        Assert.Contains("APPOINTMENT_WINDOW_NO_DELETE", deleteWindow.Message);
    }

    private static async Task<int> CountAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }
}

file sealed class FixedCitasSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
