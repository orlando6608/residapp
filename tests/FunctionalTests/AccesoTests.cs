using System.Net;
using System.Security.Cryptography;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ResidApp.Infrastructure.Identity;
using ResidApp.Infrastructure.Persistence;
using ResidApp.Web.Controllers;

namespace ResidApp.FunctionalTests;

/// <summary>
/// Inicio de sesión con contraseña (ADR 0007, incremento I1): el mismo mensaje para cualquier fallo, bloqueo tras 5 intentos, segundo
/// factor obligatorio para el personal (con alta en el primer acceso y códigos de recuperación de un solo uso) y solo contraseña para los
/// familiares; el paso entre la contraseña y el segundo factor no abre la aplicación; el registro de accesos; y, de I0, que guardar un
/// formulario con la sesión terminada vuelve a la pantalla del formulario en vez de a una acción solo POST.
/// </summary>
public partial class AccesoTests(ResidentsFlowTests.WebAppFactory factory) : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    private const string Password = "contraseña de prueba larga";

    private HttpClient Client(WebApplicationFactory<Program>? app = null) =>
        (app ?? factory).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(string Subject, Guid AccountId)> SeedWithPasswordAsync(string profileCode)
    {
        var account = await SesionTests.SeedAccountAsync(profileCode);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<CredentialAccount>>();
        Assert.True((await users.AddPasswordAsync((await users.FindByNameAsync(account.Subject))!, Password)).Succeeded);
        return account;
    }

    /// <summary>Personal con el segundo factor ya activado; devuelve también sus códigos de recuperación.</summary>
    private async Task<(string Subject, Guid AccountId, IReadOnlyList<string> Codes)> SeedWithTwoFactorAsync()
    {
        var (subject, accountId) = await SeedWithPasswordAsync("ENFERMERIA");
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<CredentialAccount>>();
        var account = (await users.FindByNameAsync(subject))!;
        await users.ResetAuthenticatorKeyAsync(account);
        await users.SetTwoFactorEnabledAsync(account, true);
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(account, 10))!.ToList();
        return (subject, accountId, codes);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, Dictionary<string, string> fields, string tokenPage = "/Acceso/Entrar")
    {
        var page = await client.GetStringAsync(tokenPage);
        fields["__RequestVerificationToken"] = SesionTests.ExtractValue(page, "__RequestVerificationToken");
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    private static Task<HttpResponseMessage> EnterAsync(HttpClient client, string subject, string password, string? returnUrl = null) =>
        PostAsync(client, "/Acceso/Entrar", new Dictionary<string, string>
        {
            ["usuario"] = subject, ["contrasena"] = password, ["returnUrl"] = returnUrl ?? string.Empty,
        });

    private static async Task<string> TextAsync(HttpResponseMessage response) => WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    private static bool OpensSession(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("residapp_sesion=", StringComparison.Ordinal)
            && !c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));

    private static async Task<T> ScalarAsync<T>(string sql, object parameters)
    {
        using var connection = await new SqlConnectionFactory(ResidentsFlowTests.WebAppFactory.TestConnectionString).OpenAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    private static Task<string> KeyAsync(Guid accountId) =>
        ScalarAsync<string>("SELECT clave_autenticador FROM dbo.credenciales WHERE cuenta_id = @accountId", new { accountId });

    private static Task<int> EventsAsync(Guid accountId, string type) =>
        ScalarAsync<int>("SELECT COUNT(*) FROM dbo.eventos_acceso WITH (NOLOCK) WHERE cuenta_id = @accountId AND tipo = @type", new { accountId, type });

    [Fact]
    public async Task Familiar_EntraSoloConLaContrasena_YQuedaAnotado()
    {
        var (subject, accountId) = await SeedWithPasswordAsync("FAMILIAR");
        var client = Client();

        var response = await EnterAsync(client, subject, Password);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
        Assert.True(OpensSession(response));
        Assert.False(SesionTests.RedirectsToLogin(await client.GetAsync("/ProfileScope/Select")));
        Assert.Equal(1, await EventsAsync(accountId, "ENTRADA"));
    }

    [Fact]
    public async Task CambiarAmbito_ConUnSoloAmbito_AvisaEnVezDeElegirloEnSilencio()
    {
        var (subject, _) = await SeedWithPasswordAsync("FAMILIAR");
        var client = Client();
        await EnterAsync(client, subject, Password);

        var firstTime = await client.GetAsync("/ProfileScope/Select");
        var changing = await client.GetAsync("/ProfileScope/Select?cambiar=true");
        var notice = await TextAsync(changing);

        Assert.Equal(HttpStatusCode.Redirect, firstTime.StatusCode);
        Assert.Equal(HttpStatusCode.OK, changing.StatusCode);
        Assert.Contains("Solo tienes un ámbito activo", notice);
        Assert.DoesNotContain("name=\"profileScopeId\"", notice);
    }

    [Fact]
    public async Task CualquierFallo_DaElMismoMensaje_YNoAbreSesion()
    {
        var wrong = await SeedWithPasswordAsync("FAMILIAR");
        var suspended = await SeedWithPasswordAsync("FAMILIAR");
        var withoutScope = await SeedWithPasswordAsync("FAMILIAR");
        var withoutPassword = await SesionTests.SeedAccountAsync("FAMILIAR");
        await ScalarAsync<int>("UPDATE dbo.cuentas SET estado = 'SUSPENDED' WHERE id = @AccountId; SELECT 1", new { suspended.AccountId });
        await ScalarAsync<int>("UPDATE dbo.ambitos_perfil SET estado = 'REVOKED', revocado_en = SYSUTCDATETIME(), revocado_por_cuenta_id = cuenta_id WHERE cuenta_id = @AccountId; SELECT 1",
            new { withoutScope.AccountId });

        var responses = new[]
        {
            await EnterAsync(Client(), wrong.Subject, "no es la contraseña"),
            await EnterAsync(Client(), $"nadie-{Guid.NewGuid():N}", Password),
            await EnterAsync(Client(), suspended.Subject, Password),
            await EnterAsync(Client(), withoutScope.Subject, Password),
            await EnterAsync(Client(), withoutPassword.Subject, Password),
        };

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(AccesoController.FailedMessage, await TextAsync(response));
            Assert.False(OpensSession(response));
        }
        Assert.Equal(1, await EventsAsync(wrong.AccountId, "CONTRASENA_FALLIDA"));
    }

    [Fact]
    public async Task CincoFallos_BloqueanLaCuenta_AunqueLaSextaSeaCorrecta()
    {
        var (subject, accountId) = await SeedWithPasswordAsync("FAMILIAR");
        var client = Client();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await EnterAsync(client, subject, "no es la contraseña");
        }
        var sixth = await EnterAsync(client, subject, Password);

        Assert.Contains(AccesoController.FailedMessage, await TextAsync(sixth));
        Assert.False(OpensSession(sixth));
        Assert.Equal(1, await EventsAsync(accountId, "BLOQUEO"));
        Assert.Equal(6, await EventsAsync(accountId, "CONTRASENA_FALLIDA"));
    }

    /// <summary>ADR 0007, I2: el segundo factor solo se da de alta desde una invitación; saber la contraseña no basta.</summary>
    [Fact]
    public async Task Personal_SinSegundoFactor_NoLoPuedeActivarAlEntrar()
    {
        var (subject, accountId) = await SeedWithPasswordAsync("ENFERMERIA");
        var client = Client();

        var afterPassword = await EnterAsync(client, subject, Password);
        var enroll = await client.GetAsync("/Acceso/ActivarSegundoFactor");

        Assert.Equal(HttpStatusCode.OK, afterPassword.StatusCode);
        Assert.Contains(AccesoController.NeedsInvitationMessage, await TextAsync(afterPassword));
        Assert.False(OpensSession(afterPassword));
        Assert.Equal("/Acceso/Entrar", enroll.Headers.Location!.OriginalString);
        Assert.Null(await ScalarAsync<string?>("SELECT clave_autenticador FROM dbo.credenciales WHERE cuenta_id = @accountId AND segundo_factor_activo = 1",
            new { accountId }));
    }

    [Fact]
    public async Task Personal_ConSegundoFactor_PideElCodigo_YUnCodigoDeRecuperacionValeUnaVez()
    {
        var (subject, accountId, codes) = await SeedWithTwoFactorAsync();
        var key = await KeyAsync(accountId);

        var totpClient = Client();
        var toSecondFactor = await EnterAsync(totpClient, subject, Password);
        var withTotp = await PostAsync(totpClient, "/Acceso/SegundoFactor", new() { ["codigo"] = Totp.Code(key) }, "/Acceso/SegundoFactor");

        var recoveryClient = Client();
        await EnterAsync(recoveryClient, subject, Password);
        var withRecovery = await PostAsync(recoveryClient, "/Acceso/CodigoRecuperacion", new() { ["codigo"] = codes[0] }, "/Acceso/CodigoRecuperacion");

        var reuseClient = Client();
        await EnterAsync(reuseClient, subject, Password);
        var reused = await PostAsync(reuseClient, "/Acceso/CodigoRecuperacion", new() { ["codigo"] = codes[0] }, "/Acceso/CodigoRecuperacion");

        Assert.Equal("/Acceso/SegundoFactor", toSecondFactor.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, withTotp.StatusCode);
        Assert.True(OpensSession(withTotp));
        Assert.True(OpensSession(withRecovery));
        Assert.False(OpensSession(reused));
        Assert.Contains("El código no es correcto", await TextAsync(reused));
        Assert.Equal(1, await EventsAsync(accountId, "CODIGO_RECUPERACION_USADO"));
    }

    [Fact]
    public async Task PasoPendiente_NoValeSiCambiaLaContrasena_NiPasadosDiezMinutos()
    {
        var clock = new TestClock();
        var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));
        var (subject, accountId, _) = await SeedWithTwoFactorAsync();
        var key = await KeyAsync(accountId);

        var changed = Client(app);
        await EnterAsync(changed, subject, Password);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<CredentialAccount>>();
            var account = (await users.FindByNameAsync(subject))!;
            await users.RemovePasswordAsync(account);
            await users.AddPasswordAsync(account, Password);
        }
        var afterChange = await PostAsync(changed, "/Acceso/SegundoFactor", new() { ["codigo"] = Totp.Code(key) });

        var late = Client(app);
        await EnterAsync(late, subject, Password);
        clock.Advance(TimeSpan.FromMinutes(11));
        var afterTenMinutes = await PostAsync(late, "/Acceso/SegundoFactor", new() { ["codigo"] = Totp.Code(key) });

        Assert.Equal("/Acceso/Entrar", afterChange.Headers.Location!.OriginalString);
        Assert.False(OpensSession(afterChange));
        Assert.Equal("/Acceso/Entrar", afterTenMinutes.Headers.Location!.OriginalString);
        Assert.False(OpensSession(afterTenMinutes));
    }

    [Fact]
    public async Task GuardarConLaSesionTerminada_VuelveALaPantallaDelFormulario_YAvisa()
    {
        var client = Client();
        Task<HttpResponseMessage> PostWithReferer(string referer)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/ProfileScope/Select") { Content = new FormUrlEncodedContent([]) };
            request.Headers.Referrer = new Uri(referer);
            return client.SendAsync(request);
        }

        var sameApp = await PostWithReferer("http://localhost/Administracion/NuevoUsuario?x=1");
        var otherSite = await PostWithReferer("https://ejemplo.invalid/Administracion");
        var login = await TextAsync(await client.GetAsync(sameApp.Headers.Location));
        var (subject, _) = await SeedWithPasswordAsync("FAMILIAR");
        var afterLogin = await EnterAsync(client, subject, Password, "/Administracion/NuevoUsuario?x=1");

        Assert.Equal("/Acceso/Entrar?sesionCaducada=true&ReturnUrl=%2FAdministracion%2FNuevoUsuario%3Fx%3D1",
            sameApp.Headers.Location!.PathAndQuery());
        Assert.Equal("/Acceso/Entrar?sesionCaducada=true", otherSite.Headers.Location!.PathAndQuery());
        Assert.Contains("no se ha guardado", login);
        Assert.Contains("value=\"/Administracion/NuevoUsuario?x=1\"", login);
        Assert.Equal("/Administracion/NuevoUsuario?x=1", afterLogin.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task SolicitarAcceso_EsPublico_YSinFormularioDeAlta()
    {
        var client = Client();
        // Sin sesión, una ruta que no existe lleva al inicio de sesión (I0): la maqueta se busca con sesión.
        var withSession = await SesionTests.LoginAsync(factory, (await SesionTests.SeedAccountAsync()).Subject);

        var page = await client.GetAsync("/Acceso/Solicitar");
        var oldMockup = await withSession.GetAsync("/DevAuth/Register");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Pide el acceso a la Administración de tu centro", await TextAsync(page));
        Assert.DoesNotContain("<form", await TextAsync(page));
        Assert.Equal(HttpStatusCode.NotFound, oldMockup.StatusCode);
    }

    /// <summary>Ejecuta database/operaciones/invitar_cuenta.sql como lo haría quien administra la base y devuelve lo que imprime.</summary>
    private static async Task<IReadOnlyList<string>> BootstrapInvitationAsync(string subject)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "database", "operaciones", "invitar_cuenta.sql")))
        {
            directory = directory.Parent;
        }
        var script = (await File.ReadAllTextAsync(Path.Combine(directory!.FullName, "database", "operaciones", "invitar_cuenta.sql")))
            .Replace("$(CUENTA)", subject);
        var printed = new List<string>();
        using var connection = (Microsoft.Data.SqlClient.SqlConnection)await new SqlConnectionFactory(
            ResidentsFlowTests.WebAppFactory.TestConnectionString).OpenAsync();
        // Los PRINT pueden llegar juntos en un solo aviso: se toma cada uno.
        connection.InfoMessage += (_, message) => printed.AddRange(message.Errors.Cast<Microsoft.Data.SqlClient.SqlError>().Select(e => e.Message));
        await connection.ExecuteAsync(script);
        return printed;
    }

    [Fact]
    public async Task InvitacionDeArranque_DesdeLaBase_AbreElAlta_YLaSiguienteEsDeRecuperacion()
    {
        var (subject, accountId) = await SesionTests.SeedAccountAsync("ADMINISTRACION");

        var printed = await BootstrapInvitationAsync(subject);
        var client = Client();
        var codes = await UseInvitationAsync(client, printed[1], accountId);
        var again = await BootstrapInvitationAsync(subject);

        Assert.StartsWith($"Invitación de alta para {subject}", printed[0]);
        Assert.Equal(10, codes.Count);
        Assert.False(SesionTests.RedirectsToLogin(await client.GetAsync("/ProfileScope/Select")));
        Assert.Equal(1, await EventsAsync(accountId, "INVITACION_USADA"));
        Assert.Equal(2, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.invitaciones_acceso WHERE cuenta_id = @accountId AND perfil_emisor = 'ARRANQUE'", new { accountId }));
        Assert.StartsWith($"Invitación de recuperación para {subject}", again[0]);
    }

    [Fact]
    public async Task SinSesion_SeEntraYSeSaleConContrasena()
    {
        var (subject, accountId) = await SeedWithPasswordAsync("FAMILIAR");
        var client = Client();

        var entered = await EnterAsync(client, subject, Password);
        var exited = await PostAsync(client, "/Acceso/Salir", new());

        Assert.True(OpensSession(entered));
        Assert.Equal(HttpStatusCode.Redirect, exited.StatusCode);
        Assert.Equal("LOGOUT", await ScalarAsync<string>("SELECT motivo_cierre FROM dbo.sesiones WHERE cuenta_id = @accountId", new { accountId }));
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan time) => _now += time;
    }
}

internal static class UriPath
{
    /// <summary>La ruta y la consulta de una dirección de redirección, sea relativa o absoluta.</summary>
    public static string PathAndQuery(this Uri uri) => uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;
}

/// <summary>Código TOTP (RFC 6238: HMAC-SHA1, 30 segundos, 6 cifras) a partir de la clave en base32, como lo calcula una aplicación de
/// autenticación.</summary>
internal static class Totp
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Code(string base32Key, DateTimeOffset? at = null)
    {
        var step = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counter);
        }

        var hash = HMACSHA1.HashData(Decode(base32Key), counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Decode(string base32)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in base32.TrimEnd('=').ToUpperInvariant())
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return [.. bytes];
    }
}
