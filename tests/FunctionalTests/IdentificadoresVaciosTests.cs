using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ResidApp.Infrastructure.Persistence;
using static ResidApp.FunctionalTests.ResidentsFlowTests;

namespace ResidApp.FunctionalTests;

/// <summary>Una dirección o un formulario sin identificador (Guid.Empty) es una petición mal formada: vuelve a Inicio, no da un 500.</summary>
public class IdentificadoresVaciosTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static string ExtractValue(string html, string inputName) =>
        Regex.Match(html, $"name=\"{inputName}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

    private async Task<HttpClient> AdminAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var accountId = Guid.NewGuid();
        var centerId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        var subject = $"vacios-{suffix}";
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        using (var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @subject, 'ACTIVE', @now);
                INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en) VALUES (@centerId, @code, @code, 'ACTIVE', @now);
                INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@unitId, @centerId, @unitCode, @unitCode, 'ACTIVE', @now);
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (@scopeId, @accountId, @centerId, 'ADMINISTRACION', 'ACTIVE', @now, @accountId);
                INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @scopeId, @centerId, @unitId, @now, @accountId);
                """, new { accountId, subject, centerId, unitId, scopeId, now, code = $"VACIOS-CENTER-{suffix}", unitCode = $"VACIOS-UNIT-{suffix}" });
        }

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SesionTests.PostLoginAsync(client, subject);
        // Con un único ámbito, entrar a Inicio lo selecciona.
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/ProfileScope/Select")).StatusCode);
        return client;
    }

    [Fact]
    public async Task UnaDireccionSinIdentificador_VuelveAInicio_YNoDaUn500()
    {
        var admin = await AdminAsync();

        var response = await admin.GetAsync("/Administracion/Usuario");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task UnFormularioSinIdentificador_VuelveAInicio_YNoDaUn500()
    {
        var admin = await AdminAsync();
        var page = await admin.GetStringAsync("/Administracion/Residentes");

        var response = await admin.PostAsync("/Administracion/AutorizacionFamiliar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"), ["Form.Cambio"] = "Abrir",
        }));

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task NingunaRutaGetDaUn500_CuandoFaltanLosIdentificadores()
    {
        // Barrido: todas las rutas GET con los identificadores sin rellenar. Antes, cada una que los usara sin comprobar daba un 500.
        var admin = await AdminAsync();
        var routes = factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(a => a.ActionConstraints?.OfType<HttpMethodActionConstraint>().All(c => c.HttpMethods.Contains("GET")) ?? true)
            .Select(a => (a.ControllerName, a.ActionName))
            .Distinct()
            .ToList();

        var visited = 0;
        foreach (var (controller, action) in routes)
        {
            // Salir de sesión y los ficheros descargables no son pantallas de lectura con identificadores.
            if (controller is "AccesoCj" || action is "Logout" or "Salir")
            {
                continue;
            }

            var response = await admin.GetAsync($"/{controller}/{action}");
            visited++;
            Assert.False(response.StatusCode == HttpStatusCode.InternalServerError, $"{controller}/{action} dio un 500 sin identificadores");
        }

        Assert.True(visited > 50, $"El barrido solo visitó {visited} rutas.");
    }
}
