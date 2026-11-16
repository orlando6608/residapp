using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using ResidApp.Domain.Modules;
using ResidApp.Infrastructure.Persistence;
using static ResidApp.FunctionalTests.ResidentsFlowTests;

namespace ResidApp.FunctionalTests;

/// <summary>
/// La sección «Módulos opcionales» del detalle de un centro en Plataforma y el panel de vencimientos de la lista (ADR 0015, incremento 4), por HTTP y con SQL real: solo Plataforma
/// enciende y apaga, con la petición del centro; la referencia no se repite; el apagado se nota en el estado; los vencimientos próximos y los vencidos sin renovar se enseñan.
/// </summary>
public class ModulosPlataformaPageTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private const string Token = "__RequestVerificationToken";
    private static readonly DateOnly Today = CenterModuleRules.TodayInMadrid(DateTimeOffset.UtcNow);

    private static string ExtractValue(string html, string inputName) =>
        Regex.Match(html, $"name=\"{Regex.Escape(inputName)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

    private static SqlConnectionFactory Db => new(WebAppFactory.TestConnectionString);

    private static async Task<string> SeedOperatorAsync()
    {
        var accountId = Guid.NewGuid();
        var subject = $"modulos-plat-{Guid.NewGuid():N}"[..30];
        using var connection = await Db.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @subject, 'ACTIVE', SYSUTCDATETIME());
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @accountId, '5F3A1C00-0000-4000-8000-000000000001', 'PLATAFORMA', 'ACTIVE', SYSUTCDATETIME(), @accountId);
            """, new { accountId, subject });
        return subject;
    }

    private static async Task<(Guid Id, string Name)> SeedCenterAsync()
    {
        var id = Guid.NewGuid();
        var name = $"Residencia Módulos {Guid.NewGuid():N}"[..34];
        using var connection = await Db.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en) VALUES (@id, @code, @name, 'ACTIVE', SYSUTCDATETIME())",
            new { id, code = $"MOD-{id:N}"[..24].ToUpperInvariant(), name });
        return (id, name);
    }

    private async Task<HttpClient> LoginAsync(string subject)
    {
        var client = factory.CreateClient();
        (await SesionTests.PostLoginAsync(client, subject)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<string> PostAsync(HttpClient client, string page, string action, IEnumerable<KeyValuePair<string, string>> fields)
    {
        var html = await client.GetStringAsync(page);
        var all = new List<KeyValuePair<string, string>> { new(Token, ExtractValue(html, Token)) };
        all.AddRange(fields);
        return WebUtility.HtmlDecode(await (await client.PostAsync($"/Plataforma/{action}", new FormUrlEncodedContent(all))).Content.ReadAsStringAsync());
    }

    private static List<KeyValuePair<string, string>> Request(string reference, string channel = "Correo") =>
    [
        new("SolicitudRecibidaEn", DateTime.UtcNow.AddHours(-6).ToString("yyyy-MM-ddTHH:mm")), new("Canal", channel), new("ReferenciaSolicitud", reference),
    ];

    private static List<KeyValuePair<string, string>> Activation(Guid center, string reference, DateOnly from, DateOnly until, bool allConditions = true)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("CentroId", center.ToString()), new("ModuloCodigo", CenterModules.DictadoVoz), new("VigenteDesde", from.ToString("yyyy-MM-dd")),
            new("VigenteHasta", until.ToString("yyyy-MM-dd")), new("ReferenciaAnexo", "ANX-FUNC-1"), new("VersionAnexo", "1"),
        };
        fields.AddRange(CenterModules.All[0].PrivacyConditions.Take(allConditions ? int.MaxValue : 2).Select(c => new KeyValuePair<string, string>("Condiciones", c)));
        fields.AddRange(Request(reference));
        return fields;
    }

    private static Task<int> RowsAsync(Guid center) => CountAsync("SELECT COUNT(*) FROM dbo.modulos_centro WHERE centro_id = @center", new { center });

    private static async Task<int> CountAsync(string sql, object parameters)
    {
        using var connection = await Db.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    [Fact]
    public async Task Plataforma_activa_un_modulo_a_peticion_del_centro_lo_ve_en_el_historial_y_lo_apaga_con_efecto_inmediato()
    {
        var (center, _) = await SeedCenterAsync();
        var client = await LoginAsync(await SeedOperatorAsync());
        var page = $"/Plataforma/Centro?centroId={center}";

        var before = WebUtility.HtmlDecode(await client.GetStringAsync(page));
        var activated = await PostAsync(client, page, "ActivarModulo", Activation(center, "SOL-FUNC-1", Today.AddDays(-1), Today.AddDays(60)));
        var repeated = await PostAsync(client, page, "ActivarModulo", Activation(center, "SOL-FUNC-1", Today.AddDays(-1), Today.AddDays(60)));
        var disabled = await PostAsync(client, page, "ApagarModulo", [
            new("CentroId", center.ToString()), new("ModuloCodigo", CenterModules.DictadoVoz), new("MotivoBaja", "PeticionCentro"), .. Request("SOL-FUNC-2", "Telefono")]);

        Assert.Contains("Módulos opcionales", before);
        Assert.Contains("Dictado por voz", before);
        Assert.Contains("Nunca activado", before);
        Assert.DoesNotContain("Apagar el módulo ahora", before);
        Assert.Contains("Módulo activado.", activated);
        Assert.Contains($"Activo hasta el {Today.AddDays(60):dd/MM/yyyy}", activated);
        Assert.Contains("Apagar el módulo ahora", activated);
        Assert.Contains("SOL-FUNC-1", activated);
        Assert.Contains("Esa referencia de solicitud ya se usó para este centro y módulo", repeated);
        Assert.Contains("Módulo apagado.", disabled);
        Assert.Contains(">Apagado<", disabled);
        Assert.Contains("Apagado: Petición del centro", disabled);
        Assert.Contains("Teléfono", disabled);
        Assert.Equal(2, await RowsAsync(center));
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE centro_id = @center AND tipo_recurso = 'CENTER_MODULE'", new { center }));
    }

    [Fact]
    public async Task Sin_todas_las_condiciones_marcadas_con_una_referencia_con_nombres_o_con_una_vigencia_futura_no_se_activa_nada()
    {
        var (center, _) = await SeedCenterAsync();
        var client = await LoginAsync(await SeedOperatorAsync());
        var page = $"/Plataforma/Centro?centroId={center}";

        var missingConditions = await PostAsync(client, page, "ActivarModulo", Activation(center, "SOL-FUNC-3", Today, Today.AddDays(30), allConditions: false));
        var withName = await PostAsync(client, page, "ActivarModulo", Activation(center, "Ana Pérez", Today, Today.AddDays(30)));
        var future = await PostAsync(client, page, "ActivarModulo", Activation(center, "SOL-FUNC-4", Today.AddDays(3), Today.AddDays(30)));
        var expired = await PostAsync(client, page, "ActivarModulo", Activation(center, "SOL-FUNC-5", Today.AddDays(-30), Today.AddDays(-1)));

        Assert.All([missingConditions, withName, future, expired], html => Assert.Contains("Revisa los datos del módulo", html));
        Assert.Equal(0, await RowsAsync(center));
    }

    [Fact]
    public async Task Una_hora_de_solicitud_absurda_un_canal_no_definido_o_un_motivo_vacio_dan_el_mensaje_y_no_un_500_ni_escriben_nada()
    {
        var (center, _) = await SeedCenterAsync();
        var client = await LoginAsync(await SeedOperatorAsync());
        var page = $"/Plataforma/Centro?centroId={center}";
        Assert.Contains("Módulo activado.", await PostAsync(client, page, "ActivarModulo", Activation(center, "SOL-OK-1", Today.AddDays(-1), Today.AddDays(30))));

        List<KeyValuePair<string, string>> Disable(string reason, string channel, string received) =>
        [
            new("CentroId", center.ToString()), new("ModuloCodigo", CenterModules.DictadoVoz), new("MotivoBaja", reason),
            new("SolicitudRecibidaEn", received), new("Canal", channel), new("ReferenciaSolicitud", "SOL-MAL"),
        ];
        var recent = DateTime.UtcNow.AddHours(-6).ToString("yyyy-MM-ddTHH:mm");

        var results = new[]
        {
            await PostAsync(client, page, "ApagarModulo", Disable("PeticionCentro", "Correo", "0001-01-01T00:00")),
            await PostAsync(client, page, "ApagarModulo", Disable("PeticionCentro", "99", recent)),
            await PostAsync(client, page, "ApagarModulo", Disable("", "Correo", recent)),
            await PostAsync(client, page, "ApagarModulo", Disable("PeticionCentro", "Correo", "no-es-una-fecha")),
        };

        Assert.All(results, html => Assert.Contains("Revisa los datos del módulo", html));
        Assert.Equal(1, await RowsAsync(center));
    }

    [Fact]
    public async Task Un_centro_inexistente_o_el_reservado_no_permite_cambiar_modulos_y_se_puede_apagar_aunque_el_centro_este_inactivo()
    {
        var (center, _) = await SeedCenterAsync();
        var client = await LoginAsync(await SeedOperatorAsync());
        var page = $"/Plataforma/Centro?centroId={center}";
        Assert.Contains("Módulo activado.", await PostAsync(client, page, "ActivarModulo", Activation(center, "SOL-IN-1", Today.AddDays(-1), Today.AddDays(30))));
        using (var connection = await Db.OpenAsync())
        {
            await connection.ExecuteAsync("UPDATE dbo.centros SET estado = 'INACTIVE' WHERE id = @center", new { center });
        }

        var missing = Guid.NewGuid();
        var reserved = Guid.Parse("5F3A1C00-0000-4000-8000-000000000001");
        var onMissing = await PostAsync(client, page, "ActivarModulo", Activation(missing, "SOL-IN-2", Today, Today.AddDays(30)));
        var onReserved = await PostAsync(client, page, "ActivarModulo", Activation(reserved, "SOL-IN-3", Today, Today.AddDays(30)));
        var reactivate = await PostAsync(client, page, "ActivarModulo", Activation(center, "SOL-IN-4", Today, Today.AddDays(30)));
        var disabled = await PostAsync(client, page, "ApagarModulo", [
            new("CentroId", center.ToString()), new("ModuloCodigo", CenterModules.DictadoVoz), new("MotivoBaja", "FinContrato"), .. Request("SOL-IN-5")]);

        Assert.Equal(0, await RowsAsync(missing));
        Assert.Equal(0, await RowsAsync(reserved));
        Assert.Contains("Módulo apagado.", disabled);
        Assert.Equal(2, await RowsAsync(center));
        Assert.Contains("Apagado: Fin del contrato", disabled);
        _ = (onMissing, onReserved, reactivate);
    }

    [Fact]
    public async Task La_lista_de_centros_enseña_los_vencimientos_proximos_y_los_vencidos_sin_renovar_y_cada_centro_su_estado()
    {
        var (soon, soonName) = await SeedCenterAsync();
        var (late, lateName) = await SeedCenterAsync();
        var (far, farName) = await SeedCenterAsync();
        var client = await LoginAsync(await SeedOperatorAsync());
        Assert.Contains("Módulo activado.", await PostAsync(client, $"/Plataforma/Centro?centroId={soon}", "ActivarModulo", Activation(soon, "SOL-V-1", Today.AddDays(-5), Today.AddDays(10))));
        Assert.Contains("Módulo activado.", await PostAsync(client, $"/Plataforma/Centro?centroId={far}", "ActivarModulo", Activation(far, "SOL-V-2", Today.AddDays(-5), Today.AddDays(200))));
        // Un alta que ya venció sin renovarse solo se puede dar así: la aplicación no la admite nacida vencida (la tabla es inmutable).
        using (var connection = await Db.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.modulos_centro
                    (id, centro_id, modulo_codigo, numero, estado_codigo, vigente_desde, vigente_hasta, referencia_anexo, version_anexo, version_ficha, condiciones_codigos,
                     solicitud_recibida_en, canal_codigo, referencia_solicitud, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
                SELECT NEWID(), @late, 'DICTADO_VOZ', 1, 'ACTIVE', @from, @until, 'ANX-OLD', '1', '1', 'A', SYSUTCDATETIME(), 'CORREO', 'SOL-V-3', MIN(c.id), 'PLATAFORMA', SYSUTCDATETIME()
                  FROM dbo.cuentas c
                """, new { late, from = Today.AddDays(-40).ToDateTime(TimeOnly.MinValue), until = Today.AddDays(-3).ToDateTime(TimeOnly.MinValue) });
        }

        var list = WebUtility.HtmlDecode(await client.GetStringAsync("/Plataforma"));
        var lateDetail = WebUtility.HtmlDecode(await client.GetStringAsync($"/Plataforma/Centro?centroId={late}"));

        var panel = Regex.Match(list, "id=\"vencimientos-modulos\".*?</section>", RegexOptions.Singleline).Value;
        Assert.Contains(soonName, panel);
        Assert.Contains($"hasta el {Today.AddDays(10):dd/MM/yyyy}", panel);
        Assert.Contains(lateName, panel);
        Assert.Contains($"vencido el {Today.AddDays(-3):dd/MM/yyyy}", panel);
        Assert.DoesNotContain(farName, panel);
        Assert.Contains($"Vencido el {Today.AddDays(-3):dd/MM/yyyy}", lateDetail);
    }

    [Fact]
    public async Task Quien_no_es_Plataforma_no_ve_la_seccion_de_modulos_ni_puede_cambiarlos()
    {
        var (center, _) = await SeedCenterAsync();
        var (subject, _) = await SesionTests.SeedAccountAsync("ADMINISTRACION");
        var client = await LoginAsync(subject);

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/Plataforma/Centro?centroId={center}"));
        var post = await client.PostAsync("/Plataforma/ActivarModulo", new FormUrlEncodedContent(Activation(center, "SOL-X", Today, Today.AddDays(30))));

        Assert.DoesNotContain("Módulos opcionales", page);
        Assert.DoesNotContain("Activar el módulo", page);
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        Assert.Equal(0, await RowsAsync(center));
    }
}
