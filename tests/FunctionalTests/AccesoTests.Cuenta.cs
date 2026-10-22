using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using ResidApp.Infrastructure.Persistence;

namespace ResidApp.FunctionalTests;

/// <summary>
/// ADR 0007, incremento I3, «Mi cuenta» (ADM-30 y FAM-16): la persona cambia su contraseña (que cierra sus demás sesiones), genera códigos de
/// recuperación nuevos y cierra sus sesiones abiertas, sin poder tocar las de otra cuenta. Los fallos al pedir la contraseña actual cuentan
/// para el bloqueo igual que al entrar.
/// </summary>
public partial class AccesoTests
{
    private const string NewPassword = "otra contraseña de prueba larga";

    private async Task<HttpClient> SignedInAsync(string subject)
    {
        var client = Client();
        var response = await EnterAsync(client, subject, Password);
        Assert.True(OpensSession(response), "La entrada con la contraseña debería abrir sesión.");
        return client;
    }

    private async Task<HttpClient> SignedInWithTotpAsync(string subject, Guid accountId)
    {
        var client = Client();
        await EnterAsync(client, subject, Password);
        var response = await PostAsync(client, "/Acceso/SegundoFactor", new() { ["codigo"] = Totp.Code(await KeyAsync(accountId)) }, "/Acceso/SegundoFactor");
        Assert.True(OpensSession(response), "El código del móvil debería abrir sesión.");
        return client;
    }

    private static Task<HttpResponseMessage> ChangePasswordAsync(HttpClient client, string current, string next, string? confirmation = null) =>
        PostAsync(client, "/Cuenta/CambiarContrasena", new() { ["actual"] = current, ["nueva"] = next, ["confirmacion"] = confirmation ?? next }, "/Cuenta");

    private static Task<string> OpenSessionIdAsync(Guid accountId, int skip = 0) => ScalarAsync<string>(
        "SELECT CAST(id AS NVARCHAR(36)) FROM dbo.sesiones WHERE cuenta_id = @accountId AND cerrada_en IS NULL ORDER BY creada_en OFFSET @skip ROWS FETCH NEXT 1 ROWS ONLY",
        new { accountId, skip });

    [Fact]
    public async Task MiCuenta_SinSesion_LlevaAlInicioDeSesion()
    {
        var client = Client();

        var page = await client.GetAsync("/Cuenta");
        var post = await client.PostAsync("/Cuenta/CerrarOtrasSesiones", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.True(SesionTests.RedirectsToLogin(page));
        Assert.True(SesionTests.RedirectsToLogin(post));
    }

    [Fact]
    public async Task MiCuenta_EnseñaLaCuenta_LasSesionesAbiertas_YLosUltimosAccesos()
    {
        var (subject, accountId) = await SeedWithPasswordAsync("FAMILIAR");
        await EnterAsync(Client(), subject, "no es la contraseña");
        var client = await SignedInAsync(subject);
        await SignedInAsync(subject);

        var page = await TextAsync(await client.GetAsync("/Cuenta"));

        Assert.Contains(subject, page);
        Assert.Contains("Esta sesión", page);
        Assert.Single(Regex.Matches(page, "Esta sesión"));
        Assert.Single(Regex.Matches(page, "action=\"/Cuenta/CerrarSesion\""));
        Assert.Contains("Contraseña incorrecta", page);
        Assert.Contains("Entrada", page);
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.sesiones WHERE cuenta_id = @accountId AND cerrada_en IS NULL", new { accountId }));
    }

    [Fact]
    public async Task CambiarContrasena_CierraLasDemasSesiones_YLaAntiguaDejaDeValer()
    {
        var (subject, accountId) = await SeedWithPasswordAsync("FAMILIAR");
        var here = await SignedInAsync(subject);
        var elsewhere = await SignedInAsync(subject);

        var wrong = await ChangePasswordAsync(here, "no es la contraseña", NewPassword);
        var tooShort = await ChangePasswordAsync(here, Password, "corta");
        var different = await ChangePasswordAsync(here, Password, NewPassword, "no coincide con la nueva");
        var same = await ChangePasswordAsync(here, Password, Password);
        var closedTooEarly = SesionTests.RedirectsToLogin(await elsewhere.GetAsync("/Cuenta"));
        var changed = await ChangePasswordAsync(here, Password, NewPassword);

        Assert.Contains("La contraseña actual no es correcta", await TextAsync(wrong));
        Assert.Contains("al menos 12 caracteres", await TextAsync(tooShort));
        Assert.Contains("no coinciden", await TextAsync(different));
        Assert.Contains("distinta de la actual", await TextAsync(same));
        Assert.False(closedTooEarly);
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        Assert.Contains("Contraseña cambiada", await TextAsync(await here.GetAsync("/Cuenta")));
        Assert.True(SesionTests.RedirectsToLogin(await elsewhere.GetAsync("/Cuenta")));
        Assert.False(SesionTests.RedirectsToLogin(await here.GetAsync("/Cuenta")));
        Assert.False(OpensSession(await EnterAsync(Client(), subject, Password)));
        Assert.True(OpensSession(await EnterAsync(Client(), subject, NewPassword)));
        Assert.Equal(1, await EventsAsync(accountId, "CONTRASENA_CAMBIADA"));
        Assert.Equal(2, await EventsAsync(accountId, "CONTRASENA_FALLIDA"));
        Assert.Equal("CIERRE_PROPIO", await ScalarAsync<string>(
            "SELECT TOP (1) motivo_cierre FROM dbo.sesiones WHERE cuenta_id = @accountId AND motivo_cierre IS NOT NULL ORDER BY cerrada_en DESC", new { accountId }));
    }

    [Fact]
    public async Task CambiarContrasena_ConLaActualIncorrecta_CuentaParaElBloqueo()
    {
        var (subject, accountId) = await SeedWithPasswordAsync("FAMILIAR");
        var client = await SignedInAsync(subject);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await ChangePasswordAsync(client, "no es la contraseña", NewPassword);
        }
        var sixth = await ChangePasswordAsync(client, Password, NewPassword);

        Assert.Contains("bloqueada", await TextAsync(sixth));
        Assert.Equal(1, await EventsAsync(accountId, "BLOQUEO"));
        Assert.False(OpensSession(await EnterAsync(Client(), subject, NewPassword)));
        Assert.Equal(0, await EventsAsync(accountId, "CONTRASENA_CAMBIADA"));
    }

    [Fact]
    public async Task CodigosDeRecuperacion_Nuevos_ExigenContrasenaYCodigo_YLosAnterioresDejanDeValer()
    {
        var (subject, accountId, oldCodes) = await SeedWithTwoFactorAsync();
        var client = await SignedInWithTotpAsync(subject, accountId);
        var key = await KeyAsync(accountId);

        var noPassword = await PostAsync(client, "/Cuenta/RegenerarCodigos", new() { ["contrasena"] = "no es la contraseña", ["codigo"] = Totp.Code(key) }, "/Cuenta");
        var noCode = await PostAsync(client, "/Cuenta/RegenerarCodigos", new() { ["contrasena"] = Password, ["codigo"] = "000000" }, "/Cuenta");
        var stillOld = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.credenciales WHERE cuenta_id = @accountId AND codigos_recuperacion IS NOT NULL", new { accountId });
        var done = await PostAsync(client, "/Cuenta/RegenerarCodigos", new() { ["contrasena"] = Password, ["codigo"] = Totp.Code(key) }, "/Cuenta");
        var html = await TextAsync(done);
        var newCodes = Regex.Matches(html, @"<li class=""col"">([^<]+)</li>").Select(m => m.Groups[1].Value.Trim()).ToList();

        Assert.Contains("La contraseña actual no es correcta", await TextAsync(noPassword));
        Assert.Contains("El código del móvil no es correcto", await TextAsync(noCode));
        Assert.Equal(1, stillOld);
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        Assert.Contains("no-store", done.Headers.CacheControl!.ToString());
        Assert.Equal(10, newCodes.Count);
        Assert.DoesNotContain(oldCodes[0], newCodes);
        Assert.Equal(1, await EventsAsync(accountId, "CODIGOS_REGENERADOS"));

        var withOld = Client();
        await EnterAsync(withOld, subject, Password);
        var oldResult = await PostAsync(withOld, "/Acceso/CodigoRecuperacion", new() { ["codigo"] = oldCodes[0] }, "/Acceso/CodigoRecuperacion");
        var withNew = Client();
        await EnterAsync(withNew, subject, Password);
        var newResult = await PostAsync(withNew, "/Acceso/CodigoRecuperacion", new() { ["codigo"] = newCodes[0] }, "/Acceso/CodigoRecuperacion");
        Assert.False(OpensSession(oldResult));
        Assert.True(OpensSession(newResult));
    }

    [Fact]
    public async Task CodigosDeRecuperacion_UnFamiliarSinSegundoFactor_NoTiene()
    {
        var (subject, accountId) = await SeedWithPasswordAsync("FAMILIAR");
        var client = await SignedInAsync(subject);

        var response = await PostAsync(client, "/Cuenta/RegenerarCodigos", new() { ["contrasena"] = Password, ["codigo"] = "123456" }, "/Cuenta");

        Assert.DoesNotContain("font-monospace", await TextAsync(response));
        Assert.Equal(0, await EventsAsync(accountId, "CODIGOS_REGENERADOS"));
    }

    [Fact]
    public async Task CerrarSesion_CierraLaOtra_NuncaLaActual_NiLaDeOtraCuenta()
    {
        var (subject, accountId) = await SeedWithPasswordAsync("FAMILIAR");
        var (otherSubject, otherAccountId) = await SeedWithPasswordAsync("FAMILIAR");
        var here = await SignedInAsync(subject);
        var elsewhere = await SignedInAsync(subject);
        var stranger = await SignedInAsync(otherSubject);
        var hereId = await OpenSessionIdAsync(accountId);
        var elsewhereId = await OpenSessionIdAsync(accountId, 1);
        var strangerId = await OpenSessionIdAsync(otherAccountId);

        // Con el id de una sesión de otra cuenta no se cierra nada, y con el de la propia sesión actual tampoco.
        await PostAsync(here, "/Cuenta/CerrarSesion", new() { ["id"] = strangerId }, "/Cuenta");
        var current = await PostAsync(here, "/Cuenta/CerrarSesion", new() { ["id"] = hereId }, "/Cuenta");
        var strangerStillOpen = !SesionTests.RedirectsToLogin(await stranger.GetAsync("/Cuenta"));
        var currentStillOpen = !SesionTests.RedirectsToLogin(await here.GetAsync("/Cuenta"));
        var closed = await PostAsync(here, "/Cuenta/CerrarSesion", new() { ["id"] = elsewhereId }, "/Cuenta");

        Assert.True(strangerStillOpen);
        Assert.True(currentStillOpen);
        Assert.Equal(HttpStatusCode.Redirect, current.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, closed.StatusCode);
        Assert.True(SesionTests.RedirectsToLogin(await elsewhere.GetAsync("/Cuenta")));
        Assert.False(SesionTests.RedirectsToLogin(await here.GetAsync("/Cuenta")));
        Assert.Equal(1, await EventsAsync(accountId, "SESION_CERRADA"));
        Assert.Equal(0, await EventsAsync(otherAccountId, "SESION_CERRADA"));
    }

    [Fact]
    public async Task CerrarOtrasSesiones_DejaSoloLaActual()
    {
        var (subject, accountId) = await SeedWithPasswordAsync("FAMILIAR");
        var here = await SignedInAsync(subject);
        var second = await SignedInAsync(subject);
        var third = await SignedInAsync(subject);

        var response = await PostAsync(here, "/Cuenta/CerrarOtrasSesiones", new(), "/Cuenta");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(SesionTests.RedirectsToLogin(await here.GetAsync("/Cuenta")));
        Assert.True(SesionTests.RedirectsToLogin(await second.GetAsync("/Cuenta")));
        Assert.True(SesionTests.RedirectsToLogin(await third.GetAsync("/Cuenta")));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.sesiones WHERE cuenta_id = @accountId AND cerrada_en IS NULL", new { accountId }));
    }
}
