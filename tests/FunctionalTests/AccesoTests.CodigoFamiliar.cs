using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ResidApp.Application.Ports;
using ResidApp.Infrastructure.Identity;
using ResidApp.Infrastructure.Persistence;
using ResidApp.Web.Controllers;

namespace ResidApp.FunctionalTests;

/// <summary>
/// El código de acceso por correo de la familia con la app entera (ADR 0012, pieza 3; docs/privacidad/codigo-acceso-familiar-por-correo.md, C1 a C15): con el código encendido no
/// hay sesión sin un código válido, ni con la contraseña ni con una invitación; todo fallo cierra; los pasos de familia y de personal no se cruzan; y, apagado fuera de un
/// entorno de desarrollo, la familia no entra. El correo lo recoge un proveedor falso.
/// </summary>
public partial class AccesoTests
{
    private static readonly Uri HttpsBase = new("https://localhost");

    private sealed class Mailbox : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public string? FailWith { get; set; }

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.FromResult(FailWith is { } failure ? EmailSendResult.Fail(failure) : EmailSendResult.Ok("op"));
        }

        public string LastCode => Regex.Match(Sent[^1].PlainText, @"^Tu código es: (\d{6})\n").Groups[1].Value;

        /// <summary>Un código que seguro no es el bueno.</summary>
        public string WrongCode => LastCode == "000000" ? "000001" : "000000";
    }

    private WebApplicationFactory<Program> WithFamilyCode(Mailbox mailbox, TimeProvider? clock = null, Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Acceso:CodigoFamiliarActivo"] = "true",
            ["Acceso:CodigoFamiliarClave"] = new string('k', 40),
            ["Correo:Activo"] = "true",
            ["Correo:CadenaConexion"] = "endpoint=https://correo.example.test/;accesskey=AAAA",
            ["Correo:UrlBase"] = "https://localhost",
            ["Correo:TodosLosDestinos"] = "true",
        };
        foreach (var (name, value) in overrides ?? [])
        {
            settings[name] = value;
        }

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IEmailSender>(_ => mailbox);
                if (clock is not null)
                {
                    services.AddSingleton(clock);
                }
            });
        });
    }

    /// <summary>La ficha del familiar de esta cuenta en el centro de su ámbito, con ese correo.</summary>
    private static async Task AddFamilyRecordAsync(Guid accountId, string? email)
    {
        using var connection = await new SqlConnectionFactory(ResidentsFlowTests.WebAppFactory.TestConnectionString).OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.familiares (id, centro_id, nombre_visible, telefono, correo, creado_por_cuenta_id, creado_en, cuenta_id)
            SELECT NEWID(), centro_id, N'Familiar de prueba', N'600000000', @email, @accountId, SYSUTCDATETIME(), @accountId
              FROM dbo.ambitos_perfil WHERE cuenta_id = @accountId
            """, new { accountId, email });
    }

    private async Task<(string Subject, Guid AccountId, string Email)> SeedFamilyWithEmailAsync()
    {
        var (subject, accountId) = await SeedWithPasswordAsync("FAMILIAR");
        var email = $"{subject}@example.test";
        await AddFamilyRecordAsync(accountId, email);
        return (subject, accountId, email);
    }

    private static Task<HttpResponseMessage> TypeCodeAsync(HttpClient client, string code) =>
        PostAsync(client, "/Acceso/CodigoCorreo", new() { ["codigo"] = code }, "/Acceso/CodigoCorreo");

    private static Task<HttpResponseMessage> ResendAsync(HttpClient client) =>
        PostAsync(client, "/Acceso/ReenviarCodigo", [], "/Acceso/CodigoCorreo");

    /// <summary>El texto de la pantalla a la que lleva una redirección.</summary>
    private static async Task<string> FollowAsync(HttpClient client, HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return await TextAsync(await client.GetAsync(response.Headers.Location));
    }

    private static async Task AssertGoesToCodeAsync(HttpClient client, HttpResponseMessage response)
    {
        Assert.Equal("/Acceso/CodigoCorreo", response.Headers.Location!.OriginalString);
        Assert.False(OpensSession(response));
        Assert.True(SesionTests.RedirectsToLogin(await client.GetAsync("/ProfileScope/Select")));
    }

    [Fact]
    public async Task Familia_ConElCodigoEncendido_SoloEntraConElCodigoDelCorreo_YUnaVez()
    {
        var (subject, accountId, email) = await SeedFamilyWithEmailAsync();
        var mailbox = new Mailbox();
        var client = Client(WithFamilyCode(mailbox));

        var entered = await EnterAsync(client, subject, Password);

        await AssertGoesToCodeAsync(client, entered);
        Assert.Equal(email, Assert.Single(mailbox.Sent).To);
        var screen = await client.GetAsync("/Acceso/CodigoCorreo");
        var text = await TextAsync(screen);
        Assert.Contains("no-store", screen.Headers.CacheControl!.ToString());
        Assert.Contains("Enviar otro código", text);
        Assert.DoesNotContain(email, text);
        Assert.DoesNotContain(mailbox.LastCode, text);
        Assert.Equal(0, await EventsAsync(accountId, "ENTRADA"));

        var token = SesionTests.ExtractValue(await screen.Content.ReadAsStringAsync(), "__RequestVerificationToken");
        var done = await TypeCodeAsync(client, $" {mailbox.LastCode[..3]} {mailbox.LastCode[3..]} ");

        Assert.Equal("/", done.Headers.Location!.OriginalString);
        Assert.True(OpensSession(done));
        Assert.Equal(1, await EventsAsync(accountId, "ENTRADA"));
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.codigos_acceso_correo WHERE cuenta_id = @accountId AND resumen IS NOT NULL", new { accountId }));
        // El paso ya se gastó: el mismo código no abre otra sesión.
        var again = await client.PostAsync("/Acceso/CodigoCorreo", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["codigo"] = mailbox.LastCode }));
        Assert.False(OpensSession(again));
    }

    [Fact]
    public async Task Familia_LaContrasenaCorrecta_NoPoneACeroLosFallosDelCodigo()
    {
        var (subject, accountId, _) = await SeedFamilyWithEmailAsync();
        var mailbox = new Mailbox();
        var client = Client(WithFamilyCode(mailbox));
        await EnterAsync(client, subject, Password);

        for (var i = 0; i < 4; i++)
        {
            Assert.Contains(AccesoController.WrongCodeMessage, await FollowAsync(client, await TypeCodeAsync(client, mailbox.WrongCode)));
        }
        // La contraseña otra vez, dentro del minuto: no sale otro código y el que había sigue siendo el de este navegador.
        Assert.Contains(AccesoController.CodeTooSoonMessage, await FollowAsync(client, await EnterAsync(client, subject, Password)));
        var fifth = await TypeCodeAsync(client, mailbox.WrongCode);

        Assert.Contains("la cuenta queda bloqueada 15 minutos", await FollowAsync(client, fifth));
        Assert.Equal(1, await EventsAsync(accountId, "BLOQUEO"));
        Assert.Equal(5, await EventsAsync(accountId, "SEGUNDO_FACTOR_FALLIDO"));
        Assert.Single(mailbox.Sent);
        var locked = await EnterAsync(client, subject, Password);
        Assert.Contains(AccesoController.FailedMessage, await TextAsync(locked));
    }

    [Fact]
    public async Task Familia_ElCodigoEsDeSuNavegador_YPedirOtroAnulaElAnterior()
    {
        var (subject, _, _) = await SeedFamilyWithEmailAsync();
        var mailbox = new Mailbox();
        var clock = new TestClock();
        var app = WithFamilyCode(mailbox, clock);
        var first = Client(app);
        var second = Client(app);
        await EnterAsync(first, subject, Password);
        var firstCode = mailbox.LastCode;

        await EnterAsync(second, subject, Password);
        Assert.Contains(AccesoController.DeadCodeMessage, await FollowAsync(second, await TypeCodeAsync(second, firstCode)));

        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Contains(AccesoController.CodeSentMessage, await FollowAsync(second, await ResendAsync(second)));
        Assert.Equal(2, mailbox.Sent.Count);
        Assert.Contains(AccesoController.DeadCodeMessage, await FollowAsync(first, await TypeCodeAsync(first, firstCode)));
        Assert.True(OpensSession(await TypeCodeAsync(second, mailbox.LastCode)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Familia_SinCorreoEnLaFicha_NoEntra(bool withRecord)
    {
        var (subject, accountId) = await SeedWithPasswordAsync("FAMILIAR");
        if (withRecord)
        {
            await AddFamilyRecordAsync(accountId, null);
        }
        var mailbox = new Mailbox();
        var client = Client(WithFamilyCode(mailbox));

        var response = await EnterAsync(client, subject, Password);

        Assert.False(OpensSession(response));
        Assert.Contains(AccesoController.FamilyNoAddressMessage, await FollowAsync(client, response));
        Assert.Empty(mailbox.Sent);
    }

    [Fact]
    public async Task Familia_SiElCorreoNoSale_NoEntra_YSeLeDice()
    {
        var (subject, _, _) = await SeedFamilyWithEmailAsync();
        var mailbox = new Mailbox { FailWith = EmailLog.ProviderRejected };
        var client = Client(WithFamilyCode(mailbox));

        var response = await EnterAsync(client, subject, Password);

        await AssertGoesToCodeAsync(client, response);
        Assert.Contains(AccesoController.CodeNotSentMessage, await FollowAsync(client, response));
        Assert.Contains(AccesoController.DeadCodeMessage, await FollowAsync(client, await TypeCodeAsync(client, mailbox.LastCode)));
    }

    [Theory]
    [InlineData("Correo:Activo", "false")]
    [InlineData("Acceso:CodigoFamiliarClave", "corta")]
    public async Task Familia_ConElCodigoEncendidoYSinLoQueNecesita_NoEntra(string setting, string value)
    {
        var (subject, _, _) = await SeedFamilyWithEmailAsync();
        var mailbox = new Mailbox();
        var client = Client(WithFamilyCode(mailbox, overrides: new() { [setting] = value }));

        var response = await EnterAsync(client, subject, Password);

        Assert.False(OpensSession(response));
        Assert.Contains(AccesoController.FamilyClosedMessage, await FollowAsync(client, response));
        Assert.Empty(mailbox.Sent);
    }

    [Fact]
    public async Task Familia_SiCambiaLaDireccionOLaContrasena_ElPasoDejaDeValer()
    {
        var (subject, accountId, _) = await SeedFamilyWithEmailAsync();
        var mailbox = new Mailbox();
        var app = WithFamilyCode(mailbox);
        var client = Client(app);
        await EnterAsync(client, subject, Password);

        using (var connection = await new SqlConnectionFactory(ResidentsFlowTests.WebAppFactory.TestConnectionString).OpenAsync())
        {
            await connection.ExecuteAsync("UPDATE dbo.familiares SET correo = N'nueva@example.test' WHERE cuenta_id = @accountId", new { accountId });
        }
        // La dirección forma parte del resumen: el código mandado a la anterior ya no es el bueno.
        var oldCode = await TypeCodeAsync(client, mailbox.LastCode);
        Assert.False(OpensSession(oldCode));
        Assert.Contains(AccesoController.WrongCodeMessage, await FollowAsync(client, oldCode));

        var screen = await client.GetStringAsync("/Acceso/CodigoCorreo");
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<CredentialAccount>>();
            await users.UpdateSecurityStampAsync((await users.FindByNameAsync(subject))!);
        }
        var response = await client.PostAsync("/Acceso/CodigoCorreo", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = SesionTests.ExtractValue(screen, "__RequestVerificationToken"), ["codigo"] = mailbox.LastCode,
        }));

        Assert.False(OpensSession(response));
        Assert.Equal("/Acceso/Entrar", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task LosPasosDeFamiliaYDePersonal_NoSeCruzan_YElPersonalNoRecibeCorreo()
    {
        var (family, _, _) = await SeedFamilyWithEmailAsync();
        var (staff, _, _) = await SeedWithTwoFactorAsync();
        var mailbox = new Mailbox();
        var app = WithFamilyCode(mailbox);
        var familyClient = Client(app);
        var staffClient = Client(app);
        await EnterAsync(familyClient, family, Password);

        var staffEntered = await EnterAsync(staffClient, staff, Password);

        Assert.Equal("/Acceso/SegundoFactor", staffEntered.Headers.Location!.OriginalString);
        Assert.Single(mailbox.Sent);
        foreach (var path in new[] { "/Acceso/SegundoFactor", "/Acceso/CodigoRecuperacion", "/Acceso/ActivarSegundoFactor" })
        {
            Assert.Equal("/Acceso/Entrar", (await Client(app).GetAsync(path)).Headers.Location!.OriginalString);
        }
        // Cada intento en la pantalla ajena gasta el paso, así que se empieza con uno nuevo cada vez.
        Assert.Equal("/Acceso/Entrar", (await staffClient.GetAsync("/Acceso/CodigoCorreo")).Headers.Location!.OriginalString);
        Assert.Equal("/Acceso/Entrar", (await familyClient.GetAsync("/Acceso/SegundoFactor")).Headers.Location!.OriginalString);
        var other = Client(app);
        await EnterAsync(other, family, Password);
        Assert.Equal("/Acceso/Entrar", (await other.GetAsync("/Acceso/ActivarSegundoFactor")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Familia_LaInvitacionTambienPideElCodigo_YQuitaElBloqueo()
    {
        var mailbox = new Mailbox();
        var app = WithFamilyCode(mailbox);
        var (admin, _, adminId) = await AdministrationAsync(app);
        var (subject, accountId) = await SeedInCenterAsync(adminId, "FAMILIAR");
        await AddFamilyRecordAsync(accountId, "invitada@example.test");
        var sheet = await TextAsync(await IssueAsync(admin, subject, "/Administracion/Usuarios"));
        using (var connection = await new SqlConnectionFactory(ResidentsFlowTests.WebAppFactory.TestConnectionString).OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.credenciales (cuenta_id, actualizada_en, codigo_correo_bloqueado_en) VALUES (@accountId, SYSUTCDATETIME(), SYSUTCDATETIME())
                """, new { accountId });
        }
        var client = Client(app);

        var chosen = await ChoosePasswordAsync(client, InvitationUrl(sheet));

        await AssertGoesToCodeAsync(client, chosen);
        Assert.Equal("invitada@example.test", Assert.Single(mailbox.Sent).To);
        Assert.True(OpensSession(await TypeCodeAsync(client, mailbox.LastCode)));
    }

    [Fact]
    public async Task FueraDeDesarrollo_ConElCodigoApagado_LaFamiliaNoEntra_NiConInvitacion_YElPersonalSigueIgual()
    {
        var app = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Staging"));
        HttpClient Https() => app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = HttpsBase });
        var (family, familyId, _) = await SeedFamilyWithEmailAsync();
        var (staff, _, _) = await SeedWithTwoFactorAsync();
        var client = Https();

        var response = await EnterAsync(client, family, Password);

        Assert.False(OpensSession(response));
        Assert.Contains(AccesoController.FamilyClosedMessage, await FollowAsync(client, response));
        Assert.Equal(0, await EventsAsync(familyId, "ENTRADA"));
        Assert.Equal("/Acceso/SegundoFactor", (await EnterAsync(Https(), staff, Password)).Headers.Location!.OriginalString);

        var (adminSubject, adminId) = await SesionTests.SeedAccountAsync("ADMINISTRACION");
        var admin = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = HttpsBase });
        (await SesionTests.PostLoginAsync(admin, adminSubject)).EnsureSuccessStatusCode();
        var (invited, _) = await SeedInCenterAsync(adminId, "FAMILIAR");
        var sheet = await TextAsync(await IssueAsync(admin, invited, "/Administracion/Usuarios"));
        var guest = Https();

        var chosen = await ChoosePasswordAsync(guest, InvitationUrl(sheet));

        Assert.False(OpensSession(chosen));
        Assert.Contains("Tu contraseña está guardada. " + AccesoController.FamilyClosedMessage, await FollowAsync(guest, chosen));
    }
}
