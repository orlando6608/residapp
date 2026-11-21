using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.PortalFamiliarTests;

namespace ResidApp.IntegrationTests;

/// <summary>El recordatorio de cita del día antes (ADR 0012; script 0092; docs/privacidad/recordatorio-cita-por-correo.md, condiciones R2 a R12). Nunca llama a ACS: el envío es un falso.
/// La hora del procedimiento que reserva se simula llamando a <c>dbo.reservar_recordatorios_cita_en</c> (solo la puede llamar el propietario) y la del servicio, con el argumento
/// «ahora»: así las pruebas no dependen de la hora a la que se ejecutan. Cada prueba crea su propio centro.</summary>
[Collection("AvisoCambioCita")]
public class RecordatorioCitaTests
{
    private const string FamilyAddress = "familiar.ficticio@example.test";
    private const string BaseUrl = "https://dev.example.test";

    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.FromResult(EmailSendResult.Ok("op-" + Guid.NewGuid().ToString("N")));
        }
    }

    private sealed record Row(string Estado, string? Motivo, string? Operacion, int Intentos, string Clave, string Idioma, string Version, DateTime? Resuelto);

    private static EmailSettings Active(bool anyRecipient = true, params string[] allowed) => new(true, BaseUrl, "es", anyRecipient, allowed, false, true);

    private static ReminderService Service(FakeEmailSender sender, EmailSettings settings) =>
        new(new SqlReminderStore(TestDatabase.ConnectionFactory, Madrid), sender, settings, null, null, Madrid);

    /// <summary>Hoy en Madrid (la fecha real).</summary>
    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Madrid).DateTime);

    /// <summary>La hora de Madrid del día (hoy + <paramref name="days"/>) como momento UTC.</summary>
    private static DateTime LocalUtc(DateOnly day, int hour, int minute = 0) => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, minute)), Madrid);

    private static DateTimeOffset Sim(DateOnly day, int hour, int minute = 0) => new(LocalUtc(day, hour, minute), TimeSpan.Zero);

    private static async Task<Setup> SetupWithAddressAsync(string name, string? address = FamilyAddress)
    {
        var setup = await SetupAsync(name);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE dbo.familiares SET correo = @address, correo_confirmado_en = CASE WHEN @address IS NULL THEN NULL ELSE SYSUTCDATETIME() END, correo_confirmado_por_cuenta_id = CASE WHEN @address IS NULL THEN NULL ELSE creado_por_cuenta_id END WHERE id = (SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @linkId)",
            new { address, linkId = setup.LinkId });
        return setup;
    }

    /// <summary>Una cita confirmada de la familia (reservada con su cuenta y su vínculo), con la hora de inicio y la de creación que se quieran.</summary>
    private static async Task<Guid> InsertAsync(
        Setup setup, string team, DateTime startsAtUtc, string status = "CONFIRMADA", DateTime? createdUtc = null)
    {
        var id = Guid.NewGuid();
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var accountId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.cuentas WHERE sujeto_externo = @subject", new { subject = setup.Subject });
        await connection.ExecuteAsync("""
            INSERT INTO dbo.citas
                (id, centro_id, residente_id, vinculo_id, cuenta_id, equipo_codigo, modalidad_codigo, inicio_en, fin_en, estado_codigo, operacion_id, creada_en)
            VALUES (@id, @center, @resident, @link, @account, @team, 'PRESENCIAL', @start, DATEADD(MINUTE, 30, @start), @status, NEWID(), COALESCE(@created, SYSUTCDATETIME()))
            """, new
        {
            id, center = setup.Admin.CenterId.Value, resident = setup.ResidentId.Value, link = setup.LinkId, account = accountId, team, start = startsAtUtc, status,
            created = createdUtc,
        });
        return id;
    }

    /// <summary>Ejecuta el procedimiento que reserva con la hora simulada y deja sin efecto los recordatorios pendientes que no sean de estas citas (otras pruebas comparten la base).</summary>
    private static async Task<int> ReserveAsync(DateTimeOffset simulatedNow, params Guid[] mine)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var reserved = await connection.ExecuteScalarAsync<int>(
            "EXEC dbo.reservar_recordatorios_cita_en @ahora_utc = @now, @idioma = N'es', @version = N'1'", new { now = simulatedNow.UtcDateTime });
        await connection.ExecuteAsync("""
            UPDATE dbo.envios_correo SET estado_codigo = 'OMITIDO', motivo_codigo = 'CITA_NO_VIGENTE', resuelto_en = SYSUTCDATETIME()
             WHERE plantilla_codigo = 'RECORDATORIO_CITA' AND estado_codigo = 'PENDIENTE' AND referencia_id NOT IN (SELECT value FROM STRING_SPLIT(@mine, ','))
            """, new { mine = string.Join(',', mine) });
        return reserved;
    }

    private static async Task<List<Row>> RowsAsync(Guid appointmentId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return (await connection.QueryAsync<Row>("""
            SELECT estado_codigo AS Estado, motivo_codigo AS Motivo, acs_operacion_id AS Operacion, CAST(intentos AS INT) AS Intentos, referencia_clave AS Clave,
                   idioma AS Idioma, plantilla_version AS Version, resuelto_en AS Resuelto
              FROM dbo.envios_correo WITH (NOLOCK) WHERE plantilla_codigo = 'RECORDATORIO_CITA' AND referencia_id = @appointmentId ORDER BY referencia_clave
            """, new { appointmentId })).ToList();
    }

    private static async Task ExecuteAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    [Fact]
    public async Task ReservaUnaFilaPorCitaConfirmadaDeMañana_ConLaFechaLocalComoClave_YNoLaRepite()
    {
        var setup = await SetupWithAddressAsync("Residente Recordatorio Reserva");
        var sim = Sim(Today.AddDays(2), 11);
        var tomorrow = await InsertAsync(setup, "MEDICO", LocalUtc(Today.AddDays(3), 12));
        var sameDay = await InsertAsync(setup, "MEDICO", LocalUtc(Today.AddDays(2), 12));
        var dayAfter = await InsertAsync(setup, "ENFERMERIA", LocalUtc(Today.AddDays(4), 12));
        var cancelled = await InsertAsync(setup, "ENFERMERIA", LocalUtc(Today.AddDays(3), 13), "CANCELADA");
        Guid[] mine = [tomorrow, sameDay, dayAfter, cancelled];

        Assert.True(await ReserveAsync(sim, mine) >= 1);

        var row = Assert.Single(await RowsAsync(tomorrow));
        Assert.Equal(("PENDIENTE", (string?)null, 0, Today.AddDays(3).ToString("yyyy-MM-dd"), "es", "1"), (row.Estado, row.Motivo, row.Intentos, row.Clave, row.Idioma, row.Version));
        Assert.Null(row.Resuelto);
        foreach (var other in new[] { sameDay, dayAfter, cancelled })
        {
            Assert.Empty(await RowsAsync(other));
        }

        // R7: otra vuelta, otra copia de la aplicación o el mismo minuto otra vez: sigue habiendo una sola fila.
        await ReserveAsync(sim, mine);
        await ReserveAsync(sim.AddMinutes(30), mine);
        Assert.Single(await RowsAsync(tomorrow));
    }

    [Fact]
    public async Task LaFranjaDeReservaEsDeLas10ALas1859_HoraDeMadrid()
    {
        var setup = await SetupWithAddressAsync("Residente Recordatorio Franja");
        var day = Today.AddDays(2);
        var first = await InsertAsync(setup, "MEDICO", LocalUtc(day.AddDays(1), 12));
        var second = await InsertAsync(setup, "ENFERMERIA", LocalUtc(day.AddDays(2), 12));
        Guid[] mine = [first, second];

        foreach (var (hour, minute) in new[] { (9, 59), (19, 0), (3, 0), (23, 30) })
        {
            Assert.Equal(0, await ReserveAsync(Sim(day, hour, minute), mine));
            Assert.Equal(0, await ReserveAsync(Sim(day.AddDays(1), hour, minute), mine));
        }

        Assert.Empty(await RowsAsync(first));
        Assert.Empty(await RowsAsync(second));

        // dentro de la franja (la primera y la última hora) sí reserva
        Assert.True(await ReserveAsync(Sim(day, 10, 0), mine) >= 1);
        Assert.Single(await RowsAsync(first));
        Assert.Empty(await RowsAsync(second));
        Assert.True(await ReserveAsync(Sim(day.AddDays(1), 18, 59), mine) >= 1);
        Assert.Single(await RowsAsync(second));
    }

    [Theory]
    [InlineData("2026-10-25", 0, 30)] // el día del cambio de hora de octubre dura 25 horas: «mañana» es el 26
    [InlineData("2026-03-29", 0, 30)] // el de marzo dura 23
    public async Task ElDiaDeMañanaSeCalculaConLaHoraDeMadrid_TambienEnLosDomingosDeCambioDeHora(string simulatedDay, int firstHour, int firstMinute)
    {
        var setup = await SetupWithAddressAsync("Residente Recordatorio Cambio Hora");
        var today = DateOnly.Parse(simulatedDay);
        var old = new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);
        var tomorrowFirst = await InsertAsync(setup, "MEDICO", LocalUtc(today.AddDays(1), firstHour, firstMinute), createdUtc: old);
        var tomorrowLast = await InsertAsync(setup, "ENFERMERIA", LocalUtc(today.AddDays(1), 23, 30), createdUtc: old);
        var todayLast = await InsertAsync(setup, "MEDICO", LocalUtc(today, 23, 30), createdUtc: old);
        var dayAfterFirst = await InsertAsync(setup, "ENFERMERIA", LocalUtc(today.AddDays(2), firstHour, firstMinute), createdUtc: old);

        await ReserveAsync(Sim(today, 11), tomorrowFirst, tomorrowLast, todayLast, dayAfterFirst);

        var key = today.AddDays(1).ToString("yyyy-MM-dd");
        Assert.Equal(key, Assert.Single(await RowsAsync(tomorrowFirst)).Clave);
        Assert.Equal(key, Assert.Single(await RowsAsync(tomorrowLast)).Clave);
        Assert.Empty(await RowsAsync(todayLast));
        Assert.Empty(await RowsAsync(dayAfterFirst));
    }

    [Fact]
    public async Task LaCitaReservadaOMovidaElMismoDia_QuedaOmitidaComoReciente_YNoSeEnvia()
    {
        var setup = await SetupWithAddressAsync("Residente Recordatorio Reciente");
        var today = Today;
        var sim = Sim(today, 11);
        var old = DateTime.UtcNow.AddDays(-3);
        var booked = await InsertAsync(setup, "MEDICO", LocalUtc(today.AddDays(1), 12)); // reservada ahora, hoy
        var moved = await InsertAsync(setup, "ENFERMERIA", LocalUtc(today.AddDays(1), 13), createdUtc: old);
        var movedYesterday = await InsertAsync(setup, "ENFERMERIA", LocalUtc(today.AddDays(1), 14), createdUtc: old);
        await ExecuteAsync("""
            INSERT INTO dbo.citas_cambios (id, centro_id, cita_id, numero, accion_codigo, actor_cuenta_id, actor_perfil_codigo, inicio_anterior_en, inicio_nuevo_en, motivo, ocurrido_en)
            SELECT NEWID(), centro_id, id, 1, 'REPROGRAMADA', @admin, 'ADMINISTRACION', DATEADD(HOUR, -1, inicio_en), inicio_en, N'Motivo interno.', SYSUTCDATETIME() FROM dbo.citas WHERE id = @moved
            """, new { admin = setup.Admin.AccountId.Value, moved });
        await ExecuteAsync("""
            INSERT INTO dbo.citas_cambios (id, centro_id, cita_id, numero, accion_codigo, actor_cuenta_id, actor_perfil_codigo, inicio_anterior_en, inicio_nuevo_en, motivo, ocurrido_en)
            SELECT NEWID(), centro_id, id, 1, 'REPROGRAMADA', @admin, 'ADMINISTRACION', DATEADD(HOUR, -1, inicio_en), inicio_en, NULL, DATEADD(DAY, -2, SYSUTCDATETIME()) FROM dbo.citas WHERE id = @movedYesterday
            """, new { admin = setup.Admin.AccountId.Value, movedYesterday });

        await ReserveAsync(sim, booked, moved, movedYesterday);

        foreach (var recent in new[] { booked, moved })
        {
            var row = Assert.Single(await RowsAsync(recent));
            Assert.Equal(("OMITIDO", "CITA_RECIENTE", 0), (row.Estado, row.Motivo, row.Intentos));
            Assert.NotNull(row.Resuelto);
        }

        Assert.Equal("PENDIENTE", Assert.Single(await RowsAsync(movedYesterday)).Estado);

        var sender = new FakeEmailSender();
        await Service(sender, Active()).ProcessDueAsync(sim);
        Assert.Single(sender.Sent); // solo la de ayer
    }

    [Fact]
    public async Task EnvíaUnSoloCorreoGenerico_AQuienReservo_YNoLoRepite()
    {
        var setup = await SetupWithAddressAsync("Residente Recordatorio Envio");
        var sim = Sim(Today.AddDays(2), 11);
        var appointment = await InsertAsync(setup, "MEDICO", LocalUtc(Today.AddDays(3), 12));
        await ReserveAsync(sim, appointment);
        var sender = new FakeEmailSender();

        Assert.Equal(1, await Service(sender, Active()).ProcessDueAsync(sim));
        Assert.Equal(0, await Service(sender, Active()).ProcessDueAsync(sim));
        await ReserveAsync(sim.AddMinutes(1), appointment);
        Assert.Equal(0, await Service(sender, Active()).ProcessDueAsync(sim.AddMinutes(1)));

        var message = Assert.Single(sender.Sent);
        Assert.Equal(FamilyAddress, message.To);
        Assert.Equal("Tienes una cita mañana en ResidApp", message.Subject);
        var row = Assert.Single(await RowsAsync(appointment));
        Assert.Equal(("ENVIADO", (string?)null, 1), (row.Estado, row.Motivo, row.Intentos));
        Assert.StartsWith("op-", row.Operacion);
    }

    [Fact]
    public async Task SiLaCitaSeMueveAOtraHoraElMismoDia_SigueEnviandose()
    {
        var setup = await SetupWithAddressAsync("Residente Recordatorio Otra Hora");
        var sim = Sim(Today.AddDays(2), 11);
        var appointment = await InsertAsync(setup, "MEDICO", LocalUtc(Today.AddDays(3), 12));
        await ReserveAsync(sim, appointment);
        await ExecuteAsync("UPDATE dbo.citas SET inicio_en = DATEADD(HOUR, 2, inicio_en), fin_en = DATEADD(HOUR, 2, fin_en) WHERE id = @appointment", new { appointment });
        var sender = new FakeEmailSender();

        await Service(sender, Active()).ProcessDueAsync(sim);

        Assert.Single(sender.Sent);
        Assert.Equal("ENVIADO", Assert.Single(await RowsAsync(appointment)).Estado);
    }

    [Theory]
    [InlineData("cancelada", "CITA_NO_VIGENTE")]
    [InlineData("movida de día", "CITA_NO_VIGENTE")]
    [InlineData("suspendido", "SIN_ACCESO")]
    [InlineData("desvinculado", "SIN_ACCESO")]
    [InlineData("cuenta suspendida", "SIN_ACCESO")]
    [InlineData("sin correo", "SIN_CORREO")]
    [InlineData("sin confirmar", "DIRECCION_NO_CONFIRMADA")]
    [InlineData("fuera de la franja", "FUERA_DE_PLAZO")]
    [InlineData("ya no es de mañana", "FUERA_DE_PLAZO")]
    public async Task SiAlEnviarYaNoEsVerdadOnoHayAQuienEscribir_SeOmiteConSuMotivo_SinCorreo(string what, string reason)
    {
        var setup = await SetupWithAddressAsync("Residente Recordatorio Omisiones " + what);
        var sim = Sim(Today.AddDays(2), 11);
        var appointment = await InsertAsync(setup, "MEDICO", LocalUtc(Today.AddDays(3), 12));
        await ReserveAsync(sim, appointment);
        var processAt = sim;
        switch (what)
        {
            case "cancelada":
                await ExecuteAsync("UPDATE dbo.citas SET estado_codigo = 'CANCELADA' WHERE id = @appointment", new { appointment });
                break;
            case "movida de día":
                await ExecuteAsync("UPDATE dbo.citas SET inicio_en = DATEADD(DAY, 1, inicio_en), fin_en = DATEADD(DAY, 1, fin_en) WHERE id = @appointment", new { appointment });
                break;
            case "suspendido":
                Assert.True((await EstadoResidenteTests.Build(setup.Admin.ExternalSubject).SuspendAsync(new SuspendResidentCommand(
                    setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, Guid.NewGuid(), null))).Ok);
                break;
            case "desvinculado":
                await ExecuteAsync(
                    "UPDATE dbo.residentes_familiares SET desvinculado_en = SYSUTCDATETIME(), desvinculado_por_cuenta_id = @account, desvinculado_motivo = 'Prueba.' WHERE id = @link",
                    new { account = setup.Admin.AccountId.Value, link = setup.LinkId });
                break;
            case "cuenta suspendida":
                await ExecuteAsync("UPDATE dbo.cuentas SET estado = 'SUSPENDED' WHERE sujeto_externo = @subject", new { subject = setup.Subject });
                break;
            case "sin correo":
                await ExecuteAsync("UPDATE dbo.familiares SET correo = NULL, correo_confirmado_en = NULL, correo_confirmado_por_cuenta_id = NULL WHERE id = (SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @link)", new { link = setup.LinkId });
                break;
            case "sin confirmar":
                await ExecuteAsync("UPDATE dbo.familiares SET correo_confirmado_en = NULL, correo_confirmado_por_cuenta_id = NULL WHERE id = (SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @link)", new { link = setup.LinkId });
                break;
            case "fuera de la franja":
                processAt = Sim(Today.AddDays(2), 19, 0);
                break;
            case "ya no es de mañana":
                processAt = Sim(Today.AddDays(3), 11); // un día tarde: la cita es de hoy
                break;
        }

        var sender = new FakeEmailSender();
        await Service(sender, Active()).ProcessDueAsync(processAt);

        Assert.Empty(sender.Sent);
        var row = Assert.Single(await RowsAsync(appointment));
        Assert.Equal(("OMITIDO", reason), (row.Estado, row.Motivo));
        Assert.NotNull(row.Resuelto);
    }

    [Fact]
    public async Task FueraDeProduccion_SoloSeEnviaALasDireccionesPermitidas()
    {
        var setup = await SetupWithAddressAsync("Residente Recordatorio Destino");
        var sim = Sim(Today.AddDays(2), 11);
        var appointment = await InsertAsync(setup, "MEDICO", LocalUtc(Today.AddDays(3), 12));
        await ReserveAsync(sim, appointment);
        var sender = new FakeEmailSender();

        await Service(sender, Active(anyRecipient: false, "otra.persona@example.test")).ProcessDueAsync(sim);

        Assert.Empty(sender.Sent);
        Assert.Equal("DESTINO_NO_PERMITIDO", Assert.Single(await RowsAsync(appointment)).Motivo);
    }

    [Fact]
    public async Task ConElInterruptorApagado_NoSeEnviaNada_AunqueHayaFilasReservadas()
    {
        var setup = await SetupWithAddressAsync("Residente Recordatorio Apagado");
        var sim = Sim(Today.AddDays(2), 11);
        var appointment = await InsertAsync(setup, "MEDICO", LocalUtc(Today.AddDays(3), 12));
        await ReserveAsync(sim, appointment);
        var sender = new FakeEmailSender();

        Assert.Equal(0, await Service(sender, Active() with { Reminder = false }).ProcessDueAsync(sim));
        Assert.Equal(0, await Service(sender, EmailSettings.Off).ProcessDueAsync(sim));

        Assert.Empty(sender.Sent);
        Assert.Equal("PENDIENTE", Assert.Single(await RowsAsync(appointment)).Estado);
    }

    [Fact]
    public async Task ElCierreDePendientes_IncluyeElRecordatorio_SinTocarLoQueSigueVigente()
    {
        var setup = await SetupWithAddressAsync("Residente Recordatorio Cierre");
        var claimed = await InsertAsync(setup, "MEDICO", LocalUtc(Today.AddDays(5), 12));
        var unclaimedOld = await InsertAsync(setup, "ENFERMERIA", LocalUtc(Today.AddDays(5), 12));
        var unclaimedRecent = await InsertAsync(setup, "MEDICO", LocalUtc(Today.AddDays(6), 12));
        await ExecuteAsync("""
            INSERT INTO dbo.envios_correo
                (id, centro_id, plantilla_codigo, plantilla_version, idioma, familiar_id, referencia_id, referencia_clave, estado_codigo, motivo_codigo, intentos, creado_en, resuelto_en)
            SELECT NEWID(), c.centro_id, 'RECORDATORIO_CITA', '1', 'es', link.familiar_id, c.id, '2099-01-01', 'PENDIENTE', NULL, v.intentos, DATEADD(MINUTE, -v.minutos, SYSUTCDATETIME()), NULL
              FROM dbo.citas c JOIN dbo.residentes_familiares link ON link.id = c.vinculo_id AND link.centro_id = c.centro_id
              JOIN (VALUES (@claimed, 1, 31), (@unclaimedOld, 0, 25 * 60), (@unclaimedRecent, 0, 60)) v(cita, intentos, minutos) ON v.cita = c.id
            """, new { claimed, unclaimedOld, unclaimedRecent });

        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteScalarAsync<int>("EXEC dbo.cerrar_envios_correo_sin_resolver");
        }

        var first = Assert.Single(await RowsAsync(claimed));
        Assert.Equal(("FALLIDO", "RESULTADO_DESCONOCIDO", 1), (first.Estado, first.Motivo, first.Intentos));
        var second = Assert.Single(await RowsAsync(unclaimedOld));
        Assert.Equal(("OMITIDO", "FUERA_DE_PLAZO", 0), (second.Estado, second.Motivo, second.Intentos));
        Assert.NotNull(second.Resuelto);
        Assert.Equal("PENDIENTE", Assert.Single(await RowsAsync(unclaimedRecent)).Estado);
    }

    /// <summary>Le pone al centro de la instalación de prueba la zona dada, como lo haría Plataforma (alta en Madrid y una corrección; script 0109).</summary>
    internal static async Task SetCenterZoneAsync(Setup setup, string zone)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.zonas_centro (id, centro_id, numero, zona_codigo, motivo_codigo, franjas_afectadas, citas_afectadas, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @center, 1, 'Europe/Madrid', 'ALTA', 0, 0, @actor, 'PLATAFORMA', SYSUTCDATETIME()),
                   (NEWID(), @center, 2, @zone, 'CAMBIO_DE_UBICACION', 0, 0, @actor, 'PLATAFORMA', SYSUTCDATETIME())
            """, new { center = setup.Admin.CenterId.Value, actor = setup.Admin.AccountId.Value, zone });
    }

    [Fact]
    public async Task CadaCentroReservaYEnviaEnLaFranjaDeSuZona_ElDeCanariasUnaHoraDespuesQueElDeLaPeninsula()
    {
        var madrid = await SetupWithAddressAsync("Residente Recordatorio Zona Madrid");
        var canary = await SetupWithAddressAsync("Residente Recordatorio Zona Canarias");
        await SetCenterZoneAsync(canary, "Atlantic/Canary");
        var day = Today.AddDays(2);
        var inMadrid = await InsertAsync(madrid, "MEDICO", LocalUtc(day.AddDays(1), 12));
        var inCanary = await InsertAsync(canary, "MEDICO", LocalUtc(day.AddDays(1), 12));
        Guid[] mine = [inMadrid, inCanary];

        // 10:30 en Madrid son las 09:30 en Canarias: solo el centro de la península está en su franja.
        await ReserveAsync(Sim(day, 10, 30), mine);
        Assert.Single(await RowsAsync(inMadrid));
        Assert.Empty(await RowsAsync(inCanary));

        // 19:30 en Madrid son las 18:30 en Canarias: ahora es al revés, y lo de Madrid no se repite.
        await ReserveAsync(Sim(day, 19, 30), mine);
        Assert.Single(await RowsAsync(inMadrid));
        var reserved = Assert.Single(await RowsAsync(inCanary));
        Assert.Equal(("PENDIENTE", day.AddDays(1).ToString("yyyy-MM-dd")), (reserved.Estado, reserved.Clave));

        // Al enviar, cada recordatorio se mira con la franja de su centro: el de Madrid ya está fuera de plazo y el de Canarias sale.
        var sender = new FakeEmailSender();
        var service = new ReminderService(new SqlReminderStore(TestDatabase.ConnectionFactory), sender, Active());
        await service.ProcessDueAsync(Sim(day, 19, 30));
        Assert.Single(sender.Sent);
        var late = Assert.Single(await RowsAsync(inMadrid));
        Assert.Equal(("OMITIDO", "FUERA_DE_PLAZO"), (late.Estado, late.Motivo));
        Assert.Equal("ENVIADO", Assert.Single(await RowsAsync(inCanary)).Estado);
    }

    [Fact]
    public async Task ElDiaDeMañanaEsElDelCentro_LaMismaCitaEsDeMañanaEnCanariasYDePasadoMañanaEnLaPeninsula()
    {
        // La cita es a las 23:30 UTC del día 15 de enero: en Canarias (UTC+0 en invierno) es el 15 a las 23:30; en la península (UTC+1) ya es el 16 a las 00:30.
        var madrid = await SetupWithAddressAsync("Residente Recordatorio Mañana Madrid");
        var canary = await SetupWithAddressAsync("Residente Recordatorio Mañana Canarias");
        await SetCenterZoneAsync(canary, "Atlantic/Canary");
        var startsAt = new DateTime(2026, 1, 15, 23, 30, 0, DateTimeKind.Utc);
        var old = new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);
        var inMadrid = await InsertAsync(madrid, "MEDICO", startsAt, createdUtc: old);
        var inCanary = await InsertAsync(canary, "MEDICO", startsAt, createdUtc: old);

        // El día 14 a las 12:00 UTC (13:00 en la península, 12:00 en Canarias) los dos centros están en franja y «mañana» es el 15 en los dos.
        await ReserveAsync(new DateTimeOffset(2026, 1, 14, 12, 0, 0, TimeSpan.Zero), inMadrid, inCanary);

        Assert.Empty(await RowsAsync(inMadrid)); // para la península la cita es del día 16
        Assert.Equal("2026-01-15", Assert.Single(await RowsAsync(inCanary)).Clave);
    }

    [Fact]
    public async Task ElRegistroDeUnRecordatorio_SoloGuardaElDiaDeLaCita_NiHoraNiDireccionNiMotivo()
    {
        var setup = await SetupWithAddressAsync("Residente Recordatorio Registro");
        var sim = Sim(Today.AddDays(2), 11);
        var appointment = await InsertAsync(setup, "MEDICO", LocalUtc(Today.AddDays(3), 12, 45));
        await ReserveAsync(sim, appointment);
        await Service(new FakeEmailSender(), Active()).ProcessDueAsync(sim);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var stored = (IDictionary<string, object?>)await connection.QuerySingleAsync(
            "SELECT * FROM dbo.envios_correo WITH (NOLOCK) WHERE plantilla_codigo = 'RECORDATORIO_CITA' AND referencia_id = @appointment", new { appointment });
        var values = string.Join('|', stored.Values);
        Assert.Equal(14, stored.Count); // las columnas de 0081: ninguna nueva guarda dirección, hora ni motivo
        Assert.DoesNotContain("example.test", values);
        Assert.DoesNotContain("12:45", values);
        Assert.DoesNotContain("Motivo", values, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", Assert.Single(await RowsAsync(appointment)).Clave);
    }
}
