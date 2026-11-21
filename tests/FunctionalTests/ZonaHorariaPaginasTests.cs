using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using ResidApp.Infrastructure.Persistence;
using static ResidApp.FunctionalTests.ResidentsFlowTests;

namespace ResidApp.FunctionalTests;

/// <summary>
/// Zona horaria por centro (script 0109, F1), por HTTP y con SQL real: Plataforma la elige al crear el centro y la corrige después con motivo, confirmación de lo afectado y
/// auditoría; la Administración no puede; y las pantallas del centro muestran las horas en su zona. Canarias está en el catálogo pero inactiva hasta F3: estas pruebas la activan
/// mientras corren y la dejan como estaba.
/// </summary>
public class ZonaHorariaPaginasTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private const string Token = "__RequestVerificationToken";
    private const string Canarias = "Atlantic/Canary";
    private const string Madrid = "Europe/Madrid";

    private static SqlConnectionFactory Db => new(WebAppFactory.TestConnectionString);


    private static string ExtractValue(string html, string inputName) =>
        Regex.Match(html, $"name=\"{Regex.Escape(inputName)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

    private async Task<HttpClient> LoginPlatformAsync()
    {
        var accountId = Guid.NewGuid();
        var subject = $"zona-plat-{Guid.NewGuid():N}"[..28];
        using (var connection = await Db.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @subject, 'ACTIVE', SYSUTCDATETIME());
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @accountId, '5F3A1C00-0000-4000-8000-000000000001', 'PLATAFORMA', 'ACTIVE', SYSUTCDATETIME(), @accountId)
                """, new { accountId, subject });
        }

        return await LoginAsync(subject);
    }

    private async Task<HttpClient> LoginAsync(string subject)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Redirect, (await SesionTests.PostLoginAsync(client, subject)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/ProfileScope/Select")).StatusCode);
        return client;
    }

    /// <summary>Un centro con su Administración y un evento de auditoría con una hora UTC fija (12:00 del 15/01/2026) para ver en qué zona se muestra.</summary>
    private async Task<(Guid CenterId, HttpClient Admin)> SeedCenterWithAdminAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var accountId = Guid.NewGuid();
        var centerId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        var subject = $"zona-adm-{suffix}";
        using (var connection = await Db.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.cuentas (id, sujeto_externo, nombre_visible, estado, creado_en) VALUES (@accountId, @subject, N'Admin Zona (ficticia)', 'ACTIVE', SYSUTCDATETIME());
                INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en) VALUES (@centerId, @code, @code, 'ACTIVE', SYSUTCDATETIME());
                INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@unitId, @centerId, @unitCode, @unitCode, 'ACTIVE', SYSUTCDATETIME());
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (@scopeId, @accountId, @centerId, 'ADMINISTRACION', 'ACTIVE', SYSUTCDATETIME(), @accountId);
                INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @scopeId, @centerId, @unitId, SYSUTCDATETIME(), @accountId);
                INSERT INTO dbo.eventos_auditoria (id, cuenta_id, perfil_activo, centro_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
                VALUES (NEWID(), @accountId, 'ADMINISTRACION', @centerId, 'CENTER', @centerId, 'CENTER_RENAME', '2026-01-15T12:00:00');
                """, new { accountId, subject, centerId, unitId, scopeId, code = $"ZONA-CENTER-{suffix}", unitCode = $"ZONA-UNIT-{suffix}" });
        }

        return (centerId, await LoginAsync(subject));
    }

    private static async Task<string> AuditHoursAsync(HttpClient admin) =>
        WebUtility.HtmlDecode(await admin.GetStringAsync("/Administracion/Auditoria?Desde=2026-01-15&Hasta=2026-01-15"));

    private static async Task<string> PostAsync(HttpClient client, string page, string action, IEnumerable<KeyValuePair<string, string>> fields)
    {
        var html = await client.GetStringAsync(page);
        var all = new List<KeyValuePair<string, string>> { new(Token, ExtractValue(html, Token)) };
        all.AddRange(fields);
        var response = await client.PostAsync($"/Plataforma/{action}", new FormUrlEncodedContent(all));
        // Tras el POST se vuelve a la pantalla del centro: se sigue la redirección para leer su mensaje.
        return response.StatusCode == HttpStatusCode.Redirect
            ? WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location!))
            : WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static List<KeyValuePair<string, string>> ZoneForm(Guid centerId, string zone, string reason, int slots = 0, int appointments = 0, bool confirm = true)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("CentroId", centerId.ToString()), new("ZonaHoraria", zone), new("Motivo", reason), new("FranjasVistas", slots.ToString()), new("CitasVistas", appointments.ToString()),
        };
        if (confirm)
        {
            fields.Add(new("Confirmo", "true"));
        }

        return fields;
    }

    [Fact]
    public async Task Plataforma_elige_la_zona_al_crear_el_centro_y_una_zona_fuera_de_la_lista_no_lo_crea()
    {
        var platform = await LoginPlatformAsync();
        var page = WebUtility.HtmlDecode(await platform.GetStringAsync("/Plataforma/NuevoCentro"));
        Assert.Contains("Zona horaria del centro", page);
        Assert.Contains("Península y Baleares (Madrid)", page);
        Assert.Contains("Canarias", page);
        // Miami y México están en el catálogo pero inactivas (script 0112): Plataforma no las ofrece.
        Assert.DoesNotContain("Miami", page);
        Assert.DoesNotContain("Ciudad de México", page);
        Assert.Contains("después solo Plataforma puede corregirla", page);

        async Task<string> CreateAsync(string zone, string suffix)
        {
            var form = await platform.GetStringAsync("/Plataforma/NuevoCentro");
            var response = await platform.PostAsync("/Plataforma/NuevoCentro", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [Token] = ExtractValue(form, Token), ["Form.OperacionId"] = ExtractValue(form, "Form.OperacionId"),
                ["Form.NombreCentro"] = $"Residencia Zona {suffix} (ficticia)", ["Form.NombreUnidad"] = "Planta 1",
                ["Form.IdentificadorAdministrador"] = $"zona-admin-{suffix}", ["Form.NombreAdministrador"] = "Admin Zona (ficticio)", ["Form.ZonaHoraria"] = zone,
            }));
            return response.StatusCode == HttpStatusCode.Redirect ? "creado" : WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        }

        var okSuffix = Guid.NewGuid().ToString("N")[..10];
        var badSuffix = Guid.NewGuid().ToString("N")[..10];
        Assert.Equal("creado", await CreateAsync(Canarias, okSuffix));
        var rejected = await CreateAsync("Mars/Olympus", badSuffix);

        Assert.Contains("Revisa los datos", rejected);
        using var connection = await Db.OpenAsync();
        Assert.Equal(Canarias, await connection.ExecuteScalarAsync<string>(
            "SELECT zone.zona_codigo FROM dbo.zonas_centro zone JOIN dbo.centros center ON center.id = zone.centro_id WHERE center.nombre_visible = @name",
            new { name = $"Residencia Zona {okSuffix} (ficticia)" }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.centros WHERE nombre_visible = @name", new { name = $"Residencia Zona {badSuffix} (ficticia)" }));
    }

    [Fact]
    public async Task Plataforma_corrige_la_zona_de_un_centro_con_motivo_y_confirmacion_y_las_horas_del_centro_cambian_en_el_acto()
    {
        var platform = await LoginPlatformAsync();
        var (centerId, admin) = await SeedCenterWithAdminAsync();
        var centerPage = $"/Plataforma/Centro?centroId={centerId}";

        // Antes: la zona de siempre, y el centro ve la hora de la península (12:00 UTC de enero = 13:00).
        var before = WebUtility.HtmlDecode(await platform.GetStringAsync(centerPage));
        Assert.Contains("Zona horaria", before);
        Assert.Contains("Península y Baleares (Madrid)", before);
        Assert.Contains("Corregir la zona horaria", before);
        Assert.Contains("Sin cambios: el centro usa la zona de la península", before);
        var adminBefore = await AuditHoursAsync(admin);
        Assert.Contains("15/01/2026 13:00", adminBefore.Replace("15/1/2026", "15/01/2026"));

        // Sin confirmar lo afectado, no se cambia nada.
        var notConfirmed = await PostAsync(platform, centerPage, "CambiarZona", ZoneForm(centerId, Canarias, "ErrorDeAlta", confirm: false));
        Assert.Contains("Marca que has visto lo que se ve afectado", notConfirmed);
        // Con otros recuentos de los que vio, tampoco: pide confirmar otra vez.
        var stale = await PostAsync(platform, centerPage, "CambiarZona", ZoneForm(centerId, Canarias, "ErrorDeAlta", slots: 4));
        Assert.Contains("revisa los números", stale);
        using (var connection = await Db.OpenAsync())
        {
            Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.zonas_centro WHERE centro_id = @centerId", new { centerId }));
        }

        var changed = await PostAsync(platform, centerPage, "CambiarZona", ZoneForm(centerId, Canarias, "ErrorDeAlta"));

        Assert.Contains("Zona horaria cambiada.", changed);
        Assert.Contains("data-zona-actual=\"Atlantic/Canary\"", changed);
        Assert.Contains("Se eligió mal al dar de alta el centro", changed);
        Assert.Contains("Alta del centro", changed);
        // Después: el centro ve esa misma hora guardada en la zona nueva (12:00 UTC de enero = 12:00 en Canarias), sin esperar a que caduque nada.
        var adminAfter = await AuditHoursAsync(admin);
        Assert.Contains("15/01/2026 12:00", adminAfter.Replace("15/1/2026", "15/01/2026"));
        Assert.DoesNotContain("13:00", adminAfter);
        Assert.Contains("Zona horaria del centro cambiada", adminAfter);
    }

    [Fact]
    public async Task La_Administracion_del_centro_no_puede_corregir_su_zona_ni_ve_la_seccion()
    {
        var (centerId, admin) = await SeedCenterWithAdminAsync();

        var page = await admin.GetAsync($"/Plataforma/Centro?centroId={centerId}");
        var hours = await AuditHoursAsync(admin);
        var form = await admin.GetStringAsync("/Administracion/Auditoria");
        var post = await admin.PostAsync("/Plataforma/CambiarZona", new FormUrlEncodedContent(
            [new(Token, ExtractValue(form, Token)), .. ZoneForm(centerId, Canarias, "ErrorDeAlta")]));

        Assert.DoesNotContain("Corregir la zona horaria", WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync()));
        Assert.Contains("13:00", hours);
        using var connection = await Db.OpenAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.zonas_centro WHERE centro_id = @centerId", new { centerId }));
        Assert.NotEqual(HttpStatusCode.InternalServerError, post.StatusCode);
    }

    [Fact]
    public async Task Plataforma_sigue_en_la_hora_de_la_peninsula_aunque_haya_centros_en_otra_zona()
    {
        var platform = await LoginPlatformAsync();
        var (centerId, _) = await SeedCenterWithAdminAsync();
        await PostAsync(platform, $"/Plataforma/Centro?centroId={centerId}", "CambiarZona", ZoneForm(centerId, Canarias, "ErrorDeAlta"));

        // La historia del centro se enseña a Plataforma en la hora de Madrid: la fila de alta lleva la hora de «ahora», que no coincide con la de Canarias salvo en invierno.
        var page = WebUtility.HtmlDecode(await platform.GetStringAsync($"/Plataforma/Centro?centroId={centerId}"));
        var madrid = DateTimeOffset.UtcNow.ToOffset(TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid").GetUtcOffset(DateTimeOffset.UtcNow)).ToString("dd/MM/yyyy");
        Assert.Contains(madrid, page);
        Assert.Contains("data-zona-actual=\"Atlantic/Canary\"", page);
    }
}
