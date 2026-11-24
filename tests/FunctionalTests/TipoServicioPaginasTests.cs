using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using ResidApp.Infrastructure.Persistence;
using static ResidApp.FunctionalTests.ResidentsFlowTests;

namespace ResidApp.FunctionalTests;

/// <summary>
/// Tipo de servicio del centro (ADR 0017, script 0113), por HTTP y con SQL real: Plataforma lo elige al crear el centro (sin valor preseleccionado y con confirmación), lo corrige después solo si el centro
/// no tiene personas, y las listas de centros lo muestran con texto (Plataforma, selector de ámbito y Mi cuenta).
/// </summary>
public class TipoServicioPaginasTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private const string Token = "__RequestVerificationToken";
    private const string PlatformCenterId = "5F3A1C00-0000-4000-8000-000000000001";

    private static SqlConnectionFactory Db => new(WebAppFactory.TestConnectionString);

    private static string ExtractValue(string html, string inputName) =>
        Regex.Match(html, $"name=\"{Regex.Escape(inputName)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

    private async Task<HttpClient> LoginPlatformAsync()
    {
        var accountId = Guid.NewGuid();
        var subject = $"tipo-plat-{Guid.NewGuid():N}"[..28];
        using (var connection = await Db.OpenAsync())
        {
            await connection.ExecuteAsync($"""
                INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @subject, 'ACTIVE', SYSUTCDATETIME());
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @accountId, '{PlatformCenterId}', 'PLATAFORMA', 'ACTIVE', SYSUTCDATETIME(), @accountId)
                """, new { accountId, subject });
        }

        return await LoginAsync(subject);
    }

    private async Task<HttpClient> LoginAsync(string subject, bool expectSingleScope = true)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Redirect, (await SesionTests.PostLoginAsync(client, subject)).StatusCode);
        if (expectSingleScope)
        {
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/ProfileScope/Select")).StatusCode);
        }

        return client;
    }

    private async Task<string> PostAsync(HttpClient client, string getPage, string postPage, IEnumerable<KeyValuePair<string, string>> fields)
    {
        var html = await client.GetStringAsync(getPage);
        var all = new List<KeyValuePair<string, string>> { new(Token, ExtractValue(html, Token)) };
        all.AddRange(fields);
        var response = await client.PostAsync(postPage, new FormUrlEncodedContent(all));
        return response.StatusCode == HttpStatusCode.Redirect
            ? "REDIRECT:" + WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location!))
            : WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static List<KeyValuePair<string, string>> NewCenterFields(string name, string suffix, string? type, bool confirm)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Form.OperacionId", Guid.NewGuid().ToString()), new("Form.NombreCentro", name), new("Form.NombreUnidad", "Sala 1"),
            new("Form.IdentificadorAdministrador", $"tipo-admin-{suffix}"), new("Form.NombreAdministrador", "Admin Tipo (ficticio)"),
        };
        if (type is not null)
        {
            fields.Add(new("Form.TipoDeServicio", type));
        }

        if (confirm)
        {
            fields.Add(new("Form.ConfirmoTipo", "true"));
        }

        return fields;
    }

    private async Task<Guid> CenterIdAsync(string name)
    {
        using var connection = await Db.OpenAsync();
        return await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.centros WITH (NOLOCK) WHERE nombre_visible = @name", new { name });
    }

    private async Task<(Guid CenterId, string Name, string Suffix)> CreateCenterAsync(HttpClient platform, string type)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var name = $"Centro Tipo {suffix} (ficticio)";
        var result = await PostAsync(platform, "/Plataforma/NuevoCentro", "/Plataforma/NuevoCentro", NewCenterFields(name, suffix, type, confirm: true));
        Assert.StartsWith("REDIRECT:", result);
        Assert.Contains("Centro creado.", result);
        return (await CenterIdAsync(name), name, suffix);
    }

    [Fact]
    public async Task El_formulario_de_alta_no_preselecciona_el_tipo_de_servicio_y_pide_confirmarlo()
    {
        var platform = await LoginPlatformAsync();
        var page = WebUtility.HtmlDecode(await platform.GetStringAsync("/Plataforma/NuevoCentro"));

        Assert.Contains("Tipo de servicio", page);
        Assert.Contains("Elige el tipo de servicio", page);
        Assert.Contains(">Residencia</option>", page);
        Assert.Contains(">Centro de Día</option>", page);
        // Ninguna opción viene marcada: hay que elegir.
        Assert.DoesNotMatch(new Regex("<option[^>]*selected[^>]*>(Residencia|Centro de Día)</option>"), page);
        Assert.Contains("No se podrá cambiar cuando el centro tenga personas", page);
        Assert.Contains("name=\"Form.ConfirmoTipo\"", page);
    }

    [Fact]
    public async Task Sin_elegir_el_tipo_o_sin_confirmarlo_el_centro_no_se_crea()
    {
        var platform = await LoginPlatformAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var withoutType = $"Centro Sin Tipo {suffix} (ficticio)";
        var withoutConfirm = $"Centro Sin Confirmar {suffix} (ficticio)";

        var noType = await PostAsync(platform, "/Plataforma/NuevoCentro", "/Plataforma/NuevoCentro", NewCenterFields(withoutType, suffix, null, confirm: true));
        var noConfirm = await PostAsync(platform, "/Plataforma/NuevoCentro", "/Plataforma/NuevoCentro", NewCenterFields(withoutConfirm, suffix + "b", "CentroDeDia", confirm: false));

        Assert.Contains("Elige el tipo de servicio del centro: Residencia o Centro de Día.", noType);
        Assert.Contains("Marca la casilla para confirmar el tipo de servicio", noConfirm);
        Assert.Equal(Guid.Empty, await CenterIdAsync(withoutType));
        Assert.Equal(Guid.Empty, await CenterIdAsync(withoutConfirm));
    }

    [Fact]
    public async Task Un_Centro_de_Dia_se_ve_con_su_etiqueta_en_la_lista_y_en_su_ficha_y_se_corrige_con_confirmacion_mientras_no_tenga_personas()
    {
        var platform = await LoginPlatformAsync();
        var (centerId, name, _) = await CreateCenterAsync(platform, "CentroDeDia");

        var list = WebUtility.HtmlDecode(await platform.GetStringAsync("/Plataforma"));
        Assert.Contains(name, list);
        Assert.Contains("data-tipo-servicio=\"CENTRO_DE_DIA\"", list);
        Assert.Contains("Tipo de servicio", list);

        var sheet = WebUtility.HtmlDecode(await platform.GetStringAsync($"/Plataforma/Centro?centroId={centerId}"));
        Assert.Contains("Tipo actual:", sheet);
        Assert.Contains("data-tipo-servicio=\"CENTRO_DE_DIA\"", sheet);
        Assert.Contains("Corregir un error de alta", sheet);
        Assert.Contains("Alta del centro", sheet);

        // Sin marcar la confirmación no se cambia nada.
        var fields = new List<KeyValuePair<string, string>> { new("CentroId", centerId.ToString()), new("TipoDeServicio", "Residencia") };
        var unconfirmed = await PostAsync(platform, $"/Plataforma/Centro?centroId={centerId}", "/Plataforma/CorregirTipoServicio", fields);
        Assert.Contains("Marca la casilla para confirmar la corrección del tipo de servicio.", unconfirmed);

        fields.Add(new("Confirmo", "true"));
        var corrected = await PostAsync(platform, $"/Plataforma/Centro?centroId={centerId}", "/Plataforma/CorregirTipoServicio", fields);
        Assert.Contains("Tipo de servicio corregido.", corrected);
        Assert.Contains("Corrección de un error de alta", corrected);
        Assert.Contains("data-tipo-servicio=\"RESIDENCIA\"", corrected);
    }

    [Fact]
    public async Task Con_personas_la_ficha_no_ofrece_corregir_y_un_envio_a_mano_se_rechaza_con_su_motivo()
    {
        var platform = await LoginPlatformAsync();
        var (centerId, _, _) = await CreateCenterAsync(platform, "Residencia");
        using (var connection = await Db.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.residentes (id, centro_id, nombre_visible, fecha_nacimiento, fecha_ingreso, sexo_documentado_codigo, estado, creado_en, creado_por_cuenta_id, creado_por_perfil)
                SELECT NEWID(), @centerId, N'Persona ficticia', '1940-01-01', '2026-01-01', 'female', 'ACTIVE', SYSUTCDATETIME(), id, 'ADMINISTRACION' FROM dbo.cuentas WHERE sujeto_externo LIKE N'tipo-admin-%' AND id IN
                    (SELECT cuenta_id FROM dbo.ambitos_perfil WHERE centro_id = @centerId)
                """, new { centerId });
        }

        var sheet = WebUtility.HtmlDecode(await platform.GetStringAsync($"/Plataforma/Centro?centroId={centerId}"));
        Assert.Contains("data-tipo-servicio-bloqueado", sheet);
        Assert.DoesNotContain("data-formulario-tipo-servicio", sheet);

        var fields = new List<KeyValuePair<string, string>> { new("CentroId", centerId.ToString()), new("TipoDeServicio", "CentroDeDia"), new("Confirmo", "true") };
        var rejected = await PostAsync(platform, $"/Plataforma/Centro?centroId={centerId}", "/Plataforma/CorregirTipoServicio", fields);
        Assert.Contains("ya tiene personas dadas de alta", rejected);
        using var check = await Db.OpenAsync();
        Assert.Equal("RESIDENCIA", await check.ExecuteScalarAsync<string>("SELECT tipo_de_servicio FROM dbo.centros WHERE id = @centerId", new { centerId }));
    }

    [Theory]
    [InlineData("CentroDeDia", "CENTRO_DE_DIA", "Centro de Día")]
    [InlineData("Residencia", "RESIDENCIA", "Residencia")]
    public async Task Las_hojas_de_invitacion_y_de_vinculacion_dicen_de_que_tipo_es_el_centro(string formType, string code, string label)
    {
        var platform = await LoginPlatformAsync();
        var (centerId, name, suffix) = await CreateCenterAsync(platform, formType);
        Guid scopeId, unitId;
        using (var connection = await Db.OpenAsync())
        {
            scopeId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.ambitos_perfil WHERE centro_id = @centerId AND perfil_codigo = 'ADMINISTRACION'", new { centerId });
            unitId = await connection.ExecuteScalarAsync<Guid>("SELECT TOP (1) id FROM dbo.unidades WHERE centro_id = @centerId", new { centerId });
        }

        // Plataforma entrega la invitación de la Administración del centro nuevo.
        var invitation = await PostAsync(platform, $"/Plataforma/Centro?centroId={centerId}", "/Plataforma/Invitacion",
            [new("centroId", centerId.ToString()), new("ambitoId", scopeId.ToString())]);
        Assert.Contains("Esta hoja solo se enseña ahora.", invitation);
        Assert.Contains(name, invitation);
        Assert.Contains($"data-tipo-servicio=\"{code}\"", invitation);
        Assert.Contains($">{label}</span>", invitation);

        // La Administración del centro emite una hoja de vinculación y la persona la ve antes de aceptar.
        var admin = await LoginAsync($"tipo-admin-{suffix}");
        var sheet = await PostAsync(admin, "/Administracion/Vinculaciones", "/Administracion/NuevaVinculacion",
            [new("Form.Perfil", "Enfermeria"), new("Form.Unidades", unitId.ToString())]);
        Assert.Contains("Hoja de acceso a otro centro", sheet);
        Assert.Contains($"data-tipo-servicio=\"{code}\"", sheet);
        var token = Regex.Match(sheet, @"/Cuenta/Vincular/([A-Za-z0-9_-]+)").Groups[1].Value;
        Assert.NotEmpty(token);
    }

    [Theory]
    [InlineData("CentroDeDia", "CENTRO_DE_DIA", false)]
    [InlineData("Residencia", "RESIDENCIA", true)]
    public async Task La_administracion_de_un_centro_de_dia_no_ve_habitaciones_ni_camas_y_ambos_tipos_llevan_su_etiqueta(string formType, string code, bool hasRooms)
    {
        var platform = await LoginPlatformAsync();
        var (_, _, suffix) = await CreateCenterAsync(platform, formType);
        var admin = await LoginAsync($"tipo-admin-{suffix}");

        var structure = WebUtility.HtmlDecode(await admin.GetStringAsync("/Administracion/Estructura"));
        var create = WebUtility.HtmlDecode(await admin.GetStringAsync("/Residents/Create"));

        Assert.Equal(hasRooms, structure.Contains("Habitaciones y plazas"));
        Assert.Equal(hasRooms, create.Contains("la cama es obligatoria"));
        // La etiqueta del tipo acompaña al centro en el menú del ámbito y en la pantalla de alta.
        Assert.Contains($"data-tipo-servicio=\"{code}\"", structure);
        Assert.Contains($"data-tipo-servicio=\"{code}\"", create);
    }

    [Theory]
    [InlineData("CentroDeDia", "El centro de día ya ha informado", false)]
    [InlineData("Residencia", "La residencia ya ha informado", true)]
    public async Task El_vocabulario_de_la_administracion_sigue_al_tipo_de_servicio(string formType, string expected, bool hasLocationFilter)
    {
        var platform = await LoginPlatformAsync();
        var (_, _, suffix) = await CreateCenterAsync(platform, formType);
        var admin = await LoginAsync($"tipo-admin-{suffix}");

        var settings = WebUtility.HtmlDecode(await admin.GetStringAsync("/CitasExternas/AjustesFamilia"));
        var residents = WebUtility.HtmlDecode(await admin.GetStringAsync("/Administracion/Residentes"));

        Assert.Contains(expected, settings);
        Assert.Equal(hasLocationFilter, residents.Contains("Ubicación pendiente"));
        Assert.Contains("Sin contacto urgente", residents);
    }

    [Theory]
    [InlineData("CentroDeDia", "18:00", "30")]
    [InlineData("Residencia", "19:00", "60")]
    public async Task El_horario_de_los_comunicados_de_un_centro_nuevo_es_el_de_su_tipo_y_lo_sugerido_tambien(string formType, string time, string margin)
    {
        var platform = await LoginPlatformAsync();
        var (_, _, suffix) = await CreateCenterAsync(platform, formType);
        var admin = await LoginAsync($"tipo-admin-{suffix}");

        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/Administracion/HorarioComunicados"));
        var suggested = WebUtility.HtmlDecode(await admin.GetStringAsync("/Administracion/HorarioComunicados?sugeridos=true"));

        foreach (var html in new[] { page, suggested })
        {
            Assert.Matches($"value=\"{time}", html);
            Assert.Matches($"value=\"{margin}\"", html);
        }

        Assert.Contains($"{time} y {margin} minutos", suggested);
    }

    [Theory]
    [InlineData("CentroDeDia", false)]
    [InlineData("Residencia", true)]
    public async Task Las_habitaciones_y_plazas_solo_existen_en_una_residencia_aunque_se_escriba_la_direccion_a_mano(string formType, bool allowed)
    {
        var platform = await LoginPlatformAsync();
        var (centerId, _, suffix) = await CreateCenterAsync(platform, formType);
        var admin = await LoginAsync($"tipo-admin-{suffix}");
        Guid unitId;
        using (var connection = await Db.OpenAsync())
        {
            unitId = await connection.ExecuteScalarAsync<Guid>("SELECT TOP (1) id FROM dbo.unidades WHERE centro_id = @centerId", new { centerId });
        }

        var get = await admin.GetAsync($"/Administracion/Habitaciones?unidadId={unitId}");
        var created = await PostAsync(admin, "/Administracion/Estructura", "/Administracion/NuevaHabitacion",
            [new("Form.OperacionId", Guid.NewGuid().ToString()), new("Form.UnidadId", unitId.ToString()), new("Form.Nombre", "Habitación escrita a mano")]);
        using var check = await Db.OpenAsync();
        var rooms = await check.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.habitaciones WHERE centro_id = @centerId", new { centerId });

        if (allowed)
        {
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            Assert.Equal(1, rooms);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Redirect, get.StatusCode);
            Assert.EndsWith("/Administracion/Estructura", get.Headers.Location!.OriginalString);
            Assert.Contains("Un Centro de Día no tiene habitaciones ni plazas.", created);
            Assert.Equal(0, rooms);
        }
    }

    [Fact]
    public async Task El_tipo_de_servicio_no_sale_en_los_titulos_de_las_pestanas_ni_viaja_en_la_cookie_del_ambito()
    {
        var platform = await LoginPlatformAsync();
        var (centerId, _, suffix) = await CreateCenterAsync(platform, "CentroDeDia");
        var admin = await LoginAsync($"tipo-admin-{suffix}");

        string Title(string html) => WebUtility.HtmlDecode(Regex.Match(html, "<title>(.*?)</title>", RegexOptions.Singleline).Groups[1].Value);
        var titles = new[]
        {
            Title(await platform.GetStringAsync("/Plataforma")),
            Title(await platform.GetStringAsync($"/Plataforma/Centro?centroId={centerId}")),
            Title(await admin.GetStringAsync("/Cuenta")),
            Title(await admin.GetStringAsync("/Administracion")),
        };
        Assert.All(titles, title =>
        {
            Assert.NotEmpty(title);
            Assert.DoesNotContain("Residencia", title);
            Assert.DoesNotContain("Centro de Día", title);
        });

        // El centro de Plataforma no es un servicio: su cabecera y su menú no llevan etiqueta (ADR 0017, punto 9).
        Assert.DoesNotContain("data-tipo-servicio", await platform.GetStringAsync("/Plataforma/Documentos"));

        // La cookie del ámbito lleva el centro y el perfil, no el tipo: este lo lee siempre la base de datos (ADR 0008, punto 5; ADR 0017, punto 7).
        Assert.DoesNotContain(typeof(ResidApp.Web.Security.ActiveProfileScopeCookieValue).GetProperties(),
            property => property.Name.Contains("Service", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task El_selector_de_ambito_Mi_cuenta_y_las_cuentas_de_Plataforma_muestran_el_tipo_de_cada_centro()
    {
        var platform = await LoginPlatformAsync();
        var residence = await CreateCenterAsync(platform, "Residencia");
        var day = await CreateCenterAsync(platform, "CentroDeDia");

        // Una persona con el perfil de Administración en los dos centros: el selector tiene que distinguirlos.
        var accountId = Guid.NewGuid();
        var subject = $"tipo-doble-{Guid.NewGuid():N}"[..28];
        using (var connection = await Db.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.cuentas (id, sujeto_externo, nombre_visible, estado, creado_en) VALUES (@accountId, @subject, N'Persona en dos centros (ficticia)', 'ACTIVE', SYSUTCDATETIME());
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @accountId, @residence, 'ADMINISTRACION', 'ACTIVE', SYSUTCDATETIME(), @accountId),
                       (NEWID(), @accountId, @day, 'ADMINISTRACION', 'ACTIVE', SYSUTCDATETIME(), @accountId)
                """, new { accountId, subject, residence = residence.CenterId, day = day.CenterId });
        }

        var client = await LoginAsync(subject, expectSingleScope: false);
        var select = WebUtility.HtmlDecode(await client.GetStringAsync("/ProfileScope/Select"));
        Assert.Contains(residence.Name, select);
        Assert.Contains(day.Name, select);
        Assert.Contains("data-tipo-servicio=\"RESIDENCIA\"", select);
        Assert.Contains("data-tipo-servicio=\"CENTRO_DE_DIA\"", select);

        // Mi cuenta lista sus ámbitos con el tipo de cada centro.
        var account = WebUtility.HtmlDecode(await client.GetStringAsync("/Cuenta"));
        Assert.Contains($"{residence.Name} (Residencia)", account);
        Assert.Contains($"{day.Name} (Centro de Día)", account);

        // Plataforma ve esa cuenta en su lista de cuentas con varios centros, con el tipo de cada uno.
        var accounts = WebUtility.HtmlDecode(await platform.GetStringAsync("/Plataforma/Cuentas"));
        Assert.Contains("Persona en dos centros (ficticia)", accounts);
        Assert.Contains("data-tipo-servicio=\"CENTRO_DE_DIA\"", accounts);
    }
}
