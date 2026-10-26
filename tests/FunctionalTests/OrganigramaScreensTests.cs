using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using ResidApp.Infrastructure.Persistence;
using static ResidApp.FunctionalTests.ResidentsFlowTests;

namespace ResidApp.FunctionalTests;

/// <summary>ADM-07 por HTTP: Administración carga la plantilla de CJ, crea un cargo y pone a una persona sin cuenta; Enfermería lo consulta en
/// solo lectura y no puede gestionarlo; la familia no lo ve.</summary>
public class OrganigramaScreensTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static string Token(string html) =>
        Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

    private async Task<HttpClient> LoginAsync(string subject)
    {
        var client = factory.CreateClient();
        (await SesionTests.PostLoginAsync(client, subject)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<(string Admin, string Nurse, string Family, string Unit, Guid CenterId)> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var ids = new { Admin = Guid.NewGuid(), Nurse = Guid.NewGuid(), Family = Guid.NewGuid(), Center = Guid.NewGuid(), Unit = Guid.NewGuid() };
        var scopes = new { Admin = Guid.NewGuid(), Nurse = Guid.NewGuid(), Family = Guid.NewGuid() };
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en)
            VALUES (@adminId, @admin, 'ACTIVE', @now), (@nurseId, @nurse, 'ACTIVE', @now), (@familyId, @family, 'ACTIVE', @now);
            INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en) VALUES (@centerId, @code, @code, 'ACTIVE', @now);
            INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@unitId, @centerId, @unitCode, @unitCode, 'ACTIVE', @now);
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@adminScope, @adminId, @centerId, 'ADMINISTRACION', 'ACTIVE', @now, @adminId),
                   (@nurseScope, @nurseId, @centerId, 'ENFERMERIA', 'ACTIVE', @now, @adminId),
                   (@familyScope, @familyId, @centerId, 'FAMILIAR', 'ACTIVE', @now, @adminId);
            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @adminScope, @centerId, @unitId, @now, @adminId), (NEWID(), @nurseScope, @centerId, @unitId, @now, @adminId),
                   (NEWID(), @familyScope, @centerId, @unitId, @now, @adminId);
            """, new
        {
            adminId = ids.Admin, nurseId = ids.Nurse, familyId = ids.Family, admin = $"organigrama-adm-{suffix}", nurse = $"organigrama-enf-{suffix}",
            family = $"organigrama-fam-{suffix}", centerId = ids.Center, unitId = ids.Unit, adminScope = scopes.Admin, nurseScope = scopes.Nurse,
            familyScope = scopes.Family, now, code = $"ORGANIGRAMA-CENTER-{suffix}", unitCode = $"ORGANIGRAMA-UNIT-{suffix}",
        });
        return ($"organigrama-adm-{suffix}", $"organigrama-enf-{suffix}", $"organigrama-fam-{suffix}", $"ORGANIGRAMA-UNIT-{suffix}", ids.Center);
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response) => WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string token, Dictionary<string, string> fields) =>
        client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = token }));

    [Fact]
    public async Task Administracion_CargaLaPlantilla_CreaUnCargo_PoneUnaPersona_YEnfermeriaLoConsultaSinGestionar()
    {
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.Admin);

        var empty = await BodyAsync(await admin.GetAsync("/Administracion/Cargos"));
        var loaded = await BodyAsync(await PostAsync(admin, "/Administracion/CargarPlantilla", Token(empty), []));
        var form = await BodyAsync(await admin.GetAsync("/Administracion/NuevoCargo"));
        var created = await BodyAsync(await PostAsync(admin, "/Administracion/NuevoCargo", Token(form), new Dictionary<string, string>
        {
            ["Form.OperacionId"] = Regex.Match(form, "name=\"Form.OperacionId\"[^>]*value=\"([^\"]*)\"").Groups[1].Value,
            ["Form.Nombre"] = "Terapia ocupacional", ["Form.UnaPersona"] = "false", ["Form.Unidades"] = await UnitIdAsync(seed.Unit),
        }));
        var positionId = await PositionIdAsync(seed.CenterId, "Terapia ocupacional");
        var occupy = await BodyAsync(await admin.GetAsync($"/Administracion/OcuparCargo?cargoId={positionId}"));
        var occupied = await BodyAsync(await PostAsync(admin, "/Administracion/OcuparCargo", Token(occupy), new Dictionary<string, string>
        {
            ["Form.CargoId"] = positionId.ToString(), ["Form.Nombre"] = "Rosa Gil",
        }));

        Assert.Contains("Cargar la plantilla", empty);
        Assert.DoesNotContain("Cargar la plantilla", loaded);
        Assert.Contains("Plantilla cargada", loaded);
        Assert.Contains("Dirección del centro", loaded);
        Assert.Contains("Cargo creado", created);
        Assert.Contains("Terapia ocupacional", created);
        Assert.Contains("Persona puesta en el cargo", occupied);
        Assert.Contains("Rosa Gil", occupied);
        Assert.Contains("(sin cuenta)", occupied);

        var nurse = await LoginAsync(seed.Nurse);
        var read = await BodyAsync(await nurse.GetAsync("/Organigrama"));
        var manage = await BodyAsync(await nurse.GetAsync("/Administracion/Cargos"));
        Assert.Contains("Organigrama del centro", read);
        Assert.Contains("Terapia ocupacional", read);
        Assert.Contains("Rosa Gil", read);
        Assert.Contains("Dirección del centro", read);
        Assert.DoesNotContain("Poner una persona", read);
        Assert.DoesNotContain("gestionar los cargos", read);
        Assert.Contains("href=\"/Organigrama\"", read);
        Assert.Contains("No se puede acceder a esta operación.", manage);
        Assert.DoesNotContain("Terapia ocupacional", manage);
        Assert.DoesNotContain("Nuevo cargo", manage);
    }

    [Fact]
    public async Task Administracion_VeElEnlaceParaGestionar_YUnCicloSeExplica()
    {
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.Admin);
        var empty = await BodyAsync(await admin.GetAsync("/Administracion/Cargos"));
        await PostAsync(admin, "/Administracion/CargarPlantilla", Token(empty), []);
        var direction = await PositionIdAsync(seed.CenterId, "Dirección del centro");
        var edit = await BodyAsync(await admin.GetAsync($"/Administracion/EditarCargo?cargoId={direction}"));
        var enfermeria = await PositionIdAsync(seed.CenterId, "Enfermería");

        var cycle = await BodyAsync(await PostAsync(admin, "/Administracion/EditarCargo", Token(edit), new Dictionary<string, string>
        {
            ["Form.CargoId"] = direction.ToString(), ["Form.Nombre"] = "Dirección del centro", ["Form.UnaPersona"] = "true",
            ["Form.SuperiorId"] = enfermeria.ToString(),
        }));
        var reading = await BodyAsync(await admin.GetAsync("/Organigrama"));

        // Enfermería depende (a través de la Coordinación) de la Dirección: no sale como posible superior de esta, y si se fuerza, se explica.
        Assert.DoesNotContain($"<option value=\"{enfermeria}\"", edit);
        Assert.Contains("no puede depender de sí mismo ni de uno de sus subordinados", cycle);
        Assert.Contains("gestionar los cargos", reading);
    }

    [Fact]
    public async Task Familia_NoVeElOrganigrama()
    {
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.Admin);
        var empty = await BodyAsync(await admin.GetAsync("/Administracion/Cargos"));
        await PostAsync(admin, "/Administracion/CargarPlantilla", Token(empty), []);

        var family = await LoginAsync(seed.Family);
        var response = await family.GetAsync("/Organigrama");
        var page = await BodyAsync(response);

        // La familia vuelve a su inicio (el portal familiar): ni el organigrama, ni su título, ni el enlace.
        Assert.NotEqual("/Organigrama", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("Dirección del centro", page);
        Assert.DoesNotContain("Organigrama del centro", page);
        Assert.DoesNotContain("href=\"/Organigrama\"", page);
    }

    private static async Task<string> UnitIdAsync(string unitCode)
    {
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        return (await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.unidades WHERE codigo = @unitCode", new { unitCode })).ToString();
    }

    private static async Task<Guid> PositionIdAsync(Guid centerId, string name)
    {
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        return await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.cargos WHERE centro_id = @centerId AND nombre = @name", new { centerId, name });
    }
}
