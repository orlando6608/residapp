using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ResidApp.Infrastructure.Identity;
using ResidApp.Infrastructure.Persistence;
using ResidApp.Web.Controllers;

namespace ResidApp.FunctionalTests;

/// <summary>
/// ADR 0007, incremento I2: Administración entrega la invitación desde la ficha de la cuenta o desde la autorización del familiar; con
/// ella la persona elige su contraseña y, si es del personal, prepara el código del móvil. La de recuperación sustituye contraseña y
/// código y cierra las sesiones abiertas. Una invitación usada, anulada, reemplazada o caducada da el mismo mensaje, y el token no se
/// queda en la dirección.
/// </summary>
public partial class AccesoTests
{
    /// <summary>Una cuenta con un único perfil en el centro de la Administración indicada (y en su unidad, si el perfil la lleva).</summary>
    private static async Task<(string Subject, Guid AccountId)> SeedInCenterAsync(Guid adminAccountId, string profileCode)
    {
        var accountId = Guid.NewGuid();
        var subject = $"invitacion-{Guid.NewGuid():N}"[..28];
        using var connection = await new SqlConnectionFactory(ResidentsFlowTests.WebAppFactory.TestConnectionString).OpenAsync();
        await connection.ExecuteAsync("""
            DECLARE @now DATETIME2(3) = SYSUTCDATETIME(), @scope UNIQUEIDENTIFIER = NEWID(), @center UNIQUEIDENTIFIER, @unit UNIQUEIDENTIFIER;
            SELECT TOP (1) @center = profile.centro_id, @unit = unit.unidad_id
              FROM dbo.ambitos_perfil profile JOIN dbo.ambitos_perfil_unidad unit ON unit.ambito_perfil_id = profile.id
             WHERE profile.cuenta_id = @adminAccountId;
            INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @subject, 'ACTIVE', @now);
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@scope, @accountId, @center, @profileCode, 'ACTIVE', @now, @adminAccountId);
            IF @profileCode <> 'FAMILIAR'
                INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @scope, @center, @unit, @now, @adminAccountId);
            """, new { adminAccountId, accountId, subject, profileCode });
        return (subject, accountId);
    }

    /// <summary>Una sesión de Administración con su ámbito ya elegido (sigue las redirecciones de la entrada de pruebas).</summary>
    private async Task<(HttpClient Client, string Subject, Guid AccountId)> AdministrationAsync(WebApplicationFactory<Program>? app = null)
    {
        var (subject, accountId) = await SesionTests.SeedAccountAsync("ADMINISTRACION");
        var client = (app ?? factory).CreateClient();
        (await SesionTests.PostLoginAsync(client, subject)).EnsureSuccessStatusCode();
        return (client, subject, accountId);
    }

    private static async Task<HttpResponseMessage> IssueAsync(HttpClient admin, string subject, string back) =>
        await PostAsync(admin, "/Administracion/Invitacion", new() { ["usuario"] = subject, ["volver"] = back }, "/Administracion/Usuarios");

    /// <summary>La dirección de la invitación de una hoja (la del QR), como ruta.</summary>
    private static string InvitationUrl(string sheet) =>
        new Uri(WebUtility.HtmlDecode(Regex.Match(sheet, "data-direccion=\"([^\"]+)\"").Groups[1].Value)).PathAndQuery;

    /// <summary>Abre la invitación y elige la contraseña; devuelve la respuesta a ese envío.</summary>
    private static async Task<HttpResponseMessage> ChoosePasswordAsync(HttpClient client, string url, string password = Password)
    {
        var opened = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, opened.StatusCode);
        Assert.Equal("/Acceso/Invitacion", opened.Headers.Location!.OriginalString);
        return await PostAsync(client, "/Acceso/Invitacion", new() { ["contrasena"] = password, ["confirmacion"] = password }, "/Acceso/Invitacion");
    }

    /// <summary>El recorrido completo del personal: contraseña, código del móvil y códigos de recuperación (los devuelve).</summary>
    private async Task<IReadOnlyList<string>> UseInvitationAsync(HttpClient client, string url, Guid accountId, string password = Password)
    {
        var chosen = await ChoosePasswordAsync(client, url, password);
        Assert.Equal("/Acceso/ActivarSegundoFactor", chosen.Headers.Location!.OriginalString);
        var enroll = await PostAsync(client, "/Acceso/ActivarSegundoFactor", new() { ["codigo"] = Totp.Code(await KeyAsync(accountId)) },
            "/Acceso/ActivarSegundoFactor");
        Assert.True(OpensSession(enroll));
        return Regex.Matches(await enroll.Content.ReadAsStringAsync(), "<li class=\"col\">([^<]+)</li>").Select(m => m.Groups[1].Value).ToList();
    }

    [Fact]
    public async Task Administracion_EntregaLaInvitacion_YElPersonalEligeContrasenaYPreparaElCodigo()
    {
        var (admin, _, adminId) = await AdministrationAsync();
        var (subject, accountId) = await SeedInCenterAsync(adminId, "ENFERMERIA");
        var fichaUrl = $"/Administracion/Usuario?cuentaId={accountId}";

        var before = WebUtility.HtmlDecode(await admin.GetStringAsync(fichaUrl));
        var sheet = await IssueAsync(admin, subject, fichaUrl);
        var sheetText = await TextAsync(sheet);
        var url = InvitationUrl(sheetText);
        var nurse = Client();
        var shortPassword = await ChoosePasswordAsync(nurse, url, "corta");
        var codes = await UseInvitationAsync(Client(), url, accountId);
        var reused = await Client().GetAsync(url);
        var after = WebUtility.HtmlDecode(await admin.GetStringAsync(fichaUrl));
        var login = Client();
        await EnterAsync(login, subject, Password);
        var withCode = await PostAsync(login, "/Acceso/SegundoFactor", new() { ["codigo"] = Totp.Code(await KeyAsync(accountId)) }, "/Acceso/SegundoFactor");

        Assert.Contains("Crear invitación de acceso", before);
        Assert.Contains("no-store", sheet.Headers.CacheControl!.ToString());
        Assert.Contains("Invitación de acceso para", sheetText);
        Assert.StartsWith("/Acceso/Invitacion/", url);
        Assert.Contains("al menos 12 caracteres", await TextAsync(shortPassword));
        Assert.Equal(10, codes.Count);
        Assert.Equal(HttpStatusCode.Gone, reused.StatusCode);
        Assert.Contains(AccesoController.InvalidInvitationMessage, await TextAsync(reused));
        Assert.Contains("Crear invitación de recuperación", after);
        Assert.True(OpensSession(withCode));
        Assert.Equal(1, await EventsAsync(accountId, "INVITACION_USADA"));
        Assert.Equal(1, await EventsAsync(accountId, "SEGUNDO_FACTOR_ALTA"));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE recurso_id = @accountId AND accion_codigo = 'ACCOUNT_INVITATION_ISSUE'",
            new { accountId }));
    }

    [Fact]
    public async Task Familiar_UsaLaInvitacionSoloConContrasena_YEntra()
    {
        var (admin, _, adminId) = await AdministrationAsync();
        var (subject, accountId) = await SeedInCenterAsync(adminId, "FAMILIAR");

        var sheet = await TextAsync(await IssueAsync(admin, subject, "/Administracion"));
        var family = Client();
        var chosen = await ChoosePasswordAsync(family, InvitationUrl(sheet));

        Assert.Equal("/", chosen.Headers.Location!.OriginalString);
        Assert.True(OpensSession(chosen));
        Assert.Equal(1, await EventsAsync(accountId, "ENTRADA"));
    }

    [Fact]
    public async Task Recuperacion_CambiaContrasenaYCodigo_YCierraLasSesionesAbiertas()
    {
        var (admin, _, adminId) = await AdministrationAsync();
        var (subject, accountId) = await SeedInCenterAsync(adminId, "ENFERMERIA");
        await UseInvitationAsync(Client(), InvitationUrl(await TextAsync(await IssueAsync(admin, subject, "/Administracion"))), accountId);
        var oldKey = await KeyAsync(accountId);
        var openSession = Client();
        await EnterAsync(openSession, subject, Password);
        await PostAsync(openSession, "/Acceso/SegundoFactor", new() { ["codigo"] = Totp.Code(oldKey) }, "/Acceso/SegundoFactor");
        var before = await openSession.GetAsync("/ProfileScope/Select");

        var sheet = await TextAsync(await IssueAsync(admin, subject, "/Administracion"));
        await UseInvitationAsync(Client(), InvitationUrl(sheet), accountId, "otra contraseña de prueba larga");
        var after = await openSession.GetAsync("/ProfileScope/Select");
        var oldPassword = await EnterAsync(Client(), subject, Password);

        Assert.Contains("Invitación de recuperación para", sheet);
        Assert.False(SesionTests.RedirectsToLogin(before));
        Assert.True(SesionTests.RedirectsToLogin(after));
        Assert.Contains(FailedMessage, await TextAsync(oldPassword));
        Assert.NotEqual(oldKey, await KeyAsync(accountId));
        Assert.True(await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.sesiones WHERE cuenta_id = @accountId AND motivo_cierre = 'RECUPERACION'", new { accountId }) >= 1);
        Assert.Equal(1, await EventsAsync(accountId, "RECUPERACION_USADA"));
    }

    [Fact]
    public async Task InvitacionReemplazadaAnuladaOCaducada_DaElMismoMensaje_YNoCambiaNada()
    {
        var clock = new TestClock();
        var late = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));
        var (admin, _, adminId) = await AdministrationAsync();
        var (subject, accountId) = await SeedInCenterAsync(adminId, "ENFERMERIA");
        var ficha = $"/Administracion/Usuario?cuentaId={accountId}";

        var replaced = InvitationUrl(await TextAsync(await IssueAsync(admin, subject, ficha)));
        var cancelled = InvitationUrl(await TextAsync(await IssueAsync(admin, subject, ficha)));
        var annul = await PostAsync(admin, "/Administracion/AnularInvitacion", new() { ["usuario"] = subject, ["volver"] = ficha },
            "/Administracion/Usuarios");
        var expired = InvitationUrl(await TextAsync(await IssueAsync(admin, subject, ficha)));
        clock.Advance(TimeSpan.FromHours(73));
        var responses = new[] { await Client().GetAsync(replaced), await Client().GetAsync(cancelled), await Client(late).GetAsync(expired) };

        Assert.Contains("Invitación anulada", await TextAsync(annul));
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
            Assert.Contains(AccesoController.InvalidInvitationMessage, await TextAsync(response));
        }
        Assert.Null(await ScalarAsync<string?>("SELECT hash_contrasena FROM dbo.credenciales WHERE cuenta_id = @accountId", new { accountId }));
    }

    [Fact]
    public async Task Administracion_NoSeEntregaInvitacionASiMisma()
    {
        var (admin, subject, accountId) = await AdministrationAsync();

        var response = await IssueAsync(admin, subject, $"/Administracion/Usuario?cuentaId={accountId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("es la tuya", await TextAsync(response));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.invitaciones_acceso WHERE cuenta_id = @accountId", new { accountId }));
    }

    private const string FailedMessage = AccesoController.FailedMessage;
}
