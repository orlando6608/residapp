using Dapper;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.Sessions;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.CitasTests;
using static ResidApp.IntegrationTests.PortalFamiliarTests;

namespace ResidApp.IntegrationTests;

/// <summary>El aviso por correo cuando el centro reprograma o cancela una cita confirmada (ADR 0012; script 0082). Nunca llama a ACS: el envío es un falso. Cada prueba crea
/// su propio centro. Las condiciones que se comprueban (N1 a N14) están en docs/privacidad/aviso-cita-cambio-cancelacion-por-correo.md, apartado 3.</summary>
[Collection("AvisoCambioCita")]
public class AvisoCambioCitaTests
{
    private const string FamilyAddress = "familiar.ficticio@example.test";
    private const string BaseUrl = "https://dev.example.test";
    private const string InternalReason = "MOTIVO-INTERNO-QUE-LA-FAMILIA-NO-VE";

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Func<EmailMessage, Task<EmailSendResult>>? Behavior { get; set; }

        public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            lock (Sent)
            {
                Sent.Add(message);
            }

            return Behavior is null ? EmailSendResult.Ok("op-" + Guid.NewGuid().ToString("N")) : await Behavior(message);
        }
    }

    private sealed record EmailRow(string Estado, string? Motivo, string? Operacion, int Intentos, string Clave, string Idioma, string Version);

    private static EmailSettings Active(bool anyRecipient = true, bool changeNotice = true, params string[] allowed) => new(true, BaseUrl, "es", anyRecipient, allowed, changeNotice);

    private static ChangeNoticeService Notice(FakeEmailSender sender, EmailSettings settings) =>
        new(new SqlChangeNoticeStore(TestDatabase.ConnectionFactory), sender, settings, null, null, TimeSpan.Zero);

    private static AppointmentServiceApplicationService AdminWith(string subject, ChangeNoticeService? notice) => new(
        new AdministrationAccessResolver(new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedCambioSessionIdentityProvider(subject)),
        new SqlAppointmentServiceRepository(TestDatabase.ConnectionFactory), notice);

    /// <summary>Un centro con el servicio en reserva directa, el familiar con correo y, por cada cita pedida, una confirmada que reservó la familia (médica, de enfermería
    /// y, si se piden más, insertadas directamente con su misma cuenta).</summary>
    private static async Task<(Setup Setup, List<Guid> Appointments)> WithAppointmentsAsync(string name, int count = 1, string? address = FamilyAddress)
    {
        await ClearPendingAsync();
        var setup = await SetupAsync(name);
        await ConfigureAsync(setup);
        await SetAddressAsync(setup.LinkId, address);
        var appointments = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            if (i >= 2)
            {
                // la familia solo puede tener una cita abierta por equipo: las demás se insertan directamente
                appointments.Add(await InsertAppointmentAsync(setup, AppointmentTeam.Medico, DateTime.UtcNow.AddDays(2 + i)));
                continue;
            }

            var team = i == 0 ? AppointmentTeam.Medico : AppointmentTeam.Enfermeria;
            var booked = await BookAsync(setup.Subject, setup.LinkId, (await SlotsAsync(setup.Subject, setup.LinkId, team))[0], team);
            Assert.True(booked.Ok, booked.Error?.Message);
            appointments.Add(booked.Value!.Id);
        }

        return (setup, appointments);
    }

    private static async Task SetAddressAsync(Guid linkId, string? address)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE dbo.familiares SET correo = @address WHERE id = (SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @linkId)", new { address, linkId });
    }

    /// <summary>Los avisos de cambio que otras pruebas dejaron sin enviar no deben quitarle el turno a los de esta (la tarea procesa los más antiguos primero).</summary>
    private static async Task ClearPendingAsync()
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            UPDATE dbo.envios_correo SET estado_codigo = 'OMITIDO', motivo_codigo = 'CITA_NO_VIGENTE', resuelto_en = SYSUTCDATETIME()
             WHERE plantilla_codigo = 'AVISO_CAMBIO_CITA' AND estado_codigo = 'PENDIENTE'
            """);
    }

    private static async Task<List<EmailRow>> RowsAsync(Guid appointmentId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return (await connection.QueryAsync<EmailRow>("""
            SELECT estado_codigo AS Estado, motivo_codigo AS Motivo, acs_operacion_id AS Operacion, CAST(intentos AS INT) AS Intentos, referencia_clave AS Clave,
                   idioma AS Idioma, plantilla_version AS Version
              FROM dbo.envios_correo WITH (NOLOCK) WHERE plantilla_codigo = 'AVISO_CAMBIO_CITA' AND referencia_id = @appointmentId ORDER BY CAST(referencia_clave AS INT)
            """, new { appointmentId })).ToList();
    }

    private static async Task<int> LastChangeNumberAsync(Guid appointmentId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>("SELECT MAX(numero) FROM dbo.citas_cambios WHERE cita_id = @appointmentId", new { appointmentId });
    }

    private static Task<ApplicationResult<bool>> CancelAsync(Setup setup, Guid appointmentId, ChangeNoticeService? notice) =>
        AdminWith(setup.Admin.ExternalSubject, notice).CancelAsync(new CancelAppointmentCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, appointmentId, InternalReason));

    private static async Task<ApplicationResult<bool>> RescheduleAsync(Setup setup, Guid appointmentId, ChangeNoticeService? notice, int slotIndex = 5)
    {
        var admin = AdminWith(setup.Admin.ExternalSubject, notice);
        var slots = (await admin.ListRescheduleSlotsAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId, appointmentId)).Value!;
        return await admin.RescheduleAsync(new RescheduleAppointmentCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, appointmentId, slots[slotIndex].StartsAt, InternalReason));
    }

    private static Task<int> ProcessAsync(ChangeNoticeService notice) => notice.ProcessDueAsync(DateTimeOffset.UtcNow.AddSeconds(1));

    [Fact]
    public async Task AdministracionCancelaYReprograma_ReservanUnAvisoCadaUna_ConElNumeroDelCambio_SinNadaDeContenido()
    {
        var (setup, appointments) = await WithAppointmentsAsync("Residente Aviso Cambio Uno", count: 2);
        var sender = new FakeEmailSender();
        var notice = Notice(sender, Active());

        Assert.True((await RescheduleAsync(setup, appointments[0], notice)).Ok);
        Assert.True((await CancelAsync(setup, appointments[1], notice)).Ok);

        var moved = Assert.Single(await RowsAsync(appointments[0]));
        var cancelled = Assert.Single(await RowsAsync(appointments[1]));
        Assert.Equal(("PENDIENTE", (string?)null, (string?)null, 0, "es", "1"), (moved.Estado, moved.Motivo, moved.Operacion, moved.Intentos, moved.Idioma, moved.Version));
        Assert.Equal((await LastChangeNumberAsync(appointments[0])).ToString(), moved.Clave);
        Assert.Equal((await LastChangeNumberAsync(appointments[1])).ToString(), cancelled.Clave);
        Assert.Empty(sender.Sent);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var dump = string.Join('|', await connection.QueryAsync<string>(
            "SELECT CONCAT(plantilla_codigo COLLATE DATABASE_DEFAULT, estado_codigo COLLATE DATABASE_DEFAULT, referencia_clave COLLATE DATABASE_DEFAULT) FROM dbo.envios_correo WITH (NOLOCK) WHERE referencia_id IN @ids",
            new { ids = appointments }));
        Assert.DoesNotContain("example.test", dump);
        Assert.DoesNotContain(InternalReason, dump);
    }

    [Fact]
    public async Task SoloAdministracionAlReprogramarOCancelar_EnganchaElAviso_NoLaReservaNiLaFinalizacionNiLaFamiliaNiLaBaja()
    {
        var (setup, appointments) = await WithAppointmentsAsync("Residente Aviso Cambio Dos", count: 3);
        var notice = Notice(new FakeEmailSender(), Active());
        var past = await InsertAppointmentAsync(setup, AppointmentTeam.Enfermeria, DateTime.UtcNow.AddDays(-2));
        var status = EstadoResidenteTests.Build(setup.Admin.ExternalSubject);
        var scope = (setup.Admin.ProfileScopeId, setup.Admin.CenterId);

        var familyQuery = await QueryAsync(setup.Subject);
        var familyMoved = await Family(setup.Subject).RescheduleAsync(new RescheduleFamilyAppointmentCommand(
            familyQuery.AmbitoPerfilId, familyQuery.CentroId, setup.LinkId, appointments[0], (await SlotsAsync(setup.Subject, setup.LinkId, rescheduling: appointments[0]))[8].StartsAt));
        var familyCancelled = await Family(setup.Subject).CancelAsync(familyQuery, setup.LinkId, appointments[1]);
        var finished = await AdminWith(setup.Admin.ExternalSubject, notice).FinishAsync(new AppointmentTargetCommand(scope.ProfileScopeId, scope.CenterId, past));
        var discharged = await status.DischargeAsync(new DischargeResidentCommand(
            scope.ProfileScopeId, scope.CenterId, setup.ResidentId, Guid.NewGuid(), ResidentDischargeReason.AltaVoluntaria, "Vuelve con su familia."));

        Assert.True(familyMoved.Ok, familyMoved.Error?.Message);
        Assert.True(familyCancelled.Ok, familyCancelled.Error?.Message);
        Assert.True(finished.Ok, finished.Error?.Message);
        Assert.True(discharged.Ok, discharged.Error?.Message);
        Assert.Equal(2, discharged.Value!.AppointmentsCancelled); // la reprogramada por la familia y la que seguía confirmada; la pasada no cuenta
        foreach (var id in new[] { appointments[0], appointments[1], appointments[2], past })
        {
            Assert.Empty(await RowsAsync(id));
        }
    }

    [Fact]
    public async Task ConElInterruptorPropioApagado_CancelarFuncionaIgual_PeroNoSeReservaNada()
    {
        var (setup, appointments) = await WithAppointmentsAsync("Residente Aviso Cambio Tres");

        var cancelled = await CancelAsync(setup, appointments[0], Notice(new FakeEmailSender(), Active(changeNotice: false)));

        Assert.True(cancelled.Ok, cancelled.Error?.Message);
        Assert.Empty(await RowsAsync(appointments[0]));
    }

    [Fact]
    public async Task Procesar_EnviaUnSoloCorreoGenerico_AQuienReservo_YNoDiceNadaDeLoQueHaPasado()
    {
        var (setup, appointments) = await WithAppointmentsAsync("Residente Aviso Cambio Cuatro");
        var otherLink = await AddFamilyAsync(setup.Admin, setup.ResidentId, "Marta Ficticia");
        await SetAddressAsync(otherLink, "otra.persona@example.test");
        var sender = new FakeEmailSender();
        var notice = Notice(sender, Active());
        Assert.True((await CancelAsync(setup, appointments[0], notice)).Ok);

        var claimed = await ProcessAsync(notice);

        Assert.True(claimed >= 1);
        var message = Assert.Single(sender.Sent);
        Assert.Equal(FamilyAddress, message.To);
        Assert.Equal("Tienes un cambio en una cita en ResidApp", message.Subject);
        var all = message.Subject + message.PlainText + message.Html;
        Assert.DoesNotContain(InternalReason, all);
        Assert.DoesNotContain("Residente Aviso Cambio Cuatro", all);
        Assert.DoesNotContain("Lucía", all);
        Assert.DoesNotContain("otra.persona", all);
        var row = Assert.Single(await RowsAsync(appointments[0]));
        Assert.Equal(("ENVIADO", (string?)null, 1, "1"), (row.Estado, row.Motivo, row.Intentos, row.Version));
        Assert.StartsWith("op-", row.Operacion);
    }

    [Fact]
    public async Task UnaReprogramacion_TambienSeEnvia()
    {
        var (setup, appointments) = await WithAppointmentsAsync("Residente Aviso Cambio Cinco");
        var sender = new FakeEmailSender();
        var notice = Notice(sender, Active());
        Assert.True((await RescheduleAsync(setup, appointments[0], notice)).Ok);

        await ProcessAsync(notice);

        Assert.Equal(FamilyAddress, Assert.Single(sender.Sent).To);
        Assert.Equal("ENVIADO", Assert.Single(await RowsAsync(appointments[0])).Estado);
    }

    [Fact]
    public async Task DosCambiosSeguidos_SoloAvisaElUltimo_ElPrimeroSeOmite()
    {
        var (setup, appointments) = await WithAppointmentsAsync("Residente Aviso Cambio Seis");
        var sender = new FakeEmailSender();
        var notice = Notice(sender, Active());
        Assert.True((await RescheduleAsync(setup, appointments[0], notice)).Ok);
        Assert.True((await CancelAsync(setup, appointments[0], notice)).Ok);

        await ProcessAsync(notice);

        var rows = await RowsAsync(appointments[0]);
        Assert.Equal(2, rows.Count);
        Assert.Equal(("OMITIDO", "CITA_NO_VIGENTE"), (rows[0].Estado, rows[0].Motivo));
        Assert.Equal("ENVIADO", rows[1].Estado);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task SiElResidenteSeDaDeBajaAntesDeEnviar_NoSeEnvia()
    {
        var (setup, appointments) = await WithAppointmentsAsync("Residente Aviso Cambio Siete");
        var sender = new FakeEmailSender();
        var notice = Notice(sender, Active());
        Assert.True((await CancelAsync(setup, appointments[0], notice)).Ok);
        var discharged = await EstadoResidenteTests.Build(setup.Admin.ExternalSubject).DischargeAsync(new DischargeResidentCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, Guid.NewGuid(), ResidentDischargeReason.AltaVoluntaria, "Vuelve con su familia."));
        Assert.True(discharged.Ok, discharged.Error?.Message);

        await ProcessAsync(notice);

        Assert.Empty(sender.Sent);
        var row = Assert.Single(await RowsAsync(appointments[0]));
        Assert.Equal(("OMITIDO", "SIN_ACCESO"), (row.Estado, row.Motivo));
    }

    [Fact]
    public async Task SinCorreoEnLaFicha_SeAnotaOmitido_SinEnvio()
    {
        var (setup, appointments) = await WithAppointmentsAsync("Residente Aviso Cambio Ocho", address: null);
        var sender = new FakeEmailSender();
        var notice = Notice(sender, Active());
        Assert.True((await CancelAsync(setup, appointments[0], notice)).Ok);

        await ProcessAsync(notice);

        Assert.Empty(sender.Sent);
        Assert.Equal(("OMITIDO", "SIN_CORREO"), (Assert.Single(await RowsAsync(appointments[0])).Estado, Assert.Single(await RowsAsync(appointments[0])).Motivo));
    }

    [Fact]
    public async Task SiLaHoraQueImportabaYaPaso_NoSeEnvia()
    {
        await ClearPendingAsync();
        var setup = await SetupAsync("Residente Aviso Cambio Nueve");
        await ConfigureAsync(setup);
        await SetAddressAsync(setup.LinkId, FamilyAddress);
        var past = await InsertAppointmentAsync(setup, AppointmentTeam.Medico, DateTime.UtcNow.AddHours(-3));
        var sender = new FakeEmailSender();
        var notice = Notice(sender, Active());
        Assert.True((await CancelAsync(setup, past, notice)).Ok);

        await ProcessAsync(notice);

        Assert.Empty(sender.Sent);
        var row = Assert.Single(await RowsAsync(past));
        Assert.Equal(("OMITIDO", "CITA_PASADA"), (row.Estado, row.Motivo));
    }

    [Fact]
    public async Task UnCambioDeLaFamiliaOUnaCancelacionPorBaja_NuncaSeAvisa_AunqueAlguienReservaraElAviso()
    {
        var (setup, appointments) = await WithAppointmentsAsync("Residente Aviso Cambio Diez", count: 2);
        var sender = new FakeEmailSender();
        var notice = Notice(sender, Active());
        var store = new SqlChangeNoticeStore(TestDatabase.ConnectionFactory);
        Assert.True((await Family(setup.Subject).CancelAsync(await QueryAsync(setup.Subject), setup.LinkId, appointments[0])).Ok);
        var discharged = await EstadoResidenteTests.Build(setup.Admin.ExternalSubject).DischargeAsync(new DischargeResidentCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, Guid.NewGuid(), ResidentDischargeReason.AltaVoluntaria, "Vuelve con su familia."));
        Assert.True(discharged.Ok, discharged.Error?.Message);
        foreach (var id in new[] { appointments[0], appointments[1] })
        {
            await store.RegisterAsync(setup.Admin.CenterId, id, await LastChangeNumberAsync(id), "es", ResidApp.Application.Email.ChangeNoticeTemplate.Version, DateTimeOffset.UtcNow);
        }

        await ProcessAsync(notice);

        Assert.Empty(sender.Sent);
        Assert.Equal(("OMITIDO", "CITA_NO_VIGENTE"), (Assert.Single(await RowsAsync(appointments[0])).Estado, Assert.Single(await RowsAsync(appointments[0])).Motivo));
        Assert.Equal(("OMITIDO", "CITA_NO_VIGENTE"), (Assert.Single(await RowsAsync(appointments[1])).Estado, Assert.Single(await RowsAsync(appointments[1])).Motivo));
    }

    [Fact]
    public async Task FueraDeProduccion_SoloSeEnviaALasDireccionesPermitidas()
    {
        var (setup, appointments) = await WithAppointmentsAsync("Residente Aviso Cambio Once");
        var sender = new FakeEmailSender();
        var notice = Notice(sender, Active(anyRecipient: false, changeNotice: true, "otra.persona@example.test"));
        Assert.True((await CancelAsync(setup, appointments[0], notice)).Ok);

        await ProcessAsync(notice);

        Assert.Empty(sender.Sent);
        var row = Assert.Single(await RowsAsync(appointments[0]));
        Assert.Equal(("OMITIDO", "DESTINO_NO_PERMITIDO"), (row.Estado, row.Motivo));
    }

    [Fact]
    public async Task SiElProveedorFalla_LaCitaQuedaCanceladaIgual_YElRegistroDiceElMotivoCerrado()
    {
        var (setup, appointments) = await WithAppointmentsAsync("Residente Aviso Cambio Doce");
        var sender = new FakeEmailSender { Behavior = _ => throw new InvalidOperationException($"no se pudo enviar a {FamilyAddress}") };
        var notice = Notice(sender, Active());
        var cancelled = await CancelAsync(setup, appointments[0], notice);

        await ProcessAsync(notice);

        Assert.True(cancelled.Ok, cancelled.Error?.Message);
        var row = Assert.Single(await RowsAsync(appointments[0]));
        Assert.Equal(("FALLIDO", "ACS_NO_DISPONIBLE", 1), (row.Estado, row.Motivo, row.Intentos));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas WHERE id = @id AND estado_codigo = 'CANCELADA'", new { id = appointments[0] }));
    }

    [Fact]
    public async Task DosEjecucionesALaVez_SoloMandanUnCorreo_YElMismoCambioNoSeReservaDosVeces()
    {
        var (setup, appointments) = await WithAppointmentsAsync("Residente Aviso Cambio Trece");
        var sender = new FakeEmailSender { Behavior = async _ => { await Task.Delay(300); return EmailSendResult.Ok("op"); } };
        var notice = Notice(sender, Active());
        Assert.True((await CancelAsync(setup, appointments[0], notice)).Ok);
        await notice.RegisterAsync(setup.Admin.CenterId, appointments[0], await LastChangeNumberAsync(appointments[0]));

        await Task.WhenAll(ProcessAsync(notice), ProcessAsync(notice));

        Assert.Single(sender.Sent);
        Assert.Equal("ENVIADO", Assert.Single(await RowsAsync(appointments[0])).Estado);
    }

    [Fact]
    public async Task ElAlmacenNuncaLeeElMotivoDelCambio()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "ResidApp.Infrastructure", "Persistence", "SqlChangeNoticeStore.cs")))
        {
            directory = directory.Parent;
        }

        var source = await File.ReadAllTextAsync(Path.Combine(directory!.FullName, "src", "ResidApp.Infrastructure", "Persistence", "SqlChangeNoticeStore.cs"));
        var code = string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));

        Assert.DoesNotMatch(@"(?<![\w])motivo(?![\w])", code);
        Assert.DoesNotContain("detalle", code, StringComparison.OrdinalIgnoreCase);
    }
}

file sealed class FixedCambioSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
