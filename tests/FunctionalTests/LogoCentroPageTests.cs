using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Dapper;
using ResidApp.Domain.Platform;
using ResidApp.Infrastructure.Persistence;
using static ResidApp.FunctionalTests.ResidentsFlowTests;

namespace ResidApp.FunctionalTests;

/// <summary>Nombre y logo del centro por HTTP (script 0068): Administración los cambia desde «Datos del centro» y todo el personal del centro los ve
/// en la esquina superior izquierda y en la cabecera de impresión, con «Generado con ResidApp» al pie; Enfermería no puede cambiarlos; Plataforma y
/// quien no ha entrado ven la marca de ResidApp; Plataforma puede subir el logo al dar de alta un centro.</summary>
public class LogoCentroPageTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static string ExtractValue(string html, string inputName) =>
        Regex.Match(html, $"name=\"{Regex.Escape(inputName)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

    private static async Task<string> BodyAsync(HttpResponseMessage response) => WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    /// <summary>El interior del enlace de marca de la esquina superior izquierda.</summary>
    private static string Brand(string html) =>
        Regex.Match(html, "<a class=\"navbar-brand[^>]*>(.*?)</a>", RegexOptions.Singleline).Groups[1].Value;

    private async Task<HttpClient> LoginAsync(string subject)
    {
        var client = factory.CreateClient();
        (await SesionTests.PostLoginAsync(client, subject)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<(string Admin, string Nurse, string CenterCode)> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var adminId = Guid.NewGuid();
        var nurseId = Guid.NewGuid();
        var centerId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var adminScope = Guid.NewGuid();
        var nurseScope = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@adminId, @admin, 'ACTIVE', @now), (@nurseId, @nurse, 'ACTIVE', @now);
            INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en) VALUES (@centerId, @code, @code, 'ACTIVE', @now);
            INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@unitId, @centerId, @unitCode, @unitCode, 'ACTIVE', @now);
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@adminScope, @adminId, @centerId, 'ADMINISTRACION', 'ACTIVE', @now, @adminId),
                   (@nurseScope, @nurseId, @centerId, 'ENFERMERIA', 'ACTIVE', @now, @adminId);
            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @adminScope, @centerId, @unitId, @now, @adminId), (NEWID(), @nurseScope, @centerId, @unitId, @now, @adminId);
            """, new
        {
            adminId, nurseId, admin = $"logo-adm-{suffix}", nurse = $"logo-enf-{suffix}", centerId, unitId, adminScope, nurseScope, now,
            code = $"LOGO-CENTER-{suffix}", unitCode = $"LOGO-UNIT-{suffix}",
        });
        return ($"logo-adm-{suffix}", $"logo-enf-{suffix}", $"LOGO-CENTER-{suffix}");
    }

    private static async Task<HttpResponseMessage> PostLogoAsync(HttpClient client, string page, byte[] file, string contentType = "image/png")
    {
        using var form = new MultipartFormDataContent { { new StringContent(ExtractValue(page, "__RequestVerificationToken")), "__RequestVerificationToken" } };
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(content, "Form.Logo", "logo.png");
        return await client.PostAsync("/Administracion/LogoCentro", form);
    }

    [Fact]
    public async Task Administracion_CambiaNombreYLogo_ElPersonalDelCentroLosVeEnLaCabecera_YEnfermeriaNoPuedeCambiarlos()
    {
        var (adminSubject, nurseSubject, code) = await SeedAsync();
        var admin = await LoginAsync(adminSubject);
        var nurse = await LoginAsync(nurseSubject);

        var start = await BodyAsync(await admin.GetAsync("/Administracion/DatosCentro"));
        Assert.Contains("Datos del centro", start);
        Assert.Contains(code, Brand(start));
        Assert.Contains("/images/logo.svg", Brand(start));

        var renamed = await BodyAsync(await admin.PostAsync("/Administracion/DatosCentro", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(start, "__RequestVerificationToken"), ["Form.Nombre"] = "Residencia Los Almendros",
        })));
        var sameName = await BodyAsync(await admin.PostAsync("/Administracion/DatosCentro", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(renamed, "__RequestVerificationToken"), ["Form.Nombre"] = "Residencia Los Almendros",
        })));
        var invalid = await BodyAsync(await PostLogoAsync(admin, renamed, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray(), "image/svg+xml"));
        var withLogo = await BodyAsync(await PostLogoAsync(admin, invalid, Png));

        Assert.Contains("Nombre del centro guardado.", renamed);
        Assert.Contains("Residencia Los Almendros", Brand(renamed));
        Assert.DoesNotContain(code, Brand(renamed));
        Assert.Contains("Escribe un nombre distinto del actual", sameName);
        Assert.Contains("El logo tiene que ser una imagen PNG o JPEG", invalid);
        Assert.DoesNotContain("/CentroLogo/Ver", Brand(invalid));
        Assert.Contains("Logo guardado.", withLogo);
        var logoUrl = Regex.Match(Brand(withLogo), "/CentroLogo/Ver\\?v=\\d+").Value;
        Assert.NotEmpty(logoUrl);
        Assert.Contains("Residencia Los Almendros", Brand(withLogo));
        Assert.DoesNotContain("/images/logo.svg", Brand(withLogo));

        var logo = await admin.GetAsync(logoUrl);
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", Assert.Single(logo.Headers.GetValues("X-Content-Type-Options")));
        Assert.True(logo.Headers.CacheControl?.Private);
        Assert.Equal(Png, await logo.Content.ReadAsByteArrayAsync());

        // El resto del personal del centro ve el mismo encabezado y la cabecera de impresión con ResidApp al pie.
        var nurseHome = await BodyAsync(await nurse.GetAsync("/Enfermeria"));
        Assert.Contains("Residencia Los Almendros", Brand(nurseHome));
        Assert.Contains("/CentroLogo/Ver?v=", Brand(nurseHome));
        var printBlock = Regex.Match(nurseHome, "impresion-marca.*?impresion-pie", RegexOptions.Singleline).Value;
        Assert.Contains("Residencia Los Almendros", printBlock);
        Assert.Contains("/CentroLogo/Ver?v=", printBlock);
        Assert.Contains("Generado con ResidApp", nurseHome);
        Assert.Equal(HttpStatusCode.OK, (await nurse.GetAsync(logoUrl)).StatusCode);

        // Enfermería no cambia nada.
        var nurseScreen = await BodyAsync(await nurse.GetAsync("/Administracion/DatosCentro"));
        var nurseRename = await BodyAsync(await nurse.PostAsync("/Administracion/DatosCentro", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(nurseHome, "__RequestVerificationToken"), ["Form.Nombre"] = "Intruso",
        })));
        Assert.DoesNotContain("name=\"Form.Nombre\"", nurseScreen);
        Assert.DoesNotContain("Nombre del centro guardado.", nurseRename);
        Assert.Contains("Residencia Los Almendros", Brand(await BodyAsync(await admin.GetAsync("/Administracion"))));

        var removed = await BodyAsync(await admin.PostAsync("/Administracion/QuitarLogoCentro", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(withLogo, "__RequestVerificationToken"),
        })));
        Assert.Contains("Logo quitado.", removed);
        Assert.DoesNotContain("/CentroLogo/Ver", Brand(removed));
        Assert.Contains("/images/logo.svg", Brand(removed));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(logoUrl)).StatusCode);
    }

    [Fact]
    public async Task Plataforma_VeLaMarcaDeResidApp_PuedeSubirElLogoAlDarDeAltaUnCentro_YSinSesionElLogoNoSeSirve()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var accountId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var subject = $"logo-plat-{suffix}";
        using (var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.cuentas (id, sujeto_externo, nombre_visible, estado, creado_en) VALUES (@accountId, @subject, N'Operador de pruebas', 'ACTIVE', SYSUTCDATETIME());
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (@scopeId, @accountId, @centerId, 'PLATAFORMA', 'ACTIVE', SYSUTCDATETIME(), @accountId);
                """, new { accountId, subject, scopeId, centerId = PlatformCenter.Id });
        }
        var platform = await LoginAsync(subject);

        var form = await BodyAsync(await platform.GetAsync("/Plataforma/NuevoCentro"));
        using var content = new MultipartFormDataContent
        {
            { new StringContent(ExtractValue(form, "__RequestVerificationToken")), "__RequestVerificationToken" },
            { new StringContent(operationId.ToString()), "Form.OperacionId" },
            { new StringContent($"logo-{suffix}"), "Form.CodigoCentro" },
            { new StringContent("Residencia Con Logo (ficticia)"), "Form.NombreCentro" },
            { new StringContent($"u-{suffix}"), "Form.CodigoUnidad" },
            { new StringContent("Primera unidad"), "Form.NombreUnidad" },
            { new StringContent($"logo-adm-{suffix}"), "Form.IdentificadorAdministrador" },
            { new StringContent("Admin Nuevo"), "Form.NombreAdministrador" },
        };
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "Form.Logo", "logo.png");
        var created = await BodyAsync(await platform.PostAsync("/Plataforma/NuevoCentro", content));

        Assert.Contains("Centro creado.", created);
        // Plataforma ve siempre la marca de ResidApp, y no tiene logo que servir.
        Assert.Contains("ResidApp", Brand(created));
        Assert.Contains("/images/logo.svg", Brand(created));
        Assert.DoesNotContain("Generado con ResidApp", created);
        Assert.Equal(HttpStatusCode.NotFound, (await platform.GetAsync("/CentroLogo/Ver")).StatusCode);
        using var verification = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        Assert.Equal(Png, await verification.QuerySingleAsync<byte[]>("SELECT contenido FROM dbo.logos_centro WHERE centro_id = @operationId", new { operationId }));

        // Sin sesión, la marca es la de ResidApp y el logo no se sirve a nadie.
        var anonymous = factory.CreateClient();
        var login = await BodyAsync(await anonymous.GetAsync("/Acceso/Entrar"));
        Assert.Contains("ResidApp", Brand(login));
        Assert.Contains("/images/logo.svg", Brand(login));
        Assert.Contains("/Acceso/Entrar", (await anonymous.GetAsync("/CentroLogo/Ver")).RequestMessage!.RequestUri!.AbsolutePath);
    }
}
