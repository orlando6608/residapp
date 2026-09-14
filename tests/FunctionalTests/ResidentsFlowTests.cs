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
            ["SexoDocumentadoCodigo"] = "Male",
        }));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Id del residente", body);
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
