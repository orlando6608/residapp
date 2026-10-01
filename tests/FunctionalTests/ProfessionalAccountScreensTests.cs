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

    [Fact]
    public async Task Estructura_AdministracionCreaYRenombraUnaUnidad_YOtroPerfilNoEntra()
    {
        var admin = await SeedAdministratorAsync();
        var client = _factory.CreateClient();
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = admin.ExternalSubject,
        }))).EnsureSuccessStatusCode();

        var newPage = await client.GetStringAsync("/Administracion/NuevaUnidad");
        async Task<string> PostAsync(string path, string page, Dictionary<string, string> fields) =>
            WebUtility.HtmlDecode(await (await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>(fields)
            {
                ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"),
            }))).Content.ReadAsStringAsync());
        var code = $"func-{Guid.NewGuid():N}"[..16];
        var empty = await PostAsync("/Administracion/NuevaUnidad", newPage, new() { ["Form.OperacionId"] = ExtractValue(newPage, "Form.OperacionId") });
        var created = await PostAsync("/Administracion/NuevaUnidad", newPage, new()
        {
            ["Form.OperacionId"] = ExtractValue(newPage, "Form.OperacionId"),
            ["Form.Codigo"] = code,
            ["Form.Nombre"] = "Planta Norte (ficticia)",
        });
        var listPage = await client.GetStringAsync("/Administracion/Estructura");
        var unitId = Regex.Match(listPage, "name=\"unidadId\" value=\"([0-9a-f-]{36})\"").Groups[1].Value;
        var renamePage = await client.GetStringAsync($"/Administracion/NombreUnidad?unidadId={unitId}");
        var renamed = await PostAsync("/Administracion/NombreUnidad", renamePage, new()
        {
            ["Form.UnidadId"] = unitId,
            ["Form.Nombre"] = "Planta Norte renombrada (ficticia)",
        });
        var inactivated = await PostAsync("/Administracion/EstadoUnidad", renamed, new() { ["unidadId"] = unitId, ["activa"] = "false" });
        var again = await PostAsync("/Administracion/EstadoUnidad", inactivated, new() { ["unidadId"] = unitId, ["activa"] = "false" });
        var nursePage = await PageAsync("ENFERMERIA", null, "/Administracion/Estructura");

        Assert.Contains("Escribe el código.", empty);
        Assert.Contains("Escribe el nombre.", empty);
        Assert.DoesNotContain("The ", empty);
        Assert.Contains("Unidad creada.", created);
        Assert.Contains("Planta Norte (ficticia)", created);
        Assert.Contains(code.ToUpperInvariant(), created);
        Assert.Contains("Activa", created);
        Assert.Contains("Nombre guardado.", renamed);
        Assert.Contains("Planta Norte renombrada (ficticia)", renamed);
        Assert.Contains("Unidad inactivada", inactivated);
        Assert.Contains("Inactiva", inactivated);
        Assert.Contains("Reactivar", inactivated);
        Assert.Contains("La unidad ya estaba en ese estado.", again);
        Assert.Contains("No se puede acceder a esta operación", nursePage);
        Assert.DoesNotContain("Nueva unidad", nursePage);
    }

    [Fact]
    public async Task Plataforma_CreaUnCentro_ElInicioSoloLaOfreceAEsePerfil_YOtroPerfilNoEntra()
    {
        var operatorSubject = await SeedPlatformOperatorAsync();
        var client = _factory.CreateClient();
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = operatorSubject,
        }))).EnsureSuccessStatusCode();

        var home = WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        var newPage = await client.GetStringAsync("/Plataforma/NuevoCentro");
        async Task<string> PostAsync(Dictionary<string, string> fields) =>
            WebUtility.HtmlDecode(await (await client.PostAsync("/Plataforma/NuevoCentro", new FormUrlEncodedContent(new Dictionary<string, string>(fields)
            {
                ["__RequestVerificationToken"] = ExtractValue(newPage, "__RequestVerificationToken"),
                ["Form.OperacionId"] = ExtractValue(newPage, "Form.OperacionId"),
            }))).Content.ReadAsStringAsync());
        var empty = await PostAsync([]);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var created = await PostAsync(new()
        {
            ["Form.CodigoCentro"] = $"func-{suffix}",
            ["Form.NombreCentro"] = "Residencia Funcional (ficticia)",
            ["Form.CodigoUnidad"] = "planta-1",
            ["Form.NombreUnidad"] = "Planta 1",
            ["Form.IdentificadorAdministrador"] = $"func-admin-{suffix}",
            ["Form.NombreAdministrador"] = "Admin Funcional (ficticio)",
        });
        var repeated = await PostAsync(new()
        {
            ["Form.CodigoCentro"] = $"func2-{suffix}",
            ["Form.NombreCentro"] = "Otra residencia",
            ["Form.CodigoUnidad"] = "planta-1",
            ["Form.NombreUnidad"] = "Planta 1",
            ["Form.IdentificadorAdministrador"] = $"func-admin-{suffix}",
            ["Form.NombreAdministrador"] = "Admin Funcional (ficticio)",
        });
        var adminHome = await PageAsync("ADMINISTRACION", null);
        var adminPlatform = await PageAsync("ADMINISTRACION", null, "/Plataforma");

        Assert.Contains("Dar de alta un centro con su primera unidad y su administrador.", home);
        Assert.Contains("Escribe el código del centro.", empty);
        Assert.Contains("Escribe el identificador de acceso del administrador.", empty);
        Assert.DoesNotContain("The ", empty);
        Assert.Contains("Centro creado.", created);
        Assert.Contains("Residencia Funcional (ficticia)", created);
        Assert.Contains($"FUNC-{suffix}".ToUpperInvariant(), created);
        Assert.Contains("Ya existe un centro con ese código o una cuenta con ese identificador de acceso.", repeated);
        Assert.DoesNotContain("Dar de alta un centro con su primera unidad", adminHome);
        Assert.Contains("No se puede acceder a esta operación", adminPlatform);
        Assert.DoesNotContain("Nuevo centro", adminPlatform);
    }

    /// <summary>Una cuenta con el perfil Plataforma, que vive en el centro reservado.</summary>
    private static async Task<string> SeedPlatformOperatorAsync()
    {
        var accountId = Guid.NewGuid();
        var subject = $"functional-plat-{Guid.NewGuid():N}"[..30];
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        using var connection = await new SqlConnectionFactory(ResidentsFlowTests.WebAppFactory.TestConnectionString).OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @subject, 'ACTIVE', @now);
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @accountId, '5F3A1C00-0000-4000-8000-000000000001', 'PLATAFORMA', 'ACTIVE', @now, @accountId);
            """, new { accountId, subject, now });
        return subject;
    }

    [Fact]
    public async Task Auditoria_MuestraLaAccionEnEspañol_IgnoraFiltrosMalFormados_YEnfermeriaNoEntra()
    {
        var admin = await SeedAdministratorAsync();
        var client = _factory.CreateClient();
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = admin.ExternalSubject,
        }))).EnsureSuccessStatusCode();
        var emptyPage = WebUtility.HtmlDecode(await client.GetStringAsync("/Administracion/Auditoria"));
        var newPage = await client.GetStringAsync("/Administracion/NuevaUnidad");
        (await client.PostAsync("/Administracion/NuevaUnidad", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(newPage, "__RequestVerificationToken"),
            ["Form.OperacionId"] = ExtractValue(newPage, "Form.OperacionId"),
            ["Form.Codigo"] = $"aud-{Guid.NewGuid():N}"[..14],
            ["Form.Nombre"] = "Unidad de la auditoría (ficticia)",
        }))).EnsureSuccessStatusCode();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/Administracion/Auditoria"));
        var malformed = WebUtility.HtmlDecode(await client.GetStringAsync("/Administracion/Auditoria?desde=xyz&hasta=2026-99-99&accion=HACK&cuenta=nope"));
        var filtered = WebUtility.HtmlDecode(await client.GetStringAsync("/Administracion/Auditoria?accion=ACCOUNT_CREATE"));
        var reversed = WebUtility.HtmlDecode(await client.GetStringAsync("/Administracion/Auditoria?desde=2026-10-01&hasta=2026-09-01"));
        var nursePage = await PageAsync("ENFERMERIA", null, "/Administracion/Auditoria");

        Assert.Contains("Auditoría administrativa", emptyPage);
        Assert.Contains("Ningún evento coincide con la búsqueda.", emptyPage);
        Assert.Contains("Unidad creada", page);
        Assert.Contains("Unidad de la auditoría (ficticia)", page);
        Assert.Contains("<optgroup label=\"Estructura del centro\">", page);
        Assert.DoesNotContain(">UNIT_CREATE<", page);
        Assert.Contains("Unidad creada", malformed);
        Assert.DoesNotContain("The ", malformed);
        Assert.Contains("Ningún evento coincide con la búsqueda.", filtered);
        Assert.Contains("La fecha «desde» no puede ser posterior a la fecha «hasta».", reversed);
        Assert.Contains("No se puede acceder a esta operación", nursePage);
        Assert.DoesNotContain("<table", nursePage);
    }

    private static string ExtractValue(string html, string inputName) =>
        Regex.Match(html, $"name=\"{Regex.Escape(inputName)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

    [Fact]
    public async Task Inicio_EnfermeriaSoloVeElAltaDeResidenteConElPermiso()
    {
        var without = await PageAsync("ENFERMERIA", null);
        var with = await PageAsync("ENFERMERIA", ProfilePermissions.ResidentIdentityCreate);

        Assert.All(new[] { without, with }, page => Assert.Contains("<h2 class=\"h5 card-title\">Enfermería</h2>", page));
        Assert.DoesNotContain("Alta de residente", without);
        Assert.Contains("Alta de residente", with);
    }

    [Theory]
    [InlineData("ENFERMERIA", ProfilePermissions.BaselineInitialComplete)]
    [InlineData("MEDICINA", ProfilePermissions.BaselineReevaluate)]
    public async Task Inicio_FirmarBorradorDeBasal_SoloConUnPermisoDeBasal(string profileCode, string permission)
    {
        var without = await PageAsync(profileCode, null);
        var with = await PageAsync(profileCode, permission);

        Assert.DoesNotContain("Firmar borrador de basal", without);
        Assert.Contains("Firmar borrador de basal", with);
    }

    [Theory]
    [InlineData("ENFERMERIA", null, false)]
    [InlineData("ENFERMERIA", ProfilePermissions.ResidentIdentityCreate, true)]
    [InlineData("MEDICINA", null, false)]
    [InlineData("ADMINISTRACION", null, true)]
    public async Task AltaDeResidente_ElFormularioSoloSaleSiElAmbitoPuedeDarDeAlta(
        string profileCode, string? permission, bool canCreate)
    {
        var page = await PageAsync(profileCode, permission, "/Residents/Create");

        Assert.Equal(canCreate, page.Contains("name=\"NombreVisible\""));
        Assert.Equal(!canCreate, page.Contains("Tu ámbito activo no tiene permiso para dar de alta residentes."));
    }

    /// <summary>Una página (por defecto el Inicio), ya decodificada, de una cuenta nueva con un ámbito del perfil indicado
    /// y, si se pide, un permiso.</summary>
    private async Task<string> PageAsync(string profileCode, string? permission, string path = "/")
    {
        var account = await SeedAdministratorAsync(profileCode, permission);
        var client = _factory.CreateClient();
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = account.ExternalSubject,
        }))).EnsureSuccessStatusCode();
        return WebUtility.HtmlDecode(await client.GetStringAsync(path));
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
