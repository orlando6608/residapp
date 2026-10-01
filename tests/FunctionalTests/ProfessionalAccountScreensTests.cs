using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Dapper;
using ResidApp.Domain.Accounts;
using ResidApp.Infrastructure.Persistence;
using ResidApp.Web.Models;

namespace ResidApp.FunctionalTests;

/// <summary>ADM-12/ADM-13 (0023) a través de ResidApp.Web de verdad: alta de usuario, ficha y lista, en español, y la
/// propia cuenta sin acciones.</summary>
public class ProfessionalAccountScreensTests : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    private readonly ResidentsFlowTests.WebAppFactory _factory;

    public ProfessionalAccountScreensTests(ResidentsFlowTests.WebAppFactory factory) => _factory = factory;

    [Fact]
    public void Estado_SeMuestraEnEspañol()
    {
        Assert.Equal("Activa", ProfessionalAccountDisplay.Status(AccountStatus.Active));
        Assert.Equal("Suspendida", ProfessionalAccountDisplay.Status(AccountStatus.Suspended));
        Assert.Equal(["Auxiliar", "Enfermería", "Medicina", "Administración", "Dirección Clínica"],
            ProfessionalAccountDisplay.ProfileOptions(ProfessionalAccount.GrantableProfiles).Select(o => o.Text));
    }

    [Fact]
    public async Task Alta_LlevaALaFichaDeLaCuenta_YLaPropiaCuentaNoTieneAcciones()
    {
        var admin = await SeedAdministratorAsync();
        var client = _factory.CreateClient();
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = admin.ExternalSubject,
        }))).EnsureSuccessStatusCode();

        var ownPage = WebUtility.HtmlDecode(await client.GetStringAsync($"/Administracion/Usuario?cuentaId={admin.AccountId}"));
        var newPage = await client.GetStringAsync("/Administracion/NuevoUsuario");
        var subject = $"func-alta-{Guid.NewGuid():N}"[..30];
        async Task<HttpResponseMessage> PostAsync(Dictionary<string, string> fields) => await client.PostAsync(
            "/Administracion/NuevoUsuario", new FormUrlEncodedContent(new Dictionary<string, string>(fields)
            {
                ["__RequestVerificationToken"] = ExtractValue(newPage, "__RequestVerificationToken"),
                ["Form.OperacionId"] = ExtractValue(newPage, "Form.OperacionId"),
            }));
        var empty = WebUtility.HtmlDecode(await (await PostAsync([])).Content.ReadAsStringAsync());
        var created = WebUtility.HtmlDecode(await (await PostAsync(new()
        {
            ["Form.Identificador"] = subject,
            ["Form.NombreVisible"] = "Ana Ruiz (ficticia)",
            ["Form.Perfil"] = "Enfermeria",
            ["Form.Unidades"] = admin.UnitId.ToString(),
        })).Content.ReadAsStringAsync());
        var list = WebUtility.HtmlDecode(await client.GetStringAsync("/Administracion/Usuarios"));

        Assert.Contains("Es tu propia cuenta", ownPage);
        Assert.DoesNotContain("Suspender cuenta", ownPage);
        Assert.DoesNotContain("Conceder perfil", ownPage);
        Assert.Contains("<option value=\"Enfermeria\">Enfermería</option>", WebUtility.HtmlDecode(newPage));
        Assert.DoesNotContain("Familiar", newPage);
        Assert.Matches($"value=\"{admin.UnitId}\"[^>]*checked=\"checked\"", newPage);
        Assert.Contains("Escribe el identificador de acceso.", empty);
        Assert.Contains("Escribe el nombre.", empty);
        Assert.Contains("Elige el perfil.", empty);
        Assert.Contains("Elige al menos una unidad.", empty);
        Assert.DoesNotContain("The ", empty);
        Assert.Contains("Usuario dado de alta.", created);
        Assert.Contains(subject, created);
        Assert.Contains("Suspender cuenta", created);
        Assert.Contains("Conceder perfil", created);
        Assert.Matches("<th scope=\"row\">Enfermería</th>", created);
        Assert.Contains("Ana Ruiz (ficticia)", list);
        Assert.Contains("Enfermería · ", list);
    }

    private static string ExtractValue(string html, string inputName) =>
        Regex.Match(html, $"name=\"{Regex.Escape(inputName)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

    [Fact]
    public async Task Inicio_EnfermeriaSoloVeElAltaDeResidenteConElPermiso()
    {
        async Task<string> HomeAsync(string? permission)
        {
            var nurse = await SeedAdministratorAsync("ENFERMERIA", permission);
            var client = _factory.CreateClient();
            var loginPage = await client.GetStringAsync("/DevAuth/Login");
            (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
                ["externalSubject"] = nurse.ExternalSubject,
            }))).EnsureSuccessStatusCode();
            return WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        }

        var without = await HomeAsync(null);
        var with = await HomeAsync(ProfilePermissions.ResidentIdentityCreate);

        Assert.All(new[] { without, with }, page => Assert.Contains("<h2 class=\"h5 card-title\">Enfermería</h2>", page));
        Assert.DoesNotContain("Alta de residente", without);
        Assert.Contains("Alta de residente", with);
    }

    /// <summary>Una cuenta con un ámbito del perfil indicado (por defecto Administración) en un centro y una unidad nuevos y,
    /// si se pide, un permiso de ese ámbito.</summary>
    private static async Task<(string ExternalSubject, Guid AccountId, Guid UnitId)> SeedAdministratorAsync(
        string profileCode = "ADMINISTRACION", string? permission = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var accountId = Guid.NewGuid();
        var centerId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var profileScopeId = Guid.NewGuid();
        var externalSubject = $"functional-{suffix}";
        var now = DateTimeOffset.UtcNow.UtcDateTime;

        using var connection = await new SqlConnectionFactory(ResidentsFlowTests.WebAppFactory.TestConnectionString).OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @externalSubject, 'ACTIVE', @now);
            INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en) VALUES (@centerId, @centerCode, @centerCode, 'ACTIVE', @now);
            INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@unitId, @centerId, @unitCode, @unitCode, 'ACTIVE', @now);
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@profileScopeId, @accountId, @centerId, @profileCode, 'ACTIVE', @now, @accountId);
            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @profileScopeId, @centerId, @unitId, @now, @accountId);
            IF @permission IS NOT NULL
                INSERT INTO dbo.permisos_perfil (id, ambito_perfil_id, centro_id, permiso_codigo, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @profileScopeId, @centerId, @permission, @now, @accountId);
            """, new
        {
            accountId, externalSubject, now, centerId, centerCode = $"FUNC-CENTER-{suffix}", unitId, unitCode = $"FUNC-UNIT-{suffix}",
            profileScopeId, profileCode, permission,
        });
        return (externalSubject, accountId, unitId);
    }
}
