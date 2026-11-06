using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ResidApp.Domain.Accounts;
using ResidApp.Shared;
using ResidApp.Web.Controllers;
using ResidApp.Web.Models;

namespace ResidApp.FunctionalTests;

/// <summary>
/// Control de deriva de la Guía por perfil (src/ResidApp.Web/Guia/*.json): compara las fichas con el código y con el Manual, el Flujo y
/// Acceso y sesión, para que un perfil, un permiso, una ruta, un botón o una referencia que cambien no dejen la Guía contando otra cosa.
/// No necesita base de datos: lee ficheros y usa reflexión. Lo que no detecta (el orden de los pasos, si se entiende, lo que depende de
/// JavaScript) lo revisan el agente doc-usuario-residapp y una persona.
/// </summary>
public partial class GuiaDerivaTests
{
    /// <summary>Perfiles cuya guía todavía no está escrita (plan de la Guía, §5). Se vacía fase a fase: un perfil con guía que siga aquí, o sin ella y fuera de aquí, falla.</summary>
    private static readonly SystemProfile[] SinGuiaTodavia = [];

    private static readonly Dictionary<SystemProfile, string> Prefijos = new()
    {
        [SystemProfile.Plataforma] = "PLA", [SystemProfile.Administracion] = "ADM", [SystemProfile.Auxiliar] = "AUX",
        [SystemProfile.Enfermeria] = "ENF", [SystemProfile.Medicina] = "MED", [SystemProfile.DireccionClinica] = "DIR",
        [SystemProfile.Familiar] = "FAM",
    };

    /// <summary>Códigos de ficha que se retiraron: no se vuelven a usar nunca (plan de la Guía, §2.4). Al retirar una ficha, su código se añade aquí.</summary>
    private static readonly HashSet<string> CodigosRetirados = [];

    // Palabras que no deben salir en una página pública de usuario real (decisiones D5 y D9 del plan).
    private static readonly string[] PalabrasInternas = ["Orlando", "almendros", "olmos", "entorno de pruebas", "cuenta de prueba", "plataforma-cj",
        // Estado interno del producto: en una guía de usuario se explica qué hacer, no en qué punto está ResidApp (decisión de Orlando, 2026-11-06).
        "política provisional", "pendiente de validar", "familias reales", "equipo clínico del producto"];

    internal static string Raiz()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "ResidApp.Web", "Guia")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static string Web(params string[] partes) => Path.Combine([Raiz(), "src", "ResidApp.Web", .. partes]);

    private static GuiaCatalogo Catalogo() => GuiaCatalogo.Cargar(Web("Guia"));

    [Fact]
    public void CadaPerfilTieneGuia_ODebeEstarEnLaListaDePendientes()
    {
        var conGuia = Catalogo().Perfiles.Select(p => p.Profile).ToHashSet();
        foreach (var profile in Enum.GetValues<SystemProfile>())
        {
            Assert.True(conGuia.Contains(profile) != SinGuiaTodavia.Contains(profile),
                $"{profile}: {(conGuia.Contains(profile) ? "ya tiene guía, quítalo de SinGuiaTodavia" : "no tiene guía, escríbela o añádelo a SinGuiaTodavia")}.");
        }
    }

    [Fact]
    public void LosPermisosDeLasFichasSonLosDelCodigo_YNoFaltaNinguno()
    {
        foreach (var perfil in Catalogo().Perfiles)
        {
            var delCodigo = ProfilePermissions.For(perfil.Profile);
            var enFichas = perfil.Fichas.SelectMany(f => f.Permisos).ToHashSet();
            Assert.True(enFichas.IsSubsetOf(delCodigo), $"{perfil.Perfil}: una ficha cita un permiso que ese perfil no tiene: {string.Join(", ", enFichas.Except(delCodigo))}.");
            Assert.True(delCodigo.All(enFichas.Contains), $"{perfil.Perfil}: ninguna ficha cubre el permiso {string.Join(", ", delCodigo.Where(p => !enFichas.Contains(p)))}.");
        }
    }

    [Fact]
    public void LasRutasDeLasFichasExistenComoAccionesDeUnControlador()
    {
        var controladores = typeof(GuiaController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && t.Name.EndsWith("Controller", StringComparison.Ordinal))
            .ToDictionary(t => t.Name[..^"Controller".Length]);
        foreach (var (perfil, ficha) in Fichas())
        {
            Assert.NotEmpty(ficha.Rutas);
            foreach (var ruta in ficha.Rutas)
            {
                var partes = ruta.Split('/');
                Assert.True(partes.Length == 2 && controladores.TryGetValue(partes[0], out var tipo)
                    && tipo.GetMethods().Any(m => m.Name == partes[1] && m.IsPublic), $"{ficha.Codigo}: la ruta «{ruta}» ya no existe.");
            }
        }
    }

    [Fact]
    public void LosBotonesLosCamposYLosMensajesSiguenEnLasVistasYControladores()
    {
        foreach (var (perfil, ficha) in Fichas())
        {
            var controladores = ficha.Rutas.Select(r => r.Split('/')[0]).Distinct().ToList();
            var vistas = controladores.Select(c => Web("Views", c)).Append(Web("Views", "Shared"))
                .Where(Directory.Exists).SelectMany(d => Directory.EnumerateFiles(d, "*.cshtml", SearchOption.AllDirectories)).Select(File.ReadAllText).ToList();
            // Los textos salen de las vistas, de los controladores y modelos, de los avisos de la capa de aplicación y del JavaScript de las pantallas.
            var codigo = controladores.SelectMany(c => Directory.EnumerateFiles(Web("Controllers"), c + "Controller*.cs")).Select(File.ReadAllText)
                .Concat(Directory.EnumerateFiles(Web("Models"), "*.cs").Select(File.ReadAllText))
                .Concat(Directory.EnumerateFiles(Web("wwwroot", "js"), "*.js").Select(File.ReadAllText))
                .Concat(Directory.EnumerateFiles(Path.Combine(Raiz(), "src", "ResidApp.Application"), "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")).Select(File.ReadAllText)).ToList();

            foreach (var texto in ficha.Pasos.SelectMany(p => p.Botones ?? []))
            {
                Assert.True(vistas.Any(v => v.Contains(texto, StringComparison.Ordinal)), $"{ficha.Codigo}: el botón «{texto}» no aparece en las vistas de {string.Join(", ", controladores)} ni en Shared.");
            }

            foreach (var texto in ficha.Pasos.SelectMany(p => p.Campos ?? []))
            {
                Assert.True(vistas.Concat(codigo).Any(v => v.Contains(texto, StringComparison.Ordinal)), $"{ficha.Codigo}: el campo «{texto}» no aparece en las vistas ni en los modelos.");
            }

            foreach (var texto in ficha.Mensajes)
            {
                Assert.True(codigo.Concat(vistas).Any(v => v.Contains(texto, StringComparison.Ordinal)), $"{ficha.Codigo}: el mensaje «{texto}» ya no está en el código.");
            }
        }
    }

    [Fact]
    public void LosPasosQueDicenEnElInicioAbrenUnaTarjetaDelInicioDeSuPerfil()
    {
        // «En el Inicio, abre X» lleva al panel del perfil (Mi panel). Las tarjetas de Rangos o de Plazos, por ejemplo, están en el Inicio general: ahí hay que decir «Pulsa Inicio».
        var carpetas = new Dictionary<SystemProfile, string>
        {
            [SystemProfile.Plataforma] = "Plataforma", [SystemProfile.Administracion] = "Administracion", [SystemProfile.Auxiliar] = "Auxiliar",
            [SystemProfile.Enfermeria] = "Enfermeria", [SystemProfile.Medicina] = "Medicina", [SystemProfile.DireccionClinica] = "Direccion",
        };
        var comprobados = 0;
        foreach (var perfil in Catalogo().Perfiles.Where(p => carpetas.ContainsKey(p.Profile)))
        {
            var inicio = File.ReadAllText(Web("Views", carpetas[perfil.Profile], "Index.cshtml"));
            foreach (var ficha in perfil.Fichas)
            {
                foreach (var paso in ficha.Pasos.Where(p => p.Texto.StartsWith("En el Inicio", StringComparison.OrdinalIgnoreCase) && p.Botones is { Count: > 0 }))
                {
                    comprobados++;
                    Assert.True(inicio.Contains(paso.Botones![0], StringComparison.Ordinal),
                        $"{ficha.Codigo}: «{paso.Botones[0]}» no está en el Inicio de {perfil.Perfil} (Views/{carpetas[perfil.Profile]}/Index.cshtml); si está en el Inicio general, el paso debe empezar por «Pulsa Inicio».");
                }
            }
        }

        Assert.True(comprobados > 10, "No se ha comprobado ningún paso «En el Inicio».");
    }

    [Fact]
    public void LasReferenciasAlManualAlFlujoYAAccesoExisten_ConElMismoTitulo()
    {
        var manual = File.ReadAllText(Web("Views", "Home", "Manual.cshtml"));
        var flujo = File.ReadAllText(Web("wwwroot", "flujo-de-trabajo.html"));
        var acceso = File.ReadAllText(Web("wwwroot", "acceso-y-sesion.html"));
        foreach (var (perfil, ficha) in Fichas())
        {
            Assert.Equal(ficha.Manual.Titulo, TituloDe(manual, ficha.Manual.Id, ficha.Manual.Titulo, $"{ficha.Codigo}: Manual"));
            if (ficha.Flujo is { } f)
            {
                Assert.Equal(f.Titulo, TituloDe(flujo, f.Id, f.Titulo, $"{ficha.Codigo}: Flujo"));
            }

            if (ficha.Acceso is { } r)
            {
                Assert.Equal(r.Titulo, TituloDe(acceso, r.Id, r.Titulo, $"{ficha.Codigo}: Acceso"));
            }
        }
    }

    /// <summary>El título de la sección con ese id: el primer &lt;h2&gt; que la sigue, sin etiquetas (el Manual añade ahí la insignia del perfil). Devuelve el esperado si coincide al principio.</summary>
    private static string TituloDe(string html, string id, string esperado, string donde)
    {
        var inicio = html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
        Assert.True(inicio >= 0, $"{donde}: no existe la sección «{id}».");
        // Desde el principio de la etiqueta que lleva el id: así vale un id puesto en el propio <h2> y no solo en su <section>.
        inicio = html.LastIndexOf('<', inicio);
        var h2 = Regex.Match(html[inicio..], "<h2[^>]*>(.*?)</h2>", RegexOptions.Singleline);
        Assert.True(h2.Success, $"{donde}: la sección «{id}» no tiene título.");
        var titulo = Espacios().Replace(Regex.Replace(h2.Groups[1].Value, "<[^>]+>", " "), " ").Trim();
        Assert.True(titulo.StartsWith(esperado, StringComparison.Ordinal), $"{donde}: la sección «{id}» se llama «{titulo}», no «{esperado}».");
        return esperado;
    }

    [Fact]
    public void LasFichasTienenLaEstructuraYLosCodigosEsperados()
    {
        var codigos = new HashSet<string>();
        var preguntas = new HashSet<string>();
        var catalogo = Catalogo();
        foreach (var perfil in catalogo.Perfiles)
        {
            Assert.All(perfil.DependeDe.Concat(perfil.EntregaA), titulo => Assert.NotNull(GuiaPerfiles.PorTitulo(titulo)));
            RevisarFichas(perfil.Perfil, Prefijos[perfil.Profile], perfil.Revisado, perfil.Fichas, codigos, preguntas);
        }

        // «Para todos»: mismas reglas, con su prefijo.
        if (catalogo.Comun is { } comun)
        {
            RevisarFichas(GuiaComun.Titulo, GuiaComun.Prefijo, comun.Revisado, comun.Fichas, codigos, preguntas);
        }
    }

    private static void RevisarFichas(string nombre, string prefijo, string revisado, IReadOnlyList<GuiaFicha> fichas,
        HashSet<string> codigos, HashSet<string> preguntas)
    {
        Assert.NotEmpty(fichas);
        Assert.True(DateOnly.TryParseExact(revisado, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha) && fecha <= DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            $"{nombre}: «revisado» no es una fecha yyyy-MM-dd pasada (se admite un día más por la zona horaria).");
        foreach (var ficha in fichas)
        {
            Assert.Matches($"^{prefijo}-\\d{{2}}$", ficha.Codigo);
            Assert.True(codigos.Add(ficha.Codigo), $"Código repetido: {ficha.Codigo}.");
            Assert.True(preguntas.Add(ficha.Pregunta), $"Pregunta repetida: {ficha.Pregunta}");
            Assert.StartsWith("¿Cómo ", ficha.Pregunta);
            Assert.True(ficha.Antes.Count > 0 && ficha.Pasos.Count > 0 && ficha.Despues.Count > 0, $"{ficha.Codigo}: faltan «antes», «pasos» o «después».");
            Assert.All(ficha.Despues, d => Assert.True(GuiaDespues.EsValido(d.Para), $"{ficha.Codigo}: «{d.Para}» no es un destinatario válido de la Guía."));
            Assert.True(ficha.Permisos.Count == 0 == (ficha.Concede is null), $"{ficha.Codigo}: «concede» solo va con permisos.");
            Assert.True(ficha.Concede is null or "Administración", $"{ficha.Codigo}: los permisos configurables los concede Administración, no «{ficha.Concede}».");
            Assert.False(CodigosRetirados.Contains(ficha.Codigo), $"{ficha.Codigo}: es un código retirado y no se reutiliza nunca.");
        }

        // Un código no se reutiliza ni se borra sin avisar: del 01 al último tiene que estar cada uno, o figurar como retirado.
        var numeros = fichas.Select(f => int.Parse(f.Codigo[^2..], CultureInfo.InvariantCulture)).ToHashSet();
        for (var n = 1; n <= numeros.Max(); n++)
        {
            Assert.True(numeros.Contains(n) || CodigosRetirados.Contains($"{prefijo}-{n:00}"),
                $"{nombre}: falta el código {prefijo}-{n:00}. Si retiraste la ficha, añade el código a CodigosRetirados.");
        }
    }

    [Fact]
    public void LosColoresDeCadaPerfilSonLosDelFlujoDeTrabajo()
    {
        var guia = Colores(File.ReadAllText(Web("wwwroot", "css", "guia.css")));
        var flujo = Colores(File.ReadAllText(Web("wwwroot", "flujo-de-trabajo.html")));
        Assert.Equal(14 * 2, guia.Count);
        foreach (var (clave, valor) in guia)
        {
            Assert.True(flujo.TryGetValue(clave, out var delFlujo) && delFlujo == valor, $"{clave}: la Guía usa {valor} y el Flujo {delFlujo}.");
        }
    }

    /// <summary>La 1.ª aparición de cada variable --lane-* es el modo claro; la 2.ª, el oscuro (así están ordenados los dos archivos).</summary>
    private static Dictionary<string, string> Colores(string texto)
    {
        var vistas = new Dictionary<string, int>();
        var resultado = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(texto, @"(--lane-(?:plat|adm|aux|enf|med|dir|fam)(?:-bg)?)\s*:\s*(#[0-9a-fA-F]{6})"))
        {
            var n = vistas[m.Groups[1].Value] = vistas.GetValueOrDefault(m.Groups[1].Value) + 1;
            if (n <= 2)
            {
                resultado[$"{m.Groups[1].Value} ({(n == 1 ? "claro" : "oscuro")})"] = m.Groups[2].Value.ToLowerInvariant();
            }
        }

        return resultado;
    }

    [Fact]
    public void ElSelloDeRevisionCoincideConElRegistro_YNoEsDeUnaVersionFutura()
    {
        var registro = JsonDocument.Parse(File.ReadAllText(Path.Combine(Raiz(), ".claude", "documentacion-usuario.json")));
        var entrada = registro.RootElement.GetProperty("documentos").EnumerateArray()
            .Single(d => d.GetProperty("ruta").GetString() == "src/ResidApp.Web/Guia/*.json");
        var sello = entrada.GetProperty("revisadoPara") is { ValueKind: JsonValueKind.String } valor ? valor.GetString() : null;
        var version = Regex.Match(File.ReadAllText(Path.Combine(Raiz(), "src", "Directory.Build.props")), "<Version>([^<]+)</Version>").Groups[1].Value;

        var catalogo = Catalogo();
        foreach (var perfil in catalogo.Perfiles)
        {
            Assert.True(perfil.RevisadoPara == sello, $"{perfil.Perfil}: revisadoPara es «{perfil.RevisadoPara}» y el registro dice «{sello}».");
        }

        if (catalogo.Comun is { } comun)
        {
            Assert.True(comun.RevisadoPara == sello, $"{GuiaComun.Titulo}: revisadoPara es «{comun.RevisadoPara}» y el registro dice «{sello}».");
        }

        if (catalogo.Glosario is { } glosario)
        {
            Assert.True(glosario.RevisadoPara == sello, $"Glosario: revisadoPara es «{glosario.RevisadoPara}» y el registro dice «{sello}».");
        }

        if (sello is not null)
        {
            Assert.True(System.Version.Parse(sello) <= System.Version.Parse(version), $"El sello v{sello} es posterior a la versión de la app v{version}.");
        }
    }

    [Fact]
    public void LaGuiaNoLlevaContenidoInterno()
    {
        // Los datos de las fichas y también las vistas que las dibujan.
        foreach (var file in Directory.EnumerateFiles(Web("Guia"), "*.json").Concat(Directory.EnumerateFiles(Web("Views", "Guia"), "*.cshtml")))
        {
            var texto = File.ReadAllText(file);
            foreach (var palabra in PalabrasInternas)
            {
                Assert.False(texto.Contains(palabra, StringComparison.OrdinalIgnoreCase), $"{Path.GetFileName(file)} contiene «{palabra}»: la Guía es pública y de usuario real.");
            }

            Assert.False(CjSuelto().IsMatch(texto), $"{Path.GetFileName(file)} nombra a «CJ»: la Guía es pública y de usuario real.");
        }
    }

    [Fact]
    public void LosCodigosDeFichaQueCitaUnTextoExisten()
    {
        var existentes = Fichas().Select(x => x.Ficha.Codigo).ToHashSet();
        foreach (var file in Directory.EnumerateFiles(Web("Guia"), "*.json"))
        {
            // Solo las citas «ver XXX-00» o «de XXX-00 a YYY-00»: los códigos que las propias fichas declaran no cuentan como cita.
            foreach (Match cita in CodigoCitado().Matches(File.ReadAllText(file)))
            {
                Assert.True(existentes.Contains(cita.Groups[1].Value), $"{Path.GetFileName(file)} cita {cita.Groups[1].Value}, que no existe como ficha.");
            }
        }
    }

    [Fact]
    public void ElTextoDeLasFichasEscapaElHtml_YMarcaSoloNegritaCursivaYCodigo()
    {
        Assert.Equal("&lt;script&gt;alert(1)&lt;/script&gt; Tom &amp; Jerry", GuiaTexto.Html("<script>alert(1)</script> Tom & Jerry").ToString());
        Assert.Equal("<strong>a</strong> <em>b</em> <code>c</code>", GuiaTexto.Html("**a** *b* `c`").ToString());
        Assert.DoesNotContain("<img", GuiaTexto.Html("**<img src=x onerror=alert(1)>**").ToString());
    }

    [Fact]
    public void NingunPerfilTieneElNombreDeUnaPaginaDeLaGuia()
    {
        // «Guia/{slug}» no puede tapar a las páginas con ruta propia (preguntas, matriz, glosario, todo, para-todos).
        string[] reservados = ["preguntas", "matriz", "glosario", "todo", GuiaComun.Slug];
        Assert.Empty(GuiaPerfiles.Todos.Select(e => e.Slug).Intersect(reservados, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void ElGlosarioNoRepiteTerminos_YSusFichasExisten()
    {
        var glosario = Catalogo().Glosario;
        Assert.NotNull(glosario);
        var existentes = Fichas().Select(x => x.Ficha.Codigo).ToHashSet();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var termino in glosario.Terminos)
        {
            Assert.False(string.IsNullOrWhiteSpace(termino.Significa), $"«{termino.Termino}» no dice qué significa.");
            Assert.True(vistos.Add(termino.Termino), $"Término repetido: {termino.Termino}");
            Assert.True(termino.Ver.Count > 0, $"«{termino.Termino}» no remite a ninguna ficha.");
            foreach (var codigo in termino.Ver)
            {
                Assert.True(existentes.Contains(codigo), $"«{termino.Termino}» remite a {codigo}, que no existe como ficha.");
            }
        }
    }

    [Fact]
    public void CadaEntregaQueDeclaraUnaGuiaLaHaceAlMenosUnaFicha()
    {
        // El diagrama de cada perfil dice a quién le entrega trabajo; la matriz sale de las fichas. Si no coinciden, una de las dos miente.
        var catalogo = Catalogo();
        var entregas = catalogo.Entregas();
        foreach (var perfil in catalogo.Perfiles)
        {
            foreach (var para in perfil.EntregaA)
            {
                var destino = GuiaPerfiles.PorTitulo(para);
                Assert.True(destino is not null, $"{perfil.Perfil}: «{para}» no es un perfil.");
                Assert.True(entregas.ContainsKey((perfil.Slug, destino.Slug)), $"{perfil.Perfil} dice que le entrega trabajo a {para}, pero ninguna de sus fichas lo hace.");
            }
        }
    }

    [Fact]
    public void UnCampoObligatorioQueFalta_HaceFallarLaCarga()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "guia-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        try
        {
            // Una ficha sin «mensajes»: la carga tiene que fallar diciendo cuál falta, no dar una ficha a medias.
            File.WriteAllText(Path.Combine(carpeta, "auxiliar.json"), """
                { "perfil": "Auxiliar", "proposito": "x", "dependeDe": [], "entregaA": [], "siempre": [], "conPermiso": [], "nunca": [],
                  "revisadoPara": null, "revisado": "2026-11-05",
                  "fichas": [ { "codigo": "AUX-01", "pregunta": "¿Cómo x?", "palabras": [], "permisos": [], "antes": [], "pasos": [],
                                "despues": [], "dudas": [], "rutas": [], "manual": { "id": "a", "titulo": "b" } } ] }
                """);
            var error = Assert.ThrowsAny<JsonException>(() => GuiaCatalogo.Cargar(carpeta));
            Assert.Contains("Mensajes", error.Message);

            // Un campo que sobra (una errata en el nombre) también.
            File.WriteAllText(Path.Combine(carpeta, "auxiliar.json"), """
                { "perfil": "Auxiliar", "proposito": "x", "dependeDe": [], "entregaA": [], "siempre": [], "conPermiso": [], "nunca": [],
                  "revisadoPara": null, "revisado": "2026-11-05", "fichas": [], "propocito": "errata" }
                """);
            Assert.ThrowsAny<JsonException>(() => GuiaCatalogo.Cargar(carpeta));
        }
        finally
        {
            Directory.Delete(carpeta, true);
        }
    }

    /// <summary>Las fichas de todos los perfiles y las del capítulo «Para todos», con el nombre de su guía.</summary>
    private static IEnumerable<(string Guia, GuiaFicha Ficha)> Fichas()
    {
        var catalogo = Catalogo();
        return catalogo.Perfiles.SelectMany(p => p.Fichas.Select(f => (p.Perfil, f)))
            .Concat((catalogo.Comun?.Fichas ?? []).Select(f => (GuiaComun.Titulo, f)));
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();

    [GeneratedRegex(@"(?:ver|de|a|y|o) ([A-Z]{3}-\d{2})\b")]
    private static partial Regex CodigoCitado();

    [GeneratedRegex(@"\bCJ\b")]
    private static partial Regex CjSuelto();
}

/// <summary>La Guía es pública (como el Manual) y se dibuja desde las fichas. Estas pruebas sí arrancan la aplicación.</summary>
public class GuiaPageTests(ResidentsFlowTests.WebAppFactory factory) : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    [Fact]
    public async Task LaGuiaSeVeSinSesion_ConLaFichaDeCentroNuevo_YElSello()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var indice = await client.GetAsync("/Guia");
        Assert.Equal(HttpStatusCode.OK, indice.StatusCode);
        var html = WebUtility.HtmlDecode(await indice.Content.ReadAsStringAsync());
        Assert.Contains("Todas las preguntas", html);

        var preguntas = await client.GetAsync("/Guia/preguntas");
        Assert.Equal(HttpStatusCode.OK, preguntas.StatusCode);
        var htmlPreguntas = WebUtility.HtmlDecode(await preguntas.Content.ReadAsStringAsync());
        Assert.Contains("¿Cómo creo un centro nuevo y qué datos necesito?", htmlPreguntas);
        Assert.Contains("¿Cómo entro por primera vez con mi invitación?", htmlPreguntas);

        var plataforma = await client.GetAsync("/Guia/plataforma");
        Assert.Equal(HttpStatusCode.OK, plataforma.StatusCode);
        html = WebUtility.HtmlDecode(await plataforma.Content.ReadAsStringAsync());
        Assert.Contains("id=\"PLA-02\"", html);
        Assert.Contains("Crear invitación de acceso o de recuperación", html);
        Assert.Contains("Pendiente de revisión de punta a punta", html);

        var comun = await client.GetAsync("/Guia/para-todos");
        Assert.Equal(HttpStatusCode.OK, comun.StatusCode);
        html = WebUtility.HtmlDecode(await comun.Content.ReadAsStringAsync());
        Assert.Contains("id=\"TOD-01\"", html);
        Assert.Contains("Ya los he guardado, continuar", html);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Guia/no-existe")).StatusCode);

        // La dirección vale escrita con mayúsculas (decisión de Orlando, 2026-11-06), pero «comun» no es el nombre de «Para todos».
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Guia/Plataforma")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Guia/Para-Todos")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Guia/MATRIZ")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Guia/comun")).StatusCode);
    }

    [Fact]
    public async Task UnaFichaConPermisoMuestraRequierePermisoYQuienLoConcede()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Guia/enfermeria"));
        var ficha = html[html.IndexOf("id=\"ENF-17\"", StringComparison.Ordinal)..];
        ficha = ficha[..ficha.IndexOf("</header>", StringComparison.Ordinal)];
        Assert.Contains("Requiere permiso", ficha);
        Assert.Contains("Dar de alta residentes", ficha);
        Assert.Contains("lo concede Administración", ficha);
    }

    [Fact]
    public async Task LaMatrizElGlosarioYLaGuiaCompletaSeVenSinSesion()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var catalogo = factory.Services.GetRequiredService<GuiaCatalogo>();

        var matriz = WebUtility.HtmlDecode(await client.GetStringAsync("/Guia/matriz"));
        Assert.Contains("Quién entrega qué a quién", matriz);
        Assert.Contains("PLA-02", matriz);

        var glosario = WebUtility.HtmlDecode(await client.GetStringAsync("/Guia/glosario"));
        Assert.Contains("Basal (estado basal)", glosario);
        Assert.Contains("href=\"/Guia/enfermeria#ENF-18\"", glosario);

        // La guía completa lleva todas las fichas, una sola vez y sin ningún id repetido (cada guía es un capítulo de la misma página).
        var todo = WebUtility.HtmlDecode(await client.GetStringAsync("/Guia/todo"));
        foreach (var (_, ficha) in catalogo.Fichas())
        {
            Assert.Single(Regex.Matches(todo, "id=\"" + ficha.Codigo + "\""));
        }

        var ids = Regex.Matches(todo, @"\sid=""([^""]+)""").Select(m => m.Groups[1].Value).ToList();
        Assert.True(ids.Count > 127, "No se han leído los id de la página.");
        Assert.Empty(ids.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.Contains("Pendiente de revisión de punta a punta", todo);
    }

    [Fact]
    public async Task ElPieDeLaWebEnlazaALaGuia_YElManualYAccesoLaNombranComoPaginaSinSesion()
    {
        var client = factory.CreateClient();

        Assert.Contains("href=\"/Guia\">Guía por perfil</a>", WebUtility.HtmlDecode(await client.GetStringAsync("/Acceso/Entrar")));
        Assert.Contains("Guía por perfil", WebUtility.HtmlDecode(await client.GetStringAsync("/Home/Manual")));
        Assert.Contains("la Guía por perfil, esta página", WebUtility.HtmlDecode(await client.GetStringAsync("/acceso-y-sesion.html")));
    }
}
