using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using ResidApp.Infrastructure.Persistence;
using static ResidApp.FunctionalTests.ResidentsFlowTests;

namespace ResidApp.FunctionalTests;

/// <summary>ADR 0010 por HTTP: Administración analiza, previsualiza e importa un fichero (que viaja en cada paso y no se guarda), descarga la
/// plantilla de ResidApp y exporta en CSV; Enfermería no puede, y un fichero ilegible se explica.</summary>
public class IntercambioScreensTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static string ExtractValue(string html, string inputName) =>
        Regex.Match(html, $"name=\"{Regex.Escape(inputName)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

    private static async Task<string> BodyAsync(HttpResponseMessage response) => WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    private async Task<HttpClient> LoginAsync(string subject)
    {
        var client = factory.CreateClient();
        (await SesionTests.PostLoginAsync(client, subject)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<(string Admin, string Nurse, string Unit)> SeedAsync()
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
            adminId, nurseId, admin = $"intercambio-adm-{suffix}", nurse = $"intercambio-enf-{suffix}", centerId, unitId, adminScope, nurseScope,
            now, code = $"INTERCAMBIO-CENTER-{suffix}", unitCode = $"INTERCAMBIO-UNIT-{suffix}",
        });
        return ($"intercambio-adm-{suffix}", $"intercambio-enf-{suffix}", $"INTERCAMBIO-UNIT-{suffix}");
    }

    /// <summary>Lo que envía intercambio.js: el token, la entidad, el fichero y, después del análisis, lo elegido.</summary>
    private static async Task<HttpResponseMessage> PostStepAsync(
        HttpClient client, string action, string token, byte[] file, Dictionary<string, string>? fields = null, string entity = "Estructura",
        params int[] confirmed)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent(entity), "Entidad" },
        };
        foreach (var row in confirmed)
        {
            content.Add(new StringContent(row.ToString()), "Confirmadas");
        }

        var fileContent = new ByteArrayContent(file);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "Fichero", "estructura.csv");
        foreach (var (name, value) in fields ?? [])
        {
            content.Add(new StringContent(value), name);
        }

        return await client.PostAsync($"/Administracion/{action}", content);
    }

    [Fact]
    public async Task Administracion_AnalizaPrevisualizaEImporta_YExporta()
    {
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.Admin);
        var page = await admin.GetStringAsync("/Administracion/Importar");
        var token = ExtractValue(page, "__RequestVerificationToken");
        var file = Encoding.UTF8.GetBytes($"Unidad;Hab.;Cama\r\n{seed.Unit};101;A\r\n");
        var chosen = new Dictionary<string, string>
        {
            ["ConEmparejamiento"] = "true", ["Campos[0]"] = "UNIT", ["Campos[1]"] = "ROOM", ["Campos[2]"] = "PLACE", ["FormatoFecha"] = "DMY",
        };

        var analysis = await BodyAsync(await PostStepAsync(admin, "ImportarAnalizar", token, file));
        var preview = await BodyAsync(await PostStepAsync(admin, "ImportarVistaPrevia", token, file, chosen));
        var run = await BodyAsync(await PostStepAsync(admin, "ImportarEjecutar", token, file, chosen));
        var template = await admin.GetAsync("/Administracion/PlantillaImportacion?entidad=Residentes");
        var exportPage = await admin.GetStringAsync("/Administracion/Exportar");
        var export = await admin.PostAsync("/Administracion/Exportar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(exportPage, "__RequestVerificationToken"), ["entidad"] = "Estructura",
        }));

        Assert.Contains("Columnas del fichero", analysis);
        Assert.Contains("Emparejado automáticamente", analysis);
        Assert.Contains("Vista previa", preview);
        Assert.Contains("Importar 1 fila", preview);
        Assert.Contains("Resultado de la importación", run);
        Assert.Contains("Se crea la habitación «101», la plaza «A».", run);
        Assert.Equal("text/csv", template.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Nombre;Fecha de nacimiento;Sexo;Unidad;Habitación;Plaza (cama);Referencia interna", await template.Content.ReadAsStringAsync());
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        Assert.Contains($"{seed.Unit};{seed.Unit};;;101;A", await export.Content.ReadAsStringAsync());
        Assert.Contains("Contiene datos personales:", exportPage);
        Assert.Contains("guárdalo en un lugar seguro y bórralo cuando termines.", exportPage);
    }

    /// <summary>CJ, importación 1.2 y 1.4: un residente con el mismo nombre y fecha de nacimiento pero otra referencia se avisa y se
    /// confirma fila a fila con «Importar de todos modos»; la vista previa cuenta los que quedarán con el sexo «No consta».</summary>
    [Fact]
    public async Task Administracion_VistaPreviaDeResidentes_AvisaDelPosibleDuplicado_ConLaCasillaYElContadorDeNoConsta()
    {
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.Admin);
        var token = ExtractValue(await admin.GetStringAsync("/Administracion/Importar"), "__RequestVerificationToken");
        var chosen = new Dictionary<string, string>
        {
            ["ConEmparejamiento"] = "true", ["Campos[0]"] = "NAME", ["Campos[1]"] = "BIRTH_DATE", ["Campos[2]"] = "UNIT",
            ["Campos[3]"] = "INTERNAL_REFERENCE", ["FormatoFecha"] = "DMY",
        };
        byte[] File(string reference) => Encoding.UTF8.GetBytes($"Nombre;F. Nacimiento;Unidad;Referencia\r\nMarta Gil;01/02/1939;{seed.Unit};{reference}\r\n");
        await PostStepAsync(admin, "ImportarEjecutar", token, File("M-1"), chosen, "Residentes");

        var warned = await BodyAsync(await PostStepAsync(admin, "ImportarVistaPrevia", token, File("M-2"), chosen, "Residentes"));
        var confirmed = await BodyAsync(await PostStepAsync(admin, "ImportarVistaPrevia", token, File("M-2"), chosen, "Residentes", 2));

        Assert.Contains("Posible duplicado", warned);
        Assert.Contains("Importar de todos modos", warned);
        Assert.Contains("name=\"Confirmadas\" value=\"2\"", warned);
        Assert.DoesNotContain("checked", warned);
        Assert.Contains("No hay ninguna fila que importar.", warned);
        Assert.Contains("1 residente quedará con el sexo «No consta»", confirmed);
        Assert.Contains("Importar 1 fila", confirmed);
        Assert.Matches("name=\"Confirmadas\" value=\"2\"[^>]*checked", confirmed);
    }

    /// <summary>Guardar una plantilla de exportación redirige a Exportar con su nombre en la dirección (no en TempData: intercambio.js sigue
    /// la redirección con fetch y la consumiría), y la pantalla la lista con su mensaje.</summary>
    [Fact]
    public async Task GuardarPlantillaDeExportacion_VuelveAExportarConElMensaje()
    {
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.Admin);
        var token = ExtractValue(await admin.GetStringAsync("/Administracion/Exportar"), "__RequestVerificationToken");
        var sample = Encoding.UTF8.GetBytes("UNIDAD,CAMA\r\n");

        var saved = await PostStepAsync(admin, "ExportarPlantillaGuardar", token, sample, new Dictionary<string, string>
        {
            ["ConEmparejamiento"] = "true", ["Campos[0]"] = "UNIT", ["Campos[1]"] = "PLACE", ["FormatoFecha"] = "DMY", ["NombrePlantilla"] = " Destino ",
        });
        var page = await BodyAsync(saved);

        Assert.Equal("/Administracion/Exportar?plantillaGuardada=Destino", saved.RequestMessage!.RequestUri!.PathAndQuery);
        Assert.Contains("Plantilla «Destino» guardada.", page);
        Assert.Contains("Estructura del centro · 2 columnas", page);
    }

    [Fact]
    public async Task Enfermeria_NoPuede_YUnFicheroIlegibleSeExplica()
    {
        var seed = await SeedAsync();
        var nurse = await LoginAsync(seed.Nurse);
        var admin = await LoginAsync(seed.Admin);
        var nurseToken = ExtractValue(await nurse.GetStringAsync("/Administracion/Importar"), "__RequestVerificationToken");
        var adminToken = ExtractValue(await admin.GetStringAsync("/Administracion/Importar"), "__RequestVerificationToken");

        var denied = await PostStepAsync(nurse, "ImportarAnalizar", nurseToken, Encoding.UTF8.GetBytes("Unidad\r\nNorte\r\n"));
        var unreadable = await PostStepAsync(admin, "ImportarAnalizar", adminToken, [0x00, 0x01, 0x02]);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("ámbito activo de Administración", await BodyAsync(denied));
        Assert.Equal(HttpStatusCode.BadRequest, unreadable.StatusCode);
        Assert.Contains("No se puede leer el fichero", await BodyAsync(unreadable));
    }
}
