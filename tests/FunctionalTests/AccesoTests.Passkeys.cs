using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using ResidApp.Web.Controllers;

namespace ResidApp.FunctionalTests;

/// <summary>
/// ADR 0007, incremento I4, passkeys: se registran desde «Mi cuenta» pidiendo contraseña y código del móvil, entran sin contraseña, el estado
/// de cada ceremonia vale una vez y solo para quien lo pidió, y una cuenta de Plataforma con passkey ya no entra con contraseña. Un
/// autenticador de pruebas hace de dispositivo.
/// </summary>
public partial class AccesoTests
{
    private static async Task<HttpResponseMessage> JsonPostAsync(HttpClient client, string path, string? json, string tokenPage)
    {
        var token = SesionTests.ExtractValue(await client.GetStringAsync(tokenPage), "__RequestVerificationToken");
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("RequestVerificationToken", token);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }
        return await client.SendAsync(request);
    }

    /// <summary>Primer paso del alta, con la contraseña y el código del móvil de la cuenta.</summary>
    private async Task<HttpResponseMessage> PasskeyOptionsAsync(HttpClient client, Guid accountId, string password = Password, string? code = null) =>
        await PostAsync(client, "/Cuenta/PasskeyOpciones", new()
        {
            ["contrasena"] = password, ["codigo"] = code ?? Totp.Code(await KeyAsync(accountId)),
        }, "/Cuenta");

    private static Task<HttpResponseMessage> PasskeyRegisterAsync(HttpClient client, string credentialJson, string name = "Móvil de prueba") =>
        JsonPostAsync(client, "/Cuenta/PasskeyAlta", JsonSerializer.Serialize(new { credentialJson, nombre = name }), "/Cuenta");

    /// <summary>Alta completa de una passkey de la cuenta con sesión; devuelve las opciones de creación (llevan el identificador de usuario).</summary>
    private async Task<string> RegisterPasskeyAsync(HttpClient client, Guid accountId, SoftwareAuthenticator authenticator, string name = "Móvil de prueba")
    {
        var options = await PasskeyOptionsAsync(client, accountId);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var json = await options.Content.ReadAsStringAsync();
        var registered = await PasskeyRegisterAsync(client, authenticator.Create(json), name);
        Assert.True(registered.StatusCode == HttpStatusCode.OK, await registered.Content.ReadAsStringAsync());
        return json;
    }

    private static async Task<HttpResponseMessage> PasskeyEnterAsync(HttpClient client, SoftwareAuthenticator authenticator, string userHandle, string? returnUrl = null)
    {
        var options = await JsonPostAsync(client, "/Passkey/OpcionesEntrada", null, "/Acceso/Entrar");
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var assertion = authenticator.Sign(await options.Content.ReadAsStringAsync(), userHandle);
        return await JsonPostAsync(client, "/Passkey/Entrar", JsonSerializer.Serialize(new { credentialJson = assertion, returnUrl }), "/Acceso/Entrar");
    }

    private static Task<int> PasskeysAsync(Guid accountId) =>
        ScalarAsync<int>("SELECT COUNT(*) FROM dbo.passkeys WHERE cuenta_id = @accountId", new { accountId });

    [Fact]
    public async Task Passkey_SeRegistraDesdeMiCuenta_YPermiteEntrarSinContrasena()
    {
        var (subject, accountId, _) = await SeedWithTwoFactorAsync();
        var authenticator = new SoftwareAuthenticator();
        var signedIn = await SignedInWithTotpAsync(subject, accountId);

        var creation = await RegisterPasskeyAsync(signedIn, accountId, authenticator, "Portátil de Ana");
        var page = await TextAsync(await signedIn.GetAsync("/Cuenta"));
        var anonymous = Client();
        var entry = await PasskeyEnterAsync(anonymous, authenticator, SoftwareAuthenticator.UserHandle(creation), "/Cuenta");

        Assert.Equal(1, await PasskeysAsync(accountId));
        Assert.Equal(1, await EventsAsync(accountId, "PASSKEY_ALTA"));
        Assert.Contains("Portátil de Ana", page);
        Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
        Assert.True(OpensSession(entry));
        Assert.Equal("/Cuenta", JsonDocument.Parse(await entry.Content.ReadAsStringAsync()).RootElement.GetProperty("redirect").GetString());
        Assert.Equal(1, await EventsAsync(accountId, "ENTRADA_PASSKEY"));
        Assert.Equal(1L, await ScalarAsync<long>("SELECT contador_firmas FROM dbo.passkeys WHERE cuenta_id = @accountId", new { accountId }));
        Assert.False(SesionTests.RedirectsToLogin(await anonymous.GetAsync("/ProfileScope/Select")));
    }

    [Fact]
    public async Task Alta_ConLaContrasenaOElCodigoMalos_NoDaOpciones_YCuentaParaElBloqueo()
    {
        var (subject, accountId, _) = await SeedWithTwoFactorAsync();
        var signedIn = await SignedInWithTotpAsync(subject, accountId);

        var wrongPassword = await PasskeyOptionsAsync(signedIn, accountId, "no es la contraseña");
        var wrongCode = await PasskeyOptionsAsync(signedIn, accountId, code: "000000");

        Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongCode.StatusCode);
        Assert.False(wrongPassword.Headers.Contains("Set-Cookie") && wrongPassword.Headers.GetValues("Set-Cookie").Any(c => c.StartsWith("residapp_passkey=")));
        Assert.Equal(1, await EventsAsync(accountId, "CONTRASENA_FALLIDA"));
        Assert.Equal(1, await EventsAsync(accountId, "SEGUNDO_FACTOR_FALLIDO"));
        Assert.Equal(0, await PasskeysAsync(accountId));
    }

    [Fact]
    public async Task Alta_SoloParaElPersonalConCodigoDelMovil()
    {
        var (subject, accountId) = await SeedWithPasswordAsync("FAMILIAR");
        var signedIn = await SignedInAsync(subject);

        var response = await PostAsync(signedIn, "/Cuenta/PasskeyOpciones", new() { ["contrasena"] = Password, ["codigo"] = "123456" }, "/Cuenta");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await PasskeysAsync(accountId));
    }

    [Fact]
    public async Task Alta_ElEstadoValeUnaVez()
    {
        var (subject, accountId, _) = await SeedWithTwoFactorAsync();
        var signedIn = await SignedInWithTotpAsync(subject, accountId);

        var options = await PasskeyOptionsAsync(signedIn, accountId);
        var credential = new SoftwareAuthenticator().Create(await options.Content.ReadAsStringAsync());
        var first = await PasskeyRegisterAsync(signedIn, credential);
        var again = await PasskeyRegisterAsync(signedIn, credential);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(1, await PasskeysAsync(accountId));
    }

    [Fact]
    public async Task Alta_ElEstadoNoSirveEnOtraSesionDeLaMismaCuenta()
    {
        var (subject, accountId, _) = await SeedWithTwoFactorAsync();
        var client = await SignedInWithTotpAsync(subject, accountId);

        var options = await PasskeyOptionsAsync(client, accountId);
        var credential = new SoftwareAuthenticator().Create(await options.Content.ReadAsStringAsync());
        // La misma cuenta entra de nuevo desde este navegador: sesión nueva, y el estado era de la anterior.
        await EnterAsync(client, subject, Password);
        await PostAsync(client, "/Acceso/SegundoFactor", new() { ["codigo"] = Totp.Code(await KeyAsync(accountId)) }, "/Acceso/SegundoFactor");
        var late = await PasskeyRegisterAsync(client, credential);

        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
        Assert.Equal(0, await PasskeysAsync(accountId));
    }

    private async Task<string> SessionCookieOfAsync(string subject, Guid accountId)
    {
        var client = Client();
        await EnterAsync(client, subject, Password);
        var response = await PostAsync(client, "/Acceso/SegundoFactor", new() { ["codigo"] = Totp.Code(await KeyAsync(accountId)) }, "/Acceso/SegundoFactor");
        return SesionTests.SessionCookie(response);
    }

    private HttpClient ClientWithCookies(params string[] cookies)
    {
        var client = Client();
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies));
        return client;
    }

    private static string StateCookie(HttpResponseMessage options) =>
        options.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("residapp_passkey=")).Split(';')[0];

    [Fact]
    public async Task Alta_ElEstadoNoSirveParaOtraCuenta()
    {
        var (subjectA, accountA, _) = await SeedWithTwoFactorAsync();
        var (subjectB, accountB, _) = await SeedWithTwoFactorAsync();
        var sessionA = await SessionCookieOfAsync(subjectA, accountA);
        var sessionB = await SessionCookieOfAsync(subjectB, accountB);

        // Control: con las cookies a mano, la propia cuenta sí completa el alta.
        var controlOptions = await PasskeyOptionsAsync(ClientWithCookies(sessionA), accountA);
        var control = await PasskeyRegisterAsync(ClientWithCookies(sessionA, StateCookie(controlOptions)),
            new SoftwareAuthenticator().Create(await controlOptions.Content.ReadAsStringAsync()));
        // B presenta el estado que se pidió para A.
        var optionsA = await PasskeyOptionsAsync(ClientWithCookies(sessionA), accountA);
        var stolen = await PasskeyRegisterAsync(ClientWithCookies(sessionB, StateCookie(optionsA)),
            new SoftwareAuthenticator().Create(await optionsA.Content.ReadAsStringAsync()));

        Assert.True(control.StatusCode == HttpStatusCode.OK, await control.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, stolen.StatusCode);
        Assert.Equal(1, await PasskeysAsync(accountA));
        Assert.Equal(0, await PasskeysAsync(accountB));
    }

    [Fact]
    public async Task Alta_ConUnRetoDistinto_OOtroOrigen_SeRechaza()
    {
        var (subject, accountId, _) = await SeedWithTwoFactorAsync();
        var signedIn = await SignedInWithTotpAsync(subject, accountId);

        var wrongChallenge = await PasskeyOptionsAsync(signedIn, accountId);
        var withOtherChallenge = await PasskeyRegisterAsync(signedIn,
            new SoftwareAuthenticator().Create(await wrongChallenge.Content.ReadAsStringAsync(), challengeOverride: "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
        // Otro sitio, un dominio que solo acaba igual, uno que lo contiene, y un subdominio sin HTTPS.
        var fromOtherSites = new List<HttpResponseMessage>();
        foreach (var origin in new[] { "https://sitio-falso.example", "https://notlocalhost", "https://localhost.sitio-falso.example", "http://app.localhost" })
        {
            var options = await PasskeyOptionsAsync(signedIn, accountId);
            fromOtherSites.Add(await PasskeyRegisterAsync(signedIn,
                new SoftwareAuthenticator(origin: origin).Create(await options.Content.ReadAsStringAsync())));
        }
        var wrongDomain = await PasskeyOptionsAsync(signedIn, accountId);
        var forOtherDomain = await PasskeyRegisterAsync(signedIn,
            new SoftwareAuthenticator(rpId: "sitio-falso.example").Create(await wrongDomain.Content.ReadAsStringAsync()));

        Assert.Equal(HttpStatusCode.BadRequest, withOtherChallenge.StatusCode);
        Assert.All(fromOtherSites, response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        Assert.Equal(HttpStatusCode.BadRequest, forOtherDomain.StatusCode);
        Assert.Equal(0, await PasskeysAsync(accountId));
    }

    [Fact]
    public async Task Alta_AceptaUnSubdominioDelDominioConfigurado_PorHttps()
    {
        var (subject, accountId, _) = await SeedWithTwoFactorAsync();
        var signedIn = await SignedInWithTotpAsync(subject, accountId);

        var options = await PasskeyOptionsAsync(signedIn, accountId);
        var registered = await PasskeyRegisterAsync(signedIn,
            new SoftwareAuthenticator(origin: "https://app.localhost").Create(await options.Content.ReadAsStringAsync()));

        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        Assert.Equal(1, await PasskeysAsync(accountId));
    }

    /// <summary>Una cuenta del personal con una passkey ya registrada y el cliente de su sesión.</summary>
    private async Task<(string Subject, Guid AccountId, SoftwareAuthenticator Authenticator, string UserHandle)> SeedWithPasskeyAsync()
    {
        var (subject, accountId, _) = await SeedWithTwoFactorAsync();
        var authenticator = new SoftwareAuthenticator();
        var creation = await RegisterPasskeyAsync(await SignedInWithTotpAsync(subject, accountId), accountId, authenticator);
        return (subject, accountId, authenticator, SoftwareAuthenticator.UserHandle(creation));
    }

    [Fact]
    public async Task Entrada_ElEstadoValeUnaVez_YUnRetoAjenoNoAbreSesion()
    {
        var (_, accountId, authenticator, userHandle) = await SeedWithPasskeyAsync();
        var client = Client();

        var options = await JsonPostAsync(client, "/Passkey/OpcionesEntrada", null, "/Acceso/Entrar");
        var optionsJson = await options.Content.ReadAsStringAsync();
        var foreign = await JsonPostAsync(client, "/Passkey/Entrar", JsonSerializer.Serialize(new
        {
            credentialJson = authenticator.Sign(optionsJson, userHandle, challengeOverride: "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"),
        }), "/Acceso/Entrar");
        // El intento fallido gastó el estado: ni siquiera la firma correcta vale ya.
        var replay = await JsonPostAsync(client, "/Passkey/Entrar", JsonSerializer.Serialize(new
        {
            credentialJson = authenticator.Sign(optionsJson, userHandle),
        }), "/Acceso/Entrar");

        Assert.Equal(HttpStatusCode.Unauthorized, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Contains(PasskeyController.FailedMessage, await replay.Content.ReadAsStringAsync());
        Assert.False(OpensSession(foreign));
        Assert.False(OpensSession(replay));
        Assert.Equal(0, await EventsAsync(accountId, "ENTRADA_PASSKEY"));
    }

    [Fact]
    public async Task Entrada_ConUnaPasskeyQueNoEstaRegistrada_NoAbreSesion()
    {
        var (_, _, _, userHandle) = await SeedWithPasskeyAsync();

        var response = await PasskeyEnterAsync(Client(), new SoftwareAuthenticator(), userHandle);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(OpensSession(response));
    }

    [Fact]
    public async Task Entrada_CuentaSuspendidaOSinAmbito_NoAbreSesion_AunqueLaPasskeyEsteBien()
    {
        var suspended = await SeedWithPasskeyAsync();
        var withoutScope = await SeedWithPasskeyAsync();
        await ScalarAsync<int>("UPDATE dbo.cuentas SET estado = 'SUSPENDED' WHERE id = @AccountId; SELECT 1", new { suspended.AccountId });
        await ScalarAsync<int>("UPDATE dbo.ambitos_perfil SET estado = 'REVOKED', revocado_en = SYSUTCDATETIME(), revocado_por_cuenta_id = cuenta_id WHERE cuenta_id = @AccountId; SELECT 1",
            new { withoutScope.AccountId });

        var first = await PasskeyEnterAsync(Client(), suspended.Authenticator, suspended.UserHandle);
        var second = await PasskeyEnterAsync(Client(), withoutScope.Authenticator, withoutScope.UserHandle);

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.False(OpensSession(first));
        Assert.False(OpensSession(second));
        Assert.Equal(1, await EventsAsync(suspended.AccountId, "PASSKEY_FALLIDA"));
    }

    [Fact]
    public async Task Entrada_CuentaBloqueadaPorIntentos_NoAbreSesion_AunqueLaPasskeyEsteBien()
    {
        var (subject, accountId, authenticator, userHandle) = await SeedWithPasskeyAsync();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await EnterAsync(Client(), subject, "no es la contraseña");
        }

        var response = await PasskeyEnterAsync(Client(), authenticator, userHandle);

        Assert.Equal(1, await EventsAsync(accountId, "BLOQUEO"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(OpensSession(response));
    }

    [Fact]
    public async Task QuitarPasskey_LaAnota_YYaNoEntra()
    {
        var (subject, accountId, authenticator, userHandle) = await SeedWithPasskeyAsync();
        var signedIn = await SignedInWithTotpAsync(subject, accountId);
        var id = Microsoft.AspNetCore.WebUtilities.Base64UrlTextEncoder.Encode(authenticator.CredentialId);

        var removed = await PostAsync(signedIn, "/Cuenta/QuitarPasskey", new() { ["id"] = id }, "/Cuenta");
        var entry = await PasskeyEnterAsync(Client(), authenticator, userHandle);

        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);
        Assert.Equal(0, await PasskeysAsync(accountId));
        Assert.Equal(1, await EventsAsync(accountId, "PASSKEY_BAJA"));
        Assert.Equal(HttpStatusCode.Unauthorized, entry.StatusCode);
    }

    [Fact]
    public async Task QuitarPasskey_DeOtraCuenta_NoQuitaNada()
    {
        var mine = await SeedWithPasskeyAsync();
        var theirs = await SeedWithPasskeyAsync();
        var signedIn = await SignedInWithTotpAsync(mine.Subject, mine.AccountId);

        await PostAsync(signedIn, "/Cuenta/QuitarPasskey",
            new() { ["id"] = Microsoft.AspNetCore.WebUtilities.Base64UrlTextEncoder.Encode(theirs.Authenticator.CredentialId) }, "/Cuenta");

        Assert.Equal(1, await PasskeysAsync(theirs.AccountId));
        Assert.Equal(1, await PasskeysAsync(mine.AccountId));
    }

    [Fact]
    public async Task Recuperacion_QuitaLasPasskeys_DeLaCuenta()
    {
        var (admin, _, adminId) = await AdministrationAsync();
        var (subject, accountId) = await SeedInCenterAsync(adminId, "ENFERMERIA");
        await UseInvitationAsync(Client(), InvitationUrl(await TextAsync(await IssueAsync(admin, subject, "/Administracion"))), accountId);
        var authenticator = new SoftwareAuthenticator();
        var creation = await RegisterPasskeyAsync(await SignedInWithTotpAsync(subject, accountId), accountId, authenticator);

        var sheet = await TextAsync(await IssueAsync(admin, subject, "/Administracion"));
        await UseInvitationAsync(Client(), InvitationUrl(sheet), accountId, "otra contraseña de prueba larga");
        var entry = await PasskeyEnterAsync(Client(), authenticator, SoftwareAuthenticator.UserHandle(creation));

        Assert.Equal(0, await PasskeysAsync(accountId));
        Assert.Equal(HttpStatusCode.Unauthorized, entry.StatusCode);
    }

    /// <summary>Una cuenta de Plataforma (en su centro reservado) con contraseña y código del móvil.</summary>
    private async Task<(string Subject, Guid AccountId)> SeedPlatformWithTwoFactorAsync()
    {
        var accountId = Guid.NewGuid();
        var subject = $"plataforma-{Guid.NewGuid():N}"[..28];
        await ScalarAsync<int>("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @subject, 'ACTIVE', SYSUTCDATETIME());
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @accountId, '5F3A1C00-0000-4000-8000-000000000001', 'PLATAFORMA', 'ACTIVE', SYSUTCDATETIME(), @accountId);
            SELECT 1
            """, new { accountId, subject });
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ResidApp.Infrastructure.Identity.CredentialAccount>>();
        var account = (await users.FindByNameAsync(subject))!;
        Assert.True((await users.AddPasswordAsync(account, Password)).Succeeded);
        await users.ResetAuthenticatorKeyAsync(account);
        await users.SetTwoFactorEnabledAsync(account, true);
        return (subject, accountId);
    }

    [Fact]
    public async Task Plataforma_SinPasskeyVaAMiCuenta_ConPasskeyYaNoEntraConContrasena_YNoPuedeQuitarLaUltima()
    {
        var (subject, accountId) = await SeedPlatformWithTwoFactorAsync();
        var authenticator = new SoftwareAuthenticator();
        var client = Client();
        await EnterAsync(client, subject, Password);
        var firstEntry = await PostAsync(client, "/Acceso/SegundoFactor", new() { ["codigo"] = Totp.Code(await KeyAsync(accountId)) }, "/Acceso/SegundoFactor");

        await RegisterPasskeyAsync(client, accountId, authenticator);
        var withPasskey = await EnterAsync(Client(), subject, Password);
        var lastRemoved = await PostAsync(client, "/Cuenta/QuitarPasskey",
            new() { ["id"] = Microsoft.AspNetCore.WebUtilities.Base64UrlTextEncoder.Encode(authenticator.CredentialId) }, "/Cuenta");

        Assert.True(OpensSession(firstEntry));
        Assert.Equal("/Cuenta", firstEntry.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, withPasskey.StatusCode);
        Assert.Contains(AccesoController.PlatformUsesPasskeyMessage, await TextAsync(withPasskey));
        Assert.False(OpensSession(withPasskey));
        Assert.Equal(HttpStatusCode.Redirect, lastRemoved.StatusCode);
        Assert.Equal(1, await PasskeysAsync(accountId));
        Assert.Equal(0, await EventsAsync(accountId, "PASSKEY_BAJA"));
    }
}
