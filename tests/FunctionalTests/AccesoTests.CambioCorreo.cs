using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using ResidApp.Application.Errors;
using ResidApp.Infrastructure.Persistence;
using ResidApp.Web.Controllers;

namespace ResidApp.FunctionalTests;

/// <summary>
/// Administración y la familia que entra con código, con la app entera y, en la CI, con el usuario limitado de la base de datos (ADR 0012, pieza 3; script 0122): sin correo no hay
/// acceso ni invitación; cambiar la dirección echa a la familia, avisa a la anterior y manda el siguiente código a la nueva; y la familia ve su última entrada. Apagado, las
/// pantallas son las de siempre.
/// </summary>
public partial class AccesoTests
{
    private sealed record AdminFamily(HttpClient Admin, Guid ResidentId, Guid LinkId)
    {
        public string Edit => $"/Administracion/EditarFamiliar?residenteId={ResidentId}&vinculoId={LinkId}";

        public string Authorization => $"/Administracion/AutorizacionFamiliar?residenteId={ResidentId}&vinculoId={LinkId}";
    }

    /// <summary>Una Administración que da de alta, por la app, un residente con una familiar sin correo.</summary>
    private async Task<AdminFamily> ResidentWithFamilyAsync(WebApplicationFactory<Program> app)
    {
        var (admin, _, adminId) = await AdministrationAsync(app);
        using var connection = await new SqlConnectionFactory(ResidentsFlowTests.WebAppFactory.TestConnectionString).OpenAsync();
        var (scopeId, centerId, unitId) = await connection.QuerySingleAsync<(Guid ScopeId, Guid CenterId, Guid UnitId)>("""
            SELECT TOP (1) profile.id, profile.centro_id, unit.unidad_id
              FROM dbo.ambitos_perfil profile JOIN dbo.ambitos_perfil_unidad unit ON unit.ambito_perfil_id = profile.id
             WHERE profile.cuenta_id = @adminId
            """, new { adminId });
        var created = await TextAsync(await PostAsync(admin, "/Residents/Create", new()
        {
            ["OperacionId"] = Guid.NewGuid().ToString(), ["AmbitoPerfilId"] = scopeId.ToString(), ["CentroId"] = centerId.ToString(), ["UnidadId"] = unitId.ToString(),
            ["NombreVisible"] = "Residente Correo Funcional", ["FechaNacimiento"] = "1938-02-20", ["SexoDocumentadoCodigo"] = "Hombre",
            ["Familiares[0].NombreVisible"] = "Hija Correo Funcional", ["Familiares[0].Relacion"] = "Hija", ["Familiares[0].Telefono"] = "600123456",
            ["Familiares[0].ContactoPrioritario"] = "true",
        }, "/Residents/Create"));
        var residentId = Guid.Parse(Regex.Match(created, "Id del residente: <code>([0-9a-f-]{36})</code>").Groups[1].Value);
        var linkId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.residentes_familiares WHERE residente_id = @residentId", new { residentId });
        return new AdminFamily(admin, residentId, linkId);
    }

    private static async Task<string> SaveEmailAsync(AdminFamily family, string email)
    {
        var page = await family.Admin.GetStringAsync(family.Edit);
        return await TextAsync(await PostAsync(family.Admin, "/Administracion/EditarFamiliar", new()
        {
            ["Form.ResidenteId"] = family.ResidentId.ToString(), ["Form.VinculoId"] = family.LinkId.ToString(), ["Form.Version"] = SesionTests.ExtractValue(page, "Form.Version"),
            ["Form.NombreVisible"] = "Hija Correo Funcional", ["Form.Relacion"] = "Hija", ["Form.Telefono"] = "600123456", ["Form.Correo"] = email,
        }, family.Edit));
    }

    private static async Task<string> GrantAccessAsync(AdminFamily family, string subject) =>
        await TextAsync(await PostAsync(family.Admin, "/Administracion/AccesoPortalFamiliar", new()
        {
            ["residenteId"] = family.ResidentId.ToString(), ["vinculoId"] = family.LinkId.ToString(), ["usuario"] = subject,
        }, family.Authorization));

    private static async Task AuthorizeAsync(AdminFamily family)
    {
        foreach (var (change, number) in new[] { ("Abrir", 0), ("Activar", 1) })
        {
            await PostAsync(family.Admin, "/Administracion/AutorizacionFamiliar", new()
            {
                ["Form.ResidenteId"] = family.ResidentId.ToString(), ["Form.VinculoId"] = family.LinkId.ToString(), ["Form.CambiosEsperados"] = number.ToString(),
                ["Form.Cambio"] = change,
            }, family.Authorization);
        }
    }

    /// <summary>El portal del residente, tras elegir el único ámbito (como hace la app al entrar).</summary>
    private static async Task<string> PortalAsync(HttpClient client, Guid linkId)
    {
        await client.GetAsync("/ProfileScope/Select");
        return WebUtility.HtmlDecode(await client.GetStringAsync($"/Familia/Residente/{linkId}"));
    }

    [Fact]
    public async Task ConElCodigo_AdministracionNecesitaElCorreo_YCambiarloEchaALaFamiliaYAvisaALaAnterior()
    {
        var mailbox = new Mailbox();
        var clock = new TestClock();
        var app = WithFamilyCode(mailbox, clock);
        var family = await ResidentWithFamilyAsync(app);
        var subject = $"familia-{Guid.NewGuid():N}"[..20];
        var oldEmail = $"{subject}@example.test";
        var newEmail = $"nueva-{subject}@example.test";

        var warned = WebUtility.HtmlDecode(await family.Admin.GetStringAsync(family.Authorization));
        var refused = await GrantAccessAsync(family, subject);
        var added = await SaveEmailAsync(family, oldEmail);
        var granted = await GrantAccessAsync(family, subject);
        await AuthorizeAsync(family);
        var editPage = WebUtility.HtmlDecode(await family.Admin.GetStringAsync(family.Edit));
        var sheet = await TextAsync(await IssueAsync(family.Admin, subject, family.Authorization));

        Assert.Contains("Para entrar necesita un correo en su ficha", warned);
        Assert.Contains(ApplicationResultRunner.FamilyEmailRequiredMessage, refused);
        Assert.Contains("Datos del familiar guardados.", added);
        Assert.DoesNotContain("Aviso enviado", added);
        Assert.Contains("Acceso al Portal Familiar creado", granted);
        Assert.Contains("Esta persona entra con un código enviado a este correo", editPage);
        Assert.Contains("Correo para entrar en el portal y para avisos de ResidApp", editPage);
        Assert.Contains("aria-describedby=\"aviso-correo-acceso ayuda-correo\"", editPage);
        Assert.DoesNotContain("Este correo solo sirve", editPage);
        Assert.Contains("necesitas además un código", sheet);

        // La familia usa la invitación y entra con el código.
        var relative = Client(app);
        await ChoosePasswordAsync(relative, InvitationUrl(sheet));
        Assert.Equal(oldEmail, mailbox.Sent[^1].To);
        Assert.True(OpensSession(await TypeCodeAsync(relative, mailbox.LastCode)));
        var firstVisit = await PortalAsync(relative, family.LinkId);
        Assert.Contains("No consta otra entrada.", firstVisit);

        // Administración cambia la dirección: la sesión de la familia se cierra y la anterior recibe el aviso.
        var changed = await SaveEmailAsync(family, newEmail);

        Assert.Contains("Aviso enviado a la dirección anterior; no podemos asegurar que llegue.", changed);
        Assert.True(SesionTests.RedirectsToLogin(await relative.GetAsync($"/Familia/Residente/{family.LinkId}")));
        Assert.Equal((oldEmail, "Cambio en tu cuenta de ResidApp"), (mailbox.Sent[^1].To, mailbox.Sent[^1].Subject));
        Assert.DoesNotContain(newEmail, mailbox.Sent[^1].PlainText);

        // El siguiente código va a la nueva; un fallo se ve en «Mi cuenta» como del correo, y el portal enseña la entrada anterior.
        clock.Advance(TimeSpan.FromSeconds(61));
        var again = Client(app);
        await EnterAsync(again, subject, Password);
        Assert.Equal(newEmail, mailbox.Sent[^1].To);
        await TypeCodeAsync(again, mailbox.WrongCode);
        Assert.True(OpensSession(await TypeCodeAsync(again, mailbox.LastCode)));
        var secondVisit = await PortalAsync(again, family.LinkId);
        var account = WebUtility.HtmlDecode(await again.GetStringAsync("/Cuenta"));

        Assert.Contains("Última entrada, sin contar esta:", secondVisit);
        Assert.Contains("Código del correo incorrecto", account);
        Assert.DoesNotContain("Código del móvil incorrecto", account);
        Assert.Contains("Entras con tu contraseña y un código que te llega por correo", account);
    }

    [Fact]
    public async Task ConElCodigoApagado_LasPantallasSonLasDeSiempre()
    {
        var family = await ResidentWithFamilyAsync(factory);
        var subject = $"familia-{Guid.NewGuid():N}"[..20];

        var granted = await GrantAccessAsync(family, subject);
        var editPage = WebUtility.HtmlDecode(await family.Admin.GetStringAsync(family.Edit));
        var saved = await SaveEmailAsync(family, $"{subject}@example.test");
        var sheet = await TextAsync(await IssueAsync(family.Admin, subject, family.Authorization));

        Assert.Contains("Acceso al Portal Familiar creado", granted);
        Assert.Contains("Este correo solo sirve", editPage);
        Assert.DoesNotContain("código enviado a este correo", editPage);
        Assert.DoesNotContain("Aviso enviado", saved);
        Assert.DoesNotContain("necesitas además un código", sheet);
    }
}
