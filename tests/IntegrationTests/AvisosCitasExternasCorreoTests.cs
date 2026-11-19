using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Email;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Families;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;
using static ResidApp.IntegrationTests.PortalFamiliarTests;

namespace ResidApp.IntegrationTests;

/// <summary>Los avisos de citas externas a la familia por correo, de extremo a extremo (script 0105; docs/tareas/alta-prioridad/pendientes-citas-externas.md, apartado 11; docs/privacidad/citas-externas-lectura-dpo.md,
/// apartado 5.2). Nunca llama a ACS: el envío es un falso. La hora de la reserva se simula con <c>dbo.reservar_avisos_citas_externas_en</c> (solo la puede llamar el propietario) y la del servicio, con el
/// argumento «ahora». Cada prueba crea su propio centro y sus propias citas.</summary>
[Collection("AvisoCambioCita")]
public class AvisosCitasExternasCorreoTests
{
    private const string BaseUrl = "https://dev.example.test";

    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            lock (Sent)
            {
                Sent.Add(message);
            }

            return Task.FromResult(EmailSendResult.Ok("op-" + Guid.NewGuid().ToString("N")));
        }
    }

    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Madrid).DateTime);

    private static DateTime LocalUtc(DateOnly day, int hour) => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, 0)), Madrid);

    private static DateTimeOffset Sim(DateOnly day, int hour) => new(LocalUtc(day, hour), TimeSpan.Zero);

    private static EmailSettings Active(bool anyRecipient = true, params string[] allowed) => new(true, BaseUrl, "es", anyRecipient, allowed, false, false, true);

    private static ExternalNoticeService Service(FakeEmailSender sender, EmailSettings settings) =>
        new(new SqlExternalNoticeStore(TestDatabase.ConnectionFactory), sender, settings, null, null, Madrid);

    private static SqlProfileScopeDirectoryProvider Scopes => new(TestDatabase.ConnectionFactory);

    private static ExternalAppointmentsApplicationService Staff(SeededProfile by) =>
        new(Scopes, new FixedMailSessionIdentityProvider(by.ExternalSubject), new SqlExternalAppointmentRepository(TestDatabase.ConnectionFactory), TimeProvider.System);

    private static ExternalAppointmentFamilySettingsApplicationService Settings(SeededProfile by) =>
        new(Scopes, new FixedMailSessionIdentityProvider(by.ExternalSubject), new SqlExternalAppointmentRepository(TestDatabase.ConnectionFactory), TimeProvider.System);

    private static FamilyExternalAppointmentsApplicationService Family(string subject) =>
        new(Scopes, new FixedMailSessionIdentityProvider(subject), new SqlFamilyExternalAppointments(TestDatabase.ConnectionFactory), TimeProvider.System);

    private sealed record Person(Guid LinkId, string Subject, Guid FamilyId, string Address);

    private sealed record World(Setup Setup, Person First, Person Second);

    /// <summary>Un residente con dos personas de su familia con acceso, correo confirmado y los avisos de citas externas encendidos en el centro.</summary>
    private static async Task<World> WorldAsync(string name, bool mailOn = true)
    {
        var setup = await SetupAsync(name);
        var first = await ConfirmedAsync(setup, setup.LinkId, setup.Subject);
        var secondLink = await AddFamilyAsync(setup.Admin, setup.ResidentId, "Mateo Pérez");
        var secondSubject = $"portal-{Guid.NewGuid():N}"[..20];
        var grant = await Build(setup.Admin.ExternalSubject).GrantFamilyPortalAccessAsync(
            new GrantFamilyPortalAccessCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, secondLink, secondSubject));
        Assert.True(grant.Ok, grant.Error?.Message);
        await ChangeAsync(setup.Admin, setup.ResidentId, secondLink, FamilyAuthorizationChange.Abrir, 0);
        await ChangeAsync(setup.Admin, setup.ResidentId, secondLink, FamilyAuthorizationChange.Activar, 1);
        var second = await ConfirmedAsync(setup, secondLink, secondSubject);

        var family = await Settings(setup.Admin).SetCenterAsync(new SetExternalFamilyCenterCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, 0, true, true, true, true));
        Assert.True(family.Ok, family.Error?.Message);
        if (mailOn)
        {
            var mail = await Settings(setup.Admin).SetMailAsync(new SetExternalMailCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, 0, true, true));
            Assert.True(mail.Ok, mail.Error?.Message);
        }

        return new World(setup, first, second);
    }

    private static async Task<Person> ConfirmedAsync(Setup setup, Guid linkId, string subject)
    {
        var address = $"familia-{Guid.NewGuid():N}@example.test";
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE dbo.familiares SET correo = @address, correo_confirmado_en = SYSUTCDATETIME(), correo_confirmado_por_cuenta_id = creado_por_cuenta_id WHERE id = (SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @linkId)",
            new { address, linkId });
        var familyId = await connection.ExecuteScalarAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @linkId", new { linkId });
        return new Person(linkId, subject, familyId, address);
    }

    private static async Task<Guid> RegisterAsync(World world, int days)
    {
        var registered = await Staff(world.Setup.Admin).RegisterAsync(new RegisterExternalAppointmentCommand(
            world.Setup.Admin.ProfileScopeId, world.Setup.Admin.CenterId, world.Setup.ResidentId,
            new ExternalAppointment(Today.AddDays(days), new TimeOnly(10, 30), ExternalAppointmentType.Consulta, ExternalAppointmentSpecialty.Oncologia, "Hospital Ficticio Norte",
                ExternalTransportMode.Familia, ExternalArrangementState.Pendiente, ExternalCompanionMode.Familiar, ExternalArrangementState.Pendiente, null, ExternalFamilyVisibility.Visible)));
        Assert.True(registered.Ok, registered.Error?.Message);
        return registered.Value;
    }

    private static async Task<int> ReserveAsync(DateOnly day, int hour)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "EXEC dbo.reservar_avisos_citas_externas_en @ahora_utc = @utc, @idioma = 'es', @version = '1'", new { utc = LocalUtc(day, hour) }, commandTimeout: 180));
    }

    private sealed record Row(string Template, string Key, string State, string? Reason, int Attempts, Guid FamilyId, string Version);

    private static async Task<List<Row>> RowsAsync(Guid appointmentId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return [.. await connection.QueryAsync<Row>("""
            SELECT plantilla_codigo AS Template, referencia_clave AS [Key], estado_codigo AS State, motivo_codigo AS Reason, CAST(intentos AS INT) AS Attempts, familiar_id AS FamilyId, plantilla_version AS Version
              FROM dbo.envios_correo WHERE referencia_id = @appointmentId ORDER BY creado_en, familiar_id
            """, new { appointmentId })];
    }

    private static async Task<FamilyArrangementOutcome> ActAsync(Person person, Guid appointmentId, FamilyArrangementAction action, ExternalArrangementScope scope = ExternalArrangementScope.Transporte)
    {
        var result = await Family(person.Subject).ArrangeAsync(await QueryAsync(person.Subject), person.LinkId, appointmentId, scope, action);
        Assert.True(result.Ok, result.Error?.Message);
        return result.Value;
    }

    private static async Task ExecuteAsync(string sql, object? parameters = null)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    // ---- A quién se escribe ----

    [Fact]
    public async Task ASieteDiasYLaVispera_SinNadieEncargado_EscribeUnCorreoPorPersona_ConUnTextoQueNoDiceNadaDeLaCita()
    {
        var world = await WorldAsync("Residente Correo Todos");
        var sevenDays = await RegisterAsync(world, 7);
        var eve = await RegisterAsync(world, 1);
        var sender = new FakeEmailSender();

        Assert.True(await ReserveAsync(Today, 11) >= 4);
        var rows = await RowsAsync(sevenDays);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(("AVISO_CITA_EXTERNA", $"7D|{Today.AddDays(7):yyyy-MM-dd}", "PENDIENTE", (string?)null, "1"), (r.Template, r.Key, r.State, r.Reason, r.Version)));
        Assert.Equal($"1D|{Today.AddDays(1):yyyy-MM-dd}", (await RowsAsync(eve)).First().Key);

        await Service(sender, Active()).ProcessDueAsync(Sim(Today, 11));
        var mine = sender.Sent.Where(m => new[] { world.First.Address, world.Second.Address }.Contains(m.To)).ToList();
        Assert.Equal(4, mine.Count);
        Assert.Equal(2, mine.Count(m => m.To == world.First.Address));
        Assert.All(mine, m =>
        {
            Assert.Equal("Tienes un aviso sobre una cita en ResidApp", m.Subject);
            Assert.Contains(BaseUrl, m.PlainText);
            Assert.Contains(ExternalNoticeTemplate.OptOutEs, m.PlainText);
            foreach (var secret in new[] { "Oncolog", "Hospital", "Ficticio", "Pérez", "Lucía", "Mateo", "10:30" })
            {
                Assert.DoesNotContain(secret, m.PlainText + m.Html + m.Subject, StringComparison.OrdinalIgnoreCase);
            }
        });
        Assert.All(await RowsAsync(sevenDays), r => Assert.Equal(("ENVIADO", 1), (r.State, r.Attempts)));
    }

    [Fact]
    public async Task SiAlguienSeHaEncargado_ElAvisoDeLaVisperaVaSoloAEsaPersona()
    {
        var world = await WorldAsync("Residente Correo Encargado");
        var id = await RegisterAsync(world, 1);
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(world.Second, id, FamilyArrangementAction.Take));
        var sender = new FakeEmailSender();

        await ReserveAsync(Today, 11);
        var rows = await RowsAsync(id);
        Assert.Equal(world.Second.FamilyId, Assert.Single(rows).FamilyId);

        await Service(sender, Active()).ProcessDueAsync(Sim(Today, 11));
        Assert.Equal(world.Second.Address, Assert.Single(sender.Sent.Where(m => m.To == world.Second.Address || m.To == world.First.Address)).To);
    }

    [Fact]
    public async Task ADosDias_SoloSiSigueSinCubrir_YNoAQuienSeEncargoODijoNoPuedo_ConLaPlantillaDeSinCubrir()
    {
        var world = await WorldAsync("Residente Correo DosDias");
        var id = await RegisterAsync(world, 2);
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(world.First, id, FamilyArrangementAction.Refuse));
        var sender = new FakeEmailSender();

        await ReserveAsync(Today, 11);
        var rows = await RowsAsync(id);
        Assert.All(rows, r => Assert.Equal("AVISO_CITA_EXTERNA_SIN_CUBRIR", r.Template));
        Assert.Equal(world.Second.FamilyId, Assert.Single(rows).FamilyId);

        await Service(sender, Active()).ProcessDueAsync(Sim(Today, 11));
        var email = Assert.Single(sender.Sent.Where(m => m.To == world.Second.Address));
        Assert.Equal("Una cita en ResidApp todavía necesita ayuda", email.Subject);

        // Con todo lo de la familia cubierto (una persona se encarga del transporte y la otra del acompañante) no sale ninguno.
        var covered = await RegisterAsync(world, 2);
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(world.First, covered, FamilyArrangementAction.Take));
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(world.Second, covered, FamilyArrangementAction.Take, ExternalArrangementScope.Acompanante));
        await ReserveAsync(Today, 12);
        Assert.Empty(await RowsAsync(covered));
    }

    // ---- Lo que cambia entre reservar y enviar ----

    [Theory]
    [InlineData("alguien se encarga", "YA_CUBIERTA")]
    [InlineData("la cita se oculta", "FAMILIA_NO_VE")]
    [InlineData("el residente se restringe", "FAMILIA_NO_VE")]
    [InlineData("la persona se da de baja", "BAJA_DE_AVISOS")]
    [InlineData("pierde el acceso", "SIN_ACCESO")]
    [InlineData("la cuenta se suspende", "SIN_ACCESO")]
    [InlineData("se apagan los correos", "AVISOS_DESACTIVADOS")]
    [InlineData("la cita se cancela", "CITA_NO_VIGENTE")]
    public async Task ElAvisoSeVuelveAComprobarAlEnviar_YSiYaNoVale_QuedaOmitidoConSuMotivo_SinEnviarNada(string change, string expectedReason)
    {
        var world = await WorldAsync($"Residente Correo Cambio {Guid.NewGuid():N}"[..40]);
        var id = await RegisterAsync(world, 7);
        await ReserveAsync(Today, 11);
        var target = Assert.Single((await RowsAsync(id)).Where(r => r.FamilyId == world.First.FamilyId));
        Assert.Equal("PENDIENTE", target.State);

        switch (change)
        {
            case "alguien se encarga":
                Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(world.Second, id, FamilyArrangementAction.Take));
                break;
            case "la cita se oculta":
                await ExecuteAsync("UPDATE dbo.citas_externas SET visibilidad_familia_codigo = 'OCULTA', version = version + 1, modificada_en = SYSUTCDATETIME() WHERE id = @id", new { id });
                break;
            case "el residente se restringe":
                Assert.True((await Settings(world.Setup.Admin).SetResidentAsync(
                    new SetExternalFamilyResidentCommand(world.Setup.Admin.ProfileScopeId, world.Setup.Admin.CenterId, world.Setup.ResidentId, 0, true))).Ok);
                break;
            case "la persona se da de baja":
                Assert.True((await Settings(world.Setup.Admin).SetFamilyMemberMailAsync(new SetExternalFamilyMemberMailCommand(
                    world.Setup.Admin.ProfileScopeId, world.Setup.Admin.CenterId, world.Setup.ResidentId, world.First.FamilyId, 0, true))).Ok);
                break;
            case "pierde el acceso":
                await ChangeAsync(world.Setup.Admin, world.Setup.ResidentId, world.First.LinkId, FamilyAuthorizationChange.Revocar, 2, "Prueba");
                break;
            case "la cuenta se suspende":
                await ExecuteAsync("UPDATE dbo.cuentas SET estado = 'SUSPENDED' WHERE sujeto_externo = @subject", new { subject = world.First.Subject });
                break;
            case "se apagan los correos":
                Assert.True((await Settings(world.Setup.Admin).SetMailAsync(new SetExternalMailCommand(world.Setup.Admin.ProfileScopeId, world.Setup.Admin.CenterId, 1, false, false))).Ok);
                break;
            default:
                await ExecuteAsync("UPDATE dbo.citas_externas SET estado_codigo = 'CANCELADA', version = version + 1, modificada_en = SYSUTCDATETIME() WHERE id = @id", new { id });
                break;
        }

        var sender = new FakeEmailSender();
        await Service(sender, Active()).ProcessDueAsync(Sim(Today, 11));

        var after = Assert.Single((await RowsAsync(id)).Where(r => r.FamilyId == world.First.FamilyId));
        Assert.Equal(("OMITIDO", expectedReason), (after.State, after.Reason));
        Assert.DoesNotContain(sender.Sent, m => m.To == world.First.Address);
    }

    [Fact]
    public async Task FueraDeLaFranja_NoSeReservaNiSeEnvia_YLoReservadoSeOmiteComoFueraDePlazo()
    {
        var world = await WorldAsync("Residente Correo Franja");
        var id = await RegisterAsync(world, 7);
        Assert.Equal(0, await ReserveAsync(Today, 9));
        Assert.Equal(0, await ReserveAsync(Today, 19));
        Assert.Empty(await RowsAsync(id));

        await ReserveAsync(Today, 11);
        var sender = new FakeEmailSender();
        await Service(sender, Active()).ProcessDueAsync(Sim(Today, 19));
        Assert.All(await RowsAsync(id), r => Assert.Equal(("OMITIDO", "FUERA_DE_PLAZO"), (r.State, r.Reason)));
        Assert.DoesNotContain(sender.Sent, m => m.To == world.First.Address || m.To == world.Second.Address);
    }

    [Fact]
    public async Task SinDireccionConfirmada_NoSeReserva_YConLosCorreosDelCentroApagados_NoSeReservaNada()
    {
        var world = await WorldAsync("Residente Correo Sin Direccion");
        var id = await RegisterAsync(world, 7);
        await ExecuteAsync(
            "UPDATE dbo.familiares SET correo_confirmado_en = NULL, correo_confirmado_por_cuenta_id = NULL WHERE id = @family", new { family = world.Second.FamilyId });

        await ReserveAsync(Today, 11);
        Assert.Equal(world.First.FamilyId, Assert.Single(await RowsAsync(id)).FamilyId);

        var off = await WorldAsync("Residente Correo Apagado", mailOn: false);
        var other = await RegisterAsync(off, 7);
        await ReserveAsync(Today, 11);
        Assert.Empty(await RowsAsync(other));
    }

    // ---- El envío ----

    [Fact]
    public async Task ConLaListaDeDestinosPermitidos_SoloSeEscribeAUnaDireccionPermitida_YConElCorreoApagadoNoSeHaceNada()
    {
        var world = await WorldAsync("Residente Correo Permitidos");
        var id = await RegisterAsync(world, 7);
        await ReserveAsync(Today, 11);

        var disabled = new FakeEmailSender();
        Assert.Equal(0, await Service(disabled, EmailSettings.Off).ProcessDueAsync(Sim(Today, 11)));
        Assert.Equal(0, await Service(disabled, Active() with { ExternalNotice = false }).ProcessDueAsync(Sim(Today, 11)));
        Assert.Empty(disabled.Sent);
        Assert.All(await RowsAsync(id), r => Assert.Equal(("PENDIENTE", 0), (r.State, r.Attempts)));

        var sender = new FakeEmailSender();
        await Service(sender, Active(false, world.First.Address)).ProcessDueAsync(Sim(Today, 11));
        var rows = await RowsAsync(id);
        Assert.Equal("ENVIADO", rows.Single(r => r.FamilyId == world.First.FamilyId).State);
        var refused = rows.Single(r => r.FamilyId == world.Second.FamilyId);
        Assert.Equal(("OMITIDO", "DESTINO_NO_PERMITIDO"), (refused.State, refused.Reason));
        Assert.Equal(world.First.Address, Assert.Single(sender.Sent.Where(m => m.To == world.First.Address || m.To == world.Second.Address)).To);
    }

    [Fact]
    public async Task DosCopiasDeLaAplicacionAlaVez_ReclamanCadaAvisoUnaSolaVez_YNoSalenCorreosRepetidos()
    {
        var world = await WorldAsync("Residente Correo Dos Copias");
        var id = await RegisterAsync(world, 7);
        await ReserveAsync(Today, 11);
        var sender = new FakeEmailSender();

        await Task.WhenAll(
            Task.Run(() => Service(sender, Active()).ProcessDueAsync(Sim(Today, 11))),
            Task.Run(() => Service(sender, Active()).ProcessDueAsync(Sim(Today, 11))));

        Assert.Equal(1, sender.Sent.Count(m => m.To == world.First.Address));
        Assert.Equal(1, sender.Sent.Count(m => m.To == world.Second.Address));
        Assert.All(await RowsAsync(id), r => Assert.Equal("ENVIADO", r.State));
    }

    [Fact]
    public async Task UnAvisoQueNuncaSeReclamo_ElCierreDeAvisosLoOmiteComoFueraDePlazo()
    {
        var world = await WorldAsync("Residente Correo Cierre");
        var id = await RegisterAsync(world, 7);
        // La fila ya nace con más de 24 horas (la identidad de una fila reservada no se puede cambiar después).
        await ExecuteAsync("""
            INSERT INTO dbo.envios_correo (id, centro_id, plantilla_codigo, plantilla_version, idioma, familiar_id, referencia_id, referencia_clave, estado_codigo, intentos, creado_en)
            VALUES (NEWID(), @center, 'AVISO_CITA_EXTERNA', '1', 'es', @family, @id, '7D|2030-01-01', 'PENDIENTE', 0, DATEADD(HOUR, -26, SYSUTCDATETIME()))
            """, new { center = world.Setup.Admin.CenterId.Value, family = world.First.FamilyId, id });

        await ExecuteAsync("EXEC dbo.cerrar_envios_correo_sin_resolver");

        Assert.All(await RowsAsync(id), r => Assert.Equal(("OMITIDO", "FUERA_DE_PLAZO"), (r.State, r.Reason)));
    }

    // ---- Lo que el rol de la tarea puede y no puede ----

    private const string TaskUser = "residapp_tareas_ext_test";

    private static readonly Lazy<Task> TaskUserCreated = new(async () =>
    {
        using var admin = await TestDatabase.ConnectionFactory.OpenAsync();
        await admin.ExecuteAsync($"""
            IF DATABASE_PRINCIPAL_ID(N'{TaskUser}') IS NULL CREATE USER {TaskUser} WITHOUT LOGIN;
            IF IS_ROLEMEMBER(N'residapp_tareas', N'{TaskUser}') = 0 ALTER ROLE residapp_tareas ADD MEMBER {TaskUser};
            """);
    });

    private sealed class NoTenant : ITenantContext
    {
        public Task<TenantScope?> GetAsync(CancellationToken ct = default) => Task.FromResult<TenantScope?>(null);
    }

    private static async Task<SqlConnection> OpenAsTaskAsync()
    {
        await TaskUserCreated.Value;
        var impersonable = new SqlConnectionStringBuilder(TestDatabase.ConnectionString) { Pooling = false, MultipleActiveResultSets = false }.ConnectionString;
        var connection = await new SqlConnectionFactory(impersonable, new NoTenant()).OpenAsync();
        await connection.ExecuteAsync($"EXECUTE AS USER = N'{TaskUser}'");
        return connection;
    }

    [Fact]
    public async Task ElRolDeLaTarea_ReservaYComprueba_PeroNoLeeLaCitaNiLosEncargosNiInserta()
    {
        var world = await WorldAsync("Residente Correo Rol Tarea");
        var id = await RegisterAsync(world, 7);
        using var connection = await OpenAsTaskAsync();

        // Lo que necesita, con sus procedimientos y sus lecturas de siempre.
        await connection.ExecuteScalarAsync<int>(SqlExternalNoticeStore.ReserveSql, new { language = "es", version = "1" });
        var notice = await connection.QueryFirstAsync<Guid>("SELECT TOP (1) id FROM dbo.envios_correo WHERE referencia_id = @id", new { id });
        var check = await connection.QuerySingleAsync<(string? Reason, Guid? AccountId, Guid? LinkId, DateTime? Date)>(SqlExternalNoticeStore.CheckSql, new { id = notice });
        Assert.Null(check.Reason);
        Assert.NotNull(check.AccountId);
        Assert.NotNull(check.LinkId);

        // Lo que no: los datos de la cita, los encargos y los «No puedo» (se leen solo por la base), y escribir en la tabla de envíos.
        foreach (var sql in new[]
        {
            "SELECT lugar FROM dbo.citas_externas",
            "SELECT especialidad_codigo FROM dbo.citas_externas",
            "SELECT preparar_texto FROM dbo.citas_externas",
            "SELECT * FROM dbo.citas_externas_encargos",
            "SELECT * FROM dbo.citas_externas_no_puedo",
            "SELECT * FROM dbo.citas_externas_correo_familiar",
            "INSERT INTO dbo.citas_externas_encargos (id) VALUES (NEWID())",
        })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql));
            Assert.Equal(229, error.Number); // permiso denegado
        }

        var insert = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "INSERT INTO dbo.envios_correo (id, centro_id, plantilla_codigo, plantilla_version, idioma, familiar_id, referencia_id, referencia_clave, estado_codigo, intentos, creado_en) " +
            "SELECT NEWID(), centro_id, 'AVISO_CITA_EXTERNA', '1', 'es', familiar_id, referencia_id, 'X', 'PENDIENTE', 0, SYSUTCDATETIME() FROM dbo.envios_correo"));
        Assert.Equal(229, insert.Number);
    }
}

file sealed class FixedMailSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
