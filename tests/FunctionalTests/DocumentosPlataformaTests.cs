using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ResidApp.Infrastructure.Persistence;
using ResidApp.Web.Models;
using static ResidApp.FunctionalTests.ResidentsFlowTests;

namespace ResidApp.FunctionalTests;

/// <summary>
/// La sección «Documentos» de Plataforma (/Plataforma/Documentos), por HTTP y con SQL real: solo quien tiene ahora un ámbito activo de Plataforma ve la lista y abre las páginas;
/// cualquier otro perfil no recibe contenido; el identificador del documento no forma ninguna ruta de fichero; las páginas cumplen la política de contenido (sin JavaScript propio ni
/// recursos de otros sitios) y no están desfasadas respecto al Markdown del que salen (tools\documentos-a-html.ps1).
/// </summary>
public class DocumentosPlataformaTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static SqlConnectionFactory Db => new(WebAppFactory.TestConnectionString);

    private static string Raiz()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "ResidApp.Web", "Documentos")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("No se encuentra la raíz del repositorio.");
    }

    private DocumentosCatalogo Catalogo => factory.Services.GetRequiredService<DocumentosCatalogo>();

    private async Task<HttpClient> LoginAsync(bool platform)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var accountId = Guid.NewGuid();
        var subject = $"docs-plat-{suffix}";
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        using (var connection = await Db.OpenAsync())
        {
            await connection.ExecuteAsync("INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @subject, 'ACTIVE', @now)", new { accountId, subject, now });
            if (platform)
            {
                await connection.ExecuteAsync("""
                    INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                    VALUES (NEWID(), @accountId, '5F3A1C00-0000-4000-8000-000000000001', 'PLATAFORMA', 'ACTIVE', @now, @accountId)
                    """, new { accountId, now });
            }
            else
            {
                var centerId = Guid.NewGuid();
                var unitId = Guid.NewGuid();
                var scopeId = Guid.NewGuid();
                await connection.ExecuteAsync("""
                    INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en) VALUES (@centerId, @code, @code, 'ACTIVE', @now);
                    INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@unitId, @centerId, @unitCode, @unitCode, 'ACTIVE', @now);
                    INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                    VALUES (@scopeId, @accountId, @centerId, 'ADMINISTRACION', 'ACTIVE', @now, @accountId);
                    INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
                    VALUES (NEWID(), @scopeId, @centerId, @unitId, @now, @accountId);
                    """, new { accountId, centerId, unitId, scopeId, now, code = $"DOCS-CENTER-{suffix}", unitCode = $"DOCS-UNIT-{suffix}" });
            }
        }

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Redirect, (await SesionTests.PostLoginAsync(client, subject)).StatusCode);
        // Con un único ámbito, entrar a la selección lo elige.
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/ProfileScope/Select")).StatusCode);
        return client;
    }

    [Fact]
    public async Task Plataforma_ve_la_lista_con_todos_los_documentos_y_el_acceso_desde_su_inicio()
    {
        var client = await LoginAsync(platform: true);

        // La entrada está en el inicio, junto a la de Centros, y no dentro de la pantalla de Centros.
        var home = WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        Assert.Contains("href=\"/Plataforma/Documentos\"", home);
        Assert.Contains("href=\"/Plataforma\"", home);
        Assert.DoesNotContain("/Plataforma/Documentos", WebUtility.HtmlDecode(await client.GetStringAsync("/Plataforma")));

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/Plataforma/Documentos"));
        Assert.NotEmpty(Catalogo.All);
        foreach (var document in Catalogo.All)
        {
            Assert.Contains(document.Titulo, page);
            Assert.Contains($"href=\"/Plataforma/Documento/{document.Id}\"", page);
        }
    }

    [Fact]
    public void Lo_de_desarrollo_y_de_validacion_no_se_publica_en_Documentos()
    {
        // La guía de implementación es de desarrollo y la lectura del DPO es de validación: ninguna es de explotación para Plataforma.
        Assert.DoesNotContain(Catalogo.All, d => d.Id.Contains("implementacion", StringComparison.Ordinal) || d.Id.Contains("lectura-dpo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Plataforma_abre_cada_documento_como_pagina_HTML_con_la_politica_de_contenido_y_sin_guardarse_en_cache()
    {
        var client = await LoginAsync(platform: true);

        foreach (var document in Catalogo.All)
        {
            var response = await client.GetAsync($"/Plataforma/Documento/{document.Id}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
            Assert.Contains("script-src 'self' 'nonce-", string.Join(' ', response.Headers.GetValues("Content-Security-Policy")));
            Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
            Assert.Contains("<title>", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Otro_perfil_no_recibe_ni_la_lista_ni_ningun_documento()
    {
        var client = await LoginAsync(platform: false);
        var document = Catalogo.All[0];

        Assert.DoesNotContain("/Plataforma/Documentos", WebUtility.HtmlDecode(await client.GetStringAsync("/")));
        var list = WebUtility.HtmlDecode(await client.GetStringAsync("/Plataforma/Documentos"));
        Assert.Contains("No tienes acceso a esta pantalla.", list);
        Assert.DoesNotContain(document.Titulo, list);

        var response = await client.GetAsync($"/Plataforma/Documento/{document.Id}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Plataforma/Documentos", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("no-existe")]
    [InlineData("..%2f..%2fappsettings")]
    [InlineData("pulsera-nfc-guia-implementacion.html")]
    [InlineData("PULSERA-NFC-GUIA-IMPLEMENTACION")]
    public async Task Un_identificador_que_no_es_de_un_documento_publicado_da_404_y_no_toca_ningun_fichero(string id)
    {
        var client = await LoginAsync(platform: true);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Plataforma/Documento?id={id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Plataforma/Documento/{id}")).StatusCode);
    }

    [Fact]
    public void Las_paginas_no_llevan_JavaScript_propio_ni_recursos_de_otros_sitios()
    {
        foreach (var document in Catalogo.All)
        {
            var html = File.ReadAllText(Catalogo.PagePath(document));

            // La política de contenido solo deja los scripts de la propia web: el único es el del tema claro u oscuro.
            Assert.Equal(["/js/tema.js"], Regex.Matches(html, "<script[^>]*>").Select(m => Regex.Match(m.Value, "src=\"([^\"]+)\"").Groups[1].Value).ToArray());
            Assert.DoesNotContain("<iframe", html);
            Assert.DoesNotContain("<link rel=\"stylesheet\"", html);
            Assert.DoesNotMatch("(?i)\\s(src|srcset)=\"https?:", html);
            Assert.DoesNotMatch("(?i)\\son[a-z]+=\"", html);
        }
    }

    [Fact]
    public void Cada_pagina_esta_al_dia_respecto_al_Markdown_del_que_sale()
    {
        foreach (var document in Catalogo.All)
        {
            var source = File.ReadAllText(Path.Combine(Raiz(), document.Fuente.Replace('/', Path.DirectorySeparatorChar)));
            var hash = Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false).GetBytes(source.Replace("\r\n", "\n"))));
            var html = File.ReadAllText(Catalogo.PagePath(document));

            Assert.True(html.Contains($"<meta name=\"fuente-sha256\" content=\"{hash}\">", StringComparison.Ordinal),
                $"{document.Fuente} ha cambiado y {document.Id}.html no se ha regenerado: ejecuta «pwsh tools\\documentos-a-html.ps1».");
        }
    }
}
