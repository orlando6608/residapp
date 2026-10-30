using System.Net;
using System.Text.RegularExpressions;

namespace ResidApp.FunctionalTests;

/// <summary>
/// ADR 0007, incremento I2b: la Administración de un centro da un perfil a alguien que ya tiene cuenta en otro. Emite una hoja sin decir a
/// quién; la persona la usa desde «Mi cuenta» con su segundo factor; la Administración confirma. Hasta confirmar no hay ningún perfil, y
/// otra Administración no ve ni resuelve la vinculación.
/// </summary>
public partial class AccesoTests
{
    private static Task<string> AdminUnitAsync(Guid adminAccountId) => ScalarAsync<string>("""
        SELECT TOP (1) CAST(unit.unidad_id AS NVARCHAR(36))
          FROM dbo.ambitos_perfil profile JOIN dbo.ambitos_perfil_unidad unit ON unit.ambito_perfil_id = profile.id
         WHERE profile.cuenta_id = @adminAccountId
        """, new { adminAccountId });

    private static Task<string> AdminCenterAsync(Guid adminAccountId) => ScalarAsync<string>(
        "SELECT TOP (1) CAST(centro_id AS NVARCHAR(36)) FROM dbo.ambitos_perfil WHERE cuenta_id = @adminAccountId", new { adminAccountId });

    private static Task<int> ScopesOfAsync(Guid accountId, string centerId) => ScalarAsync<int>(
        "SELECT COUNT(*) FROM dbo.ambitos_perfil WHERE cuenta_id = @accountId AND centro_id = @centerId AND perfil_codigo = 'ENFERMERIA' AND estado = 'ACTIVE'",
        new { accountId, centerId });

    /// <summary>La Administración emite una hoja de Enfermería en su unidad y devuelve el token de la dirección de la hoja.</summary>
    private static async Task<string> IssueLinkSheetAsync(HttpClient admin, Guid adminAccountId)
    {
        var response = await PostAsync(admin, "/Administracion/NuevaVinculacion", new()
        {
            ["Form.Perfil"] = "Enfermeria", ["Form.Unidades"] = await AdminUnitAsync(adminAccountId),
        }, "/Administracion/Vinculaciones");
        var html = await TextAsync(response);
        Assert.Contains("Hoja de acceso a otro centro", html);
        return Regex.Match(html, @"/Cuenta/Vincular/([A-Za-z0-9_-]+)").Groups[1].Value;
    }

    [Fact]
    public async Task Vinculacion_ConfirmadaPorLaAdministracion_DaElPerfilYElSelectorLoOfrece()
    {
        var (admin, _, adminAccountId) = await AdministrationAsync();
        var (subject, accountId, _) = await SeedWithTwoFactorAsync();
        var centerId = await AdminCenterAsync(adminAccountId);
        var token = await IssueLinkSheetAsync(admin, adminAccountId);
        var person = await SignedInWithTotpAsync(subject, accountId);

        var offer = await TextAsync(await person.GetAsync($"/Cuenta/Vincular/{token}"));
        var accepted = await PostAsync(person, "/Cuenta/AceptarVinculacion", new() { ["token"] = token }, "/Cuenta");
        var afterAccepting = await TextAsync(await person.GetAsync("/Cuenta"));
        var scopesBefore = await ScopesOfAsync(accountId, centerId);
        var pendingList = await TextAsync(await admin.GetAsync("/Administracion/Vinculaciones"));
        var linkId = await ScalarAsync<string>("SELECT CAST(id AS NVARCHAR(36)) FROM dbo.vinculaciones_cuenta WHERE centro_id = @centerId", new { centerId });
        await PostAsync(admin, "/Administracion/ResolverVinculacion", new() { ["id"] = linkId, ["confirmar"] = "true" }, "/Administracion/Vinculaciones");
        var reused = await person.GetAsync($"/Cuenta/Vincular/{token}");

        Assert.Contains("Acceso a ", offer);
        Assert.Contains("Enfermería", offer);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.Contains("Has aceptado el acceso", afterAccepting);
        Assert.Equal(0, scopesBefore);
        Assert.Contains(subject, pendingList);
        Assert.Contains("Confirmar", pendingList);
        Assert.Equal(1, await ScopesOfAsync(accountId, centerId));
        Assert.Equal(1, await EventsAsync(accountId, "VINCULACION_ACEPTADA"));
        Assert.Contains("no vale", await TextAsync(reused));

        // La próxima vez que entra, el selector le ofrece los dos centros.
        var again = await SignedInWithTotpAsync(subject, accountId);
        var selector = await TextAsync(await again.GetAsync("/ProfileScope/Select"));
        Assert.Equal(2, Regex.Matches(selector, "name=\"profileScopeId\"").Count);
        var changing = await TextAsync(await again.GetAsync("/ProfileScope/Select?cambiar=true"));
        Assert.Equal(2, Regex.Matches(changing, "name=\"profileScopeId\"").Count);
        Assert.DoesNotContain("Solo tienes un ámbito activo", changing);
    }

    [Fact]
    public async Task Vinculacion_RechazadaOSinConfirmar_NoDaNingunPerfil()
    {
        var (admin, _, adminAccountId) = await AdministrationAsync();
        var (subject, accountId, _) = await SeedWithTwoFactorAsync();
        var centerId = await AdminCenterAsync(adminAccountId);
        var token = await IssueLinkSheetAsync(admin, adminAccountId);
        var person = await SignedInWithTotpAsync(subject, accountId);
        await PostAsync(person, "/Cuenta/AceptarVinculacion", new() { ["token"] = token }, "/Cuenta");
        var linkId = await ScalarAsync<string>("SELECT CAST(id AS NVARCHAR(36)) FROM dbo.vinculaciones_cuenta WHERE centro_id = @centerId", new { centerId });

        await PostAsync(admin, "/Administracion/ResolverVinculacion", new() { ["id"] = linkId, ["confirmar"] = "false" }, "/Administracion/Vinculaciones");
        await PostAsync(admin, "/Administracion/ResolverVinculacion", new() { ["id"] = linkId, ["confirmar"] = "true" }, "/Administracion/Vinculaciones");

        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.vinculaciones_cuenta WHERE id = @linkId AND resolucion = 'RECHAZADA'", new { linkId }));
        Assert.Equal(0, await ScopesOfAsync(accountId, centerId));
    }

    [Fact]
    public async Task Vinculacion_SePuedePegarElCodigoOLaDireccionEnteraEnMiCuenta()
    {
        var (admin, _, adminAccountId) = await AdministrationAsync();
        var (subject, accountId, _) = await SeedWithTwoFactorAsync();
        var token = await IssueLinkSheetAsync(admin, adminAccountId);
        var person = await SignedInWithTotpAsync(subject, accountId);

        var byCode = await TextAsync(await PostAsync(person, "/Cuenta/Vincular", new() { ["codigo"] = token }, "/Cuenta"));
        var byAddress = await TextAsync(await PostAsync(person, "/Cuenta/Vincular", new() { ["codigo"] = $"https://localhost/Cuenta/Vincular/{token}" }, "/Cuenta"));
        var wrong = await TextAsync(await PostAsync(person, "/Cuenta/Vincular", new() { ["codigo"] = "no-es-un-codigo" }, "/Cuenta"));

        Assert.Contains("name=\"token\"", byCode);
        Assert.Contains("name=\"token\"", byAddress);
        Assert.Contains("Ese código no vale", wrong);
        Assert.DoesNotContain("name=\"token\"", wrong);
    }

    [Fact]
    public async Task Vinculacion_SinSegundoFactor_NoSePuedeAceptar()
    {
        var (admin, _, adminAccountId) = await AdministrationAsync();
        var (subject, accountId) = await SeedWithPasswordAsync("FAMILIAR");
        var centerId = await AdminCenterAsync(adminAccountId);
        var token = await IssueLinkSheetAsync(admin, adminAccountId);
        var person = await SignedInAsync(subject);

        var page = await TextAsync(await person.GetAsync("/Cuenta"));
        var view = await TextAsync(await person.GetAsync($"/Cuenta/Vincular/{token}"));
        var accepted = await TextAsync(await PostAsync(person, "/Cuenta/AceptarVinculacion", new() { ["token"] = token }, "/Cuenta"));

        Assert.DoesNotContain("Acceso a otro centro", page);
        Assert.Contains("es para el personal", view);
        Assert.Contains("es para el personal", accepted);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.vinculaciones_cuenta WHERE centro_id = @centerId AND canjeada_en IS NOT NULL", new { centerId }));
        Assert.Equal(0, await ScopesOfAsync(accountId, centerId));
    }

    [Fact]
    public async Task Vinculacion_OtraAdministracion_NoLaVeNiLaResuelve()
    {
        var (admin, _, adminAccountId) = await AdministrationAsync();
        var (other, _, _) = await AdministrationAsync();
        var (subject, accountId, _) = await SeedWithTwoFactorAsync();
        var centerId = await AdminCenterAsync(adminAccountId);
        var token = await IssueLinkSheetAsync(admin, adminAccountId);
        var person = await SignedInWithTotpAsync(subject, accountId);
        await PostAsync(person, "/Cuenta/AceptarVinculacion", new() { ["token"] = token }, "/Cuenta");
        var linkId = await ScalarAsync<string>("SELECT CAST(id AS NVARCHAR(36)) FROM dbo.vinculaciones_cuenta WHERE centro_id = @centerId", new { centerId });

        var list = await TextAsync(await other.GetAsync("/Administracion/Vinculaciones"));
        await PostAsync(other, "/Administracion/ResolverVinculacion", new() { ["id"] = linkId, ["confirmar"] = "true" }, "/Administracion/Vinculaciones");
        await PostAsync(other, "/Administracion/AnularVinculacion", new() { ["id"] = linkId }, "/Administracion/Vinculaciones");

        Assert.DoesNotContain(subject, list);
        Assert.Equal(0, await ScopesOfAsync(accountId, centerId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.vinculaciones_cuenta WHERE id = @linkId AND resuelta_en IS NULL AND anulada_en IS NULL", new { linkId }));
    }

    [Fact]
    public async Task Vinculacion_SinSesion_LlevaAlInicioDeSesion()
    {
        var client = Client();

        Assert.True(SesionTests.RedirectsToLogin(await client.GetAsync("/Cuenta/Vincular/cualquier-cosa")));
        Assert.True(SesionTests.RedirectsToLogin(await client.GetAsync("/Administracion/Vinculaciones")));
        Assert.True(SesionTests.RedirectsToLogin(await client.GetAsync("/Plataforma/Cuentas")));
    }
}
