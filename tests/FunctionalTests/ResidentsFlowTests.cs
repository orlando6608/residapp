using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using ResidApp.Infrastructure.Persistence;

namespace ResidApp.FunctionalTests;

/// <summary>Camino feliz a través de ResidApp.Web de verdad (Kestrel de pruebas + pipeline HTTP real):
/// identidad de desarrollo (DevAuth) y alta de residente (Residents/Create). Automatiza lo verificado
/// manualmente por curl durante el cableado inicial de la Web.</summary>
public class ResidentsFlowTests : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public ResidentsFlowTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task LoginAndCreateResident_GoldenPath_Succeeds()
    {
        var seed = await SeedAsync();
        var client = _factory.CreateClient();

        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        var loginResponse = await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = seed.ExternalSubject,
        }));
        loginResponse.EnsureSuccessStatusCode();

        var createPage = await client.GetStringAsync("/Residents/Create");

        var response = await client.PostAsync("/Residents/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(createPage, "__RequestVerificationToken"),
            ["OperacionId"] = ExtractValue(createPage, "OperacionId"),
            ["AmbitoPerfilId"] = seed.ProfileScopeId.ToString(),
            ["CentroId"] = seed.CenterId.ToString(),
            ["UnidadId"] = seed.UnitId.ToString(),
            ["NombreVisible"] = "Residente Funcional",
            ["FechaNacimiento"] = "1938-02-20",
            ["SexoDocumentadoCodigo"] = "Hombre",
        }));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Id del residente", body);
    }

    [Fact]
    public async Task Create_LaUnidadSeEligeEntreLasDelAmbito_YSusErroresSalenEnEspañol()
    {
        var seed = await SeedAsync();
        var client = _factory.CreateClient();
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = seed.ExternalSubject,
        }))).EnsureSuccessStatusCode();

        // Con una sola unidad, ya viene elegida.
        var singleUnitPage = await client.GetStringAsync("/Residents/Create");
        var secondUnitId = await GrantUnitAsync(seed, "Planta funcional dos");
        var createPage = await client.GetStringAsync("/Residents/Create");
        async Task<string> PostWithUnitAsync(string unit) => WebUtility.HtmlDecode(await (await client.PostAsync(
            "/Residents/Create", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractValue(createPage, "__RequestVerificationToken"),
                ["OperacionId"] = ExtractValue(createPage, "OperacionId"),
                ["AmbitoPerfilId"] = seed.ProfileScopeId.ToString(),
                ["CentroId"] = seed.CenterId.ToString(),
                ["UnidadId"] = unit,
                ["NombreVisible"] = "Residente Funcional",
                ["FechaNacimiento"] = "1938-02-20",
                ["SexoDocumentadoCodigo"] = "Hombre",
            }))).Content.ReadAsStringAsync());
        var empty = await PostWithUnitAsync("");
        var malformed = await PostWithUnitAsync("no-es-un-guid");

        Assert.Contains($"<option selected=\"selected\" value=\"{seed.UnitId}\">", singleUnitPage);
        Assert.DoesNotContain("Elige una unidad", singleUnitPage);
        Assert.Contains("<option value=\"\">Elige una unidad</option>", createPage);
        Assert.Contains($"<option value=\"{secondUnitId}\">Planta funcional dos</option>", createPage);
        Assert.Contains($"<option value=\"{seed.UnitId}\">", createPage);
        Assert.DoesNotContain(Guid.Empty.ToString(), createPage);
        Assert.Contains("Elige la unidad.", WebUtility.HtmlDecode(createPage));
        Assert.Contains("Elige la unidad.", empty);
        Assert.Contains("«no-es-un-guid» no es un valor válido para Unidad.", malformed);
        // Los campos ocultos (ámbito, centro, operación) conservan su mensaje por defecto: no los rellena el usuario.
        foreach (var field in new[] { "Unidad", "Nombre completo", "Fecha de nacimiento", "Sexo documentado", "Referencia interna" })
        {
            Assert.DoesNotContain($"The {field} field", createPage + empty + malformed);
        }

        Assert.DoesNotContain("The field ", createPage);
        Assert.DoesNotContain("is not valid", empty + malformed);
    }

    private static async Task<Guid> GrantUnitAsync((string ExternalSubject, Guid ProfileScopeId, Guid CenterId, Guid UnitId) seed, string name)
    {
        var unitId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@unitId, @centerId, @code, @name, 'ACTIVE', @now)",
            new { unitId, centerId = seed.CenterId, code = $"FUNC-UNIT-{unitId:N}"[..30], name, now });
        await connection.ExecuteAsync("""
            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            SELECT NEWID(), id, centro_id, @unitId, @now, cuenta_id FROM dbo.ambitos_perfil WHERE id = @profileScopeId
            """, new { unitId, now, profileScopeId = seed.ProfileScopeId });
        return unitId;
    }

    private static string ExtractValue(string html, string inputName) =>
        Regex.Match(html, $"name=\"{inputName}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

    private async Task<(string ExternalSubject, Guid ProfileScopeId, Guid CenterId, Guid UnitId)> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var accountId = Guid.NewGuid();
        var centerId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var profileScopeId = Guid.NewGuid();
        var externalSubject = $"functional-{suffix}";
        var now = DateTimeOffset.UtcNow.UtcDateTime;

        var connections = new SqlConnectionFactory(WebAppFactory.TestConnectionString);
        using var connection = await connections.OpenAsync();

        await connection.ExecuteAsync(
            "INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @externalSubject, 'ACTIVE', @now)",
            new { accountId, externalSubject, now });
        await connection.ExecuteAsync(
            "INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en) VALUES (@centerId, @code, @code, 'ACTIVE', @now)",
            new { centerId, code = $"FUNC-CENTER-{suffix}", now });
        await connection.ExecuteAsync(
            "INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@unitId, @centerId, @code, @code, 'ACTIVE', @now)",
            new { unitId, centerId, code = $"FUNC-UNIT-{suffix}", now });
        await connection.ExecuteAsync("""
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@profileScopeId, @accountId, @centerId, 'ADMINISTRACION', 'ACTIVE', @now, @accountId)
            """, new { profileScopeId, accountId, centerId, now });
        await connection.ExecuteAsync("""
            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            VALUES (@id, @profileScopeId, @centerId, @unitId, @now, @accountId)
            """, new { id = Guid.NewGuid(), profileScopeId, centerId, unitId, now, accountId });

        return (externalSubject, profileScopeId, centerId, unitId);
    }

    public class WebAppFactory : WebApplicationFactory<Program>
    {
        public static string TestConnectionString =>
            Environment.GetEnvironmentVariable("RESIDAPP_TEST_CONNECTION_STRING")
            ?? "Server=ACER-ORLANDO;Database=ResidApp;Integrated Security=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

        // Program.cs lee ConnectionStrings:ResidApp directamente sobre builder.Configuration antes de
        // builder.Build() (falla rápido si falta). Las sobrescrituras de WebApplicationFactory vía
        // ConfigureWebHost/ConfigureAppConfiguration solo se aplican en el momento de Build(), demasiado
        // tarde para ese chequeo — por eso se fija aquí como variable de entorno del proceso, que
        // CreateBuilder(args) sí incorpora desde el arranque, sea cual sea el entorno (Development o no).
        public WebAppFactory() =>
            Environment.SetEnvironmentVariable("ConnectionStrings__ResidApp", TestConnectionString);
    }
}
