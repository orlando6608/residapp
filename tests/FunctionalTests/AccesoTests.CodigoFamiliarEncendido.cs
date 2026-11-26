using System.Net;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ResidApp.Infrastructure.Identity;
using ResidApp.Infrastructure.Persistence;
using ResidApp.Web.Controllers;
using ResidApp.Web.Models;

namespace ResidApp.FunctionalTests;

/// <summary>
/// Lo que puede fallar justo al encender el código de la familia en un entorno que ya tiene cuentas (revisión de calidad de 2026-11-26): cuentas que además son de personal, cuentas
/// sin correo, invitaciones emitidas antes, recuperaciones, pasos pendientes y los textos de pantalla que cambian.
/// </summary>
public partial class AccesoTests
{
    private static async Task ExecuteSqlAsync(string sql, object parameters)
    {
        using var connection = await new SqlConnectionFactory(ResidentsFlowTests.WebAppFactory.TestConnectionString).OpenAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    [Fact]
    public async Task PersonalQueTambienEsFamilia_ConElCodigoEncendido_SigueEntrandoComoPersonal_SinCodigoPorCorreo()
    {
        var mailbox = new Mailbox();
        var app = WithFamilyCode(mailbox);
        var (admin, _, adminId) = await AdministrationAsync(app);
        var (subject, accountId) = await SeedInCenterAsync(adminId, "ENFERMERIA");
        // La ficha, antes del segundo perfil: hay una por cuenta y centro, y la ayuda crea una por cada ámbito que encuentra.
        await AddFamilyRecordAsync(accountId, $"{subject}@example.test");
        await ExecuteSqlAsync("""
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            SELECT NEWID(), cuenta_id, centro_id, N'FAMILIAR', N'ACTIVE', SYSUTCDATETIME(), @adminId FROM dbo.ambitos_perfil WHERE cuenta_id = @accountId
            """, new { adminId, accountId });
        var sheet = await TextAsync(await IssueAsync(admin, subject, "/Administracion/Usuarios"));

        // Con la invitación tiene que preparar el código del móvil.
        var chosen = await ChoosePasswordAsync(Client(app), InvitationUrl(sheet));
        Assert.Equal("/Acceso/ActivarSegundoFactor", chosen.Headers.Location!.OriginalString);
        Assert.False(OpensSession(chosen));

        // Sin prepararlo, la contraseña no basta ni abre el paso del correo.
        var withoutApp = await EnterAsync(Client(app), subject, Password);
        Assert.False(OpensSession(withoutApp));
        Assert.Contains(AccesoController.NeedsInvitationMessage, await TextAsync(withoutApp));

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<CredentialAccount>>();
            var account = (await users.FindByNameAsync(subject))!;
            await users.ResetAuthenticatorKeyAsync(account);
            await users.SetTwoFactorEnabledAsync(account, true);
        }

        var withApp = await EnterAsync(Client(app), subject, Password);

        Assert.Equal("/Acceso/SegundoFactor", withApp.Headers.Location!.OriginalString);
        Assert.False(OpensSession(withApp));
        Assert.Empty(mailbox.Sent);
    }

    [Fact]
    public async Task ConElCodigo_AdministracionVeQueUnaCuentaSinCorreoNoPuedeEntrar()
    {
        var mailbox = new Mailbox();
        var app = WithFamilyCode(mailbox);
        var family = await ResidentWithFamilyAsync(app);
        var resident = $"/Administracion/Residente?residenteId={family.ResidentId}";
        var subject = $"familia-{Guid.NewGuid():N}"[..20];
        const string Warning = "Ahora mismo no puede entrar: no tiene correo en su ficha";
        await SaveEmailAsync(family, $"{subject}@example.test");
        await GrantAccessAsync(family, subject);

        var with = WebUtility.HtmlDecode(await family.Admin.GetStringAsync(family.Authorization));
        var withInResident = WebUtility.HtmlDecode(await family.Admin.GetStringAsync(resident));
        await SaveEmailAsync(family, string.Empty);
        var without = WebUtility.HtmlDecode(await family.Admin.GetStringAsync(family.Authorization));
        var withoutInResident = WebUtility.HtmlDecode(await family.Admin.GetStringAsync(resident));

        Assert.DoesNotContain(Warning, with);
        Assert.DoesNotContain(AdministrativeResidentDisplay.PortalWithoutEmail, withInResident);
        Assert.Contains(Warning, without);
        Assert.Contains("Añadir el correo", without);
        Assert.Contains(AdministrativeResidentDisplay.PortalWithoutEmail, withoutInResident);
    }

    [Fact]
    public async Task ConElCodigoApagado_UnaCuentaSinCorreo_NoSeAvisa()
    {
        var family = await ResidentWithFamilyAsync(factory);
        await GrantAccessAsync(family, $"familia-{Guid.NewGuid():N}"[..20]);

        var authorization = WebUtility.HtmlDecode(await family.Admin.GetStringAsync(family.Authorization));
        var resident = WebUtility.HtmlDecode(await family.Admin.GetStringAsync($"/Administracion/Residente?residenteId={family.ResidentId}"));

        Assert.Contains("Tiene acceso al portal con el usuario", authorization);
        Assert.DoesNotContain("Ahora mismo no puede entrar", authorization);
        Assert.DoesNotContain(AdministrativeResidentDisplay.PortalWithoutEmail, resident);
    }

    [Fact]
    public async Task InvitacionEmitidaSinCorreo_YUsadaConElCodigo_DiceQueLaContrasenaQuedoGuardada()
    {
        var mailbox = new Mailbox();
        var app = WithFamilyCode(mailbox);
        // La invitación se crea con el código apagado, que no pide correo.
        var (admin, _, adminId) = await AdministrationAsync();
        var (subject, accountId) = await SeedInCenterAsync(adminId, "FAMILIAR");
        await AddFamilyRecordAsync(accountId, null);
        var sheet = await TextAsync(await IssueAsync(admin, subject, "/Administracion/Usuarios"));
        var client = Client(app);
        var opened = await FollowAsync(client, await client.GetAsync(InvitationUrl(sheet)));

        var chosen = await ChoosePasswordAsync(client, InvitationUrl(sheet));

        Assert.Contains("después te pediremos un código", opened);
        Assert.False(OpensSession(chosen));
        Assert.Contains(AccesoController.PasswordSavedNotice + AccesoController.FamilyNoAddressMessage, await FollowAsync(client, chosen));
        Assert.Empty(mailbox.Sent);
    }

    [Fact]
    public async Task Recuperacion_ConCuentaBloqueadaYSesionAbierta_CierraLaSesion_Desbloquea_YPideCodigo()
    {
        var mailbox = new Mailbox();
        var app = WithFamilyCode(mailbox);
        var (admin, _, adminId) = await AdministrationAsync(app);
        var (subject, accountId) = await SeedInCenterAsync(adminId, "FAMILIAR");
        await AddFamilyRecordAsync(accountId, $"{subject}@example.test");
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<CredentialAccount>>();
            Assert.True((await users.AddPasswordAsync((await users.FindByNameAsync(subject))!, Password)).Succeeded);
        }

        var open = Client(app);
        await EnterAsync(open, subject, Password);
        Assert.True(OpensSession(await TypeCodeAsync(open, mailbox.LastCode)));
        await ExecuteSqlAsync("UPDATE dbo.credenciales SET codigo_correo_bloqueado_en = SYSUTCDATETIME() WHERE cuenta_id = @accountId", new { accountId });
        var sheet = await TextAsync(await IssueAsync(admin, subject, "/Administracion/Usuarios"));
        var recovered = Client(app);
        const string NewPassword = "otra contraseña de prueba larga";

        var chosen = await ChoosePasswordAsync(recovered, InvitationUrl(sheet), NewPassword);

        Assert.True(SesionTests.RedirectsToLogin(await open.GetAsync("/ProfileScope/Select")));
        await AssertGoesToCodeAsync(recovered, chosen);
        Assert.Equal(2, mailbox.Sent.Count);
        Assert.True(OpensSession(await TypeCodeAsync(recovered, mailbox.LastCode)));
    }

    [Fact]
    public async Task UnPasoPendiente_NoValeEnLasPantallasDelOtro_ConSusCookiesPuestas()
    {
        var (family, _, _) = await SeedFamilyWithEmailAsync();
        var (staff, _, _) = await SeedWithTwoFactorAsync();
        var mailbox = new Mailbox();
        var clock = new TestClock();
        var app = WithFamilyCode(mailbox, clock);

        foreach (var path in new[] { "/Acceso/SegundoFactor", "/Acceso/CodigoRecuperacion", "/Acceso/ActivarSegundoFactor" })
        {
            var familyClient = Client(app);
            await AssertGoesToCodeAsync(familyClient, await EnterAsync(familyClient, family, Password));
            Assert.Equal(HttpStatusCode.OK, (await familyClient.GetAsync("/Acceso/CodigoCorreo")).StatusCode);

            var crossed = await familyClient.GetAsync(path);

            Assert.Equal("/Acceso/Entrar", crossed.Headers.Location!.OriginalString);
            Assert.False(OpensSession(crossed));
            clock.Advance(TimeSpan.FromSeconds(61));
        }

        var staffClient = Client(app);
        await EnterAsync(staffClient, staff, Password);
        Assert.Equal(HttpStatusCode.OK, (await staffClient.GetAsync("/Acceso/SegundoFactor")).StatusCode);

        var staffCrossed = await staffClient.GetAsync("/Acceso/CodigoCorreo");

        Assert.Equal("/Acceso/Entrar", staffCrossed.Headers.Location!.OriginalString);
        Assert.Equal(3, mailbox.Sent.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LasPantallasSoloHablanDelCodigo_CuandoEstaEncendido(bool on)
    {
        var app = on ? WithFamilyCode(new Mailbox()) : factory;
        var (admin, _, _) = await AdministrationAsync(app);

        var request = WebUtility.HtmlDecode(await Client(app).GetStringAsync("/Acceso/Solicitar"));
        var admission = WebUtility.HtmlDecode(await admin.GetStringAsync("/Residents/Create"));

        Assert.Equal(on, request.Contains("Si eres familiar y no te llega el código por correo"));
        Assert.Equal(on, admission.Contains("Correo para entrar en el portal y para avisos de ResidApp (opcional)"));
        Assert.Equal(on, admission.Contains("Hace falta para que esta persona entre en el portal"));
    }
}
