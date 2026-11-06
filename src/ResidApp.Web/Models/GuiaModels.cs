using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using ResidApp.Shared;

namespace ResidApp.Web.Models;

// Guía por perfil (/Guia): fichas «¿Cómo…?» guardadas en Guia/*.json (una por perfil) y dibujadas por las vistas de Views/Guia.

public sealed record GuiaReferencia(string Id, string Titulo);

/// <summary>Un paso numerado. «Botones» y «Campos» son los textos exactos de pantalla que cita el paso (los comprueba GuiaDerivaTests).</summary>
public sealed record GuiaPaso(string Texto, IReadOnlyList<string>? Botones = null, IReadOnlyList<string>? Campos = null);

/// <summary>Lo que pasa después para un perfil: «Para» es el título del perfil (Administración, Plataforma…).</summary>
public sealed record GuiaDespues(string Para, string Texto)
{
    /// <summary>Destinatarios que no son un perfil: la persona que lee la ficha y todo el personal del centro (por ejemplo, quien recibe una cuenta nueva).</summary>
    public const string Tu = "Tú";
    public const string Personal = "Personal del centro";

    public static GuiaPerfiles.Entrada? Perfil(string para) => para is Tu or Personal ? null : GuiaPerfiles.PorTitulo(para);

    public static bool EsValido(string para) => para is Tu or Personal || GuiaPerfiles.PorTitulo(para) is not null;
}

public sealed record GuiaDuda(string P, string R);

/// <summary>Una tarea. «Concede» (quién da el permiso), «Flujo» y «Acceso» (referencias a «Flujo de trabajo» y a «Acceso y sesión») son opcionales:
/// no toda tarea tiene sección en esos documentos. El resto de campos hay que escribirlos siempre, aunque sea con una lista vacía.</summary>
public sealed record GuiaFicha(
    string Codigo, string Pregunta, IReadOnlyList<string> Palabras, IReadOnlyList<string> Permisos,
    IReadOnlyList<string> Antes, IReadOnlyList<GuiaPaso> Pasos, IReadOnlyList<GuiaDespues> Despues, IReadOnlyList<GuiaDuda> Dudas,
    IReadOnlyList<string> Rutas, IReadOnlyList<string> Mensajes, GuiaReferencia Manual,
    string? Concede = null, GuiaReferencia? Flujo = null, GuiaReferencia? Acceso = null);

public sealed record GuiaPerfil(
    string Perfil, string Proposito, IReadOnlyList<string> DependeDe, IReadOnlyList<string> EntregaA,
    IReadOnlyList<string> Siempre, IReadOnlyList<string> ConPermiso, IReadOnlyList<string> Nunca,
    string? RevisadoPara, string Revisado, IReadOnlyList<GuiaFicha> Fichas, IReadOnlyList<string>? Rutina = null)
{
    public SystemProfile Profile => Enum.Parse<SystemProfile>(Perfil);

    public string Slug => GuiaPerfiles.Of(Profile).Slug;

    /// <summary>Título de la guía y color del perfil: los mismos que en «Flujo de trabajo».</summary>
    public string Titulo => GuiaPerfiles.Of(Profile).Titulo;

    /// <summary>El nombre que ve la persona en la app, solo si no es el título de la guía.</summary>
    public string? NombreEnPantalla => SystemProfileDisplay.Label(Profile) is var label && label != Titulo ? label : null;
}

/// <summary>El capítulo «Para todos» (Guia/comun.json): lo que vale para cualquier perfil. Sus fichas son las mismas que las de un perfil (códigos TOD-NN);
/// en «después», «Para» puede ser «Tú» o «Personal del centro» además de un perfil.</summary>
public sealed record GuiaComun(string Proposito, string? RevisadoPara, string Revisado, IReadOnlyList<GuiaFicha> Fichas)
{
    public const string Titulo = "Para todos";
    public const string Slug = "para-todos";
    public const string Prefijo = "TOD";
}

/// <summary>Una entrada del glosario (Guia/glosario.json): «Ver» son los códigos de las fichas donde se usa el término.</summary>
public sealed record GuiaTermino(string Termino, string Significa, IReadOnlyList<string> Ver);

public sealed record GuiaGlosario(string? RevisadoPara, string Revisado, IReadOnlyList<GuiaTermino> Terminos)
{
    /// <summary>Por orden alfabético, sin que los acentos cambien el orden («Ámbito» antes que «Auditoría»).</summary>
    public IEnumerable<GuiaTermino> Ordenados() =>
        Terminos.OrderBy(t => t.Termino, StringComparer.Create(new System.Globalization.CultureInfo("es-ES"), true));
}

public static class GuiaEnlaces
{
    /// <summary>El enlace a una ficha: dentro de la misma página si todo está en «Todo», y si no, a la página de su guía.</summary>
    public static string Ficha(this IUrlHelper url, string slug, string codigo, bool enTodo) =>
        enTodo ? "#" + codigo : (slug == GuiaComun.Slug ? url.Action("Comun", "Guia") : url.Action("Perfil", "Guia", new { slug }))! + "#" + codigo;
}

public static class GuiaPerfiles
{
    public sealed record Entrada(SystemProfile Perfil, string Slug, string Titulo);

    /// <summary>Los siete perfiles de la guía, en el orden de «Flujo de trabajo». Decisión D1 (2026-11-05): «Dirección / Coordinación Clínica» y «Familia».</summary>
    public static readonly IReadOnlyList<Entrada> Todos =
    [
        new(SystemProfile.Plataforma, "plataforma", "Plataforma"),
        new(SystemProfile.Administracion, "administracion", "Administración"),
        new(SystemProfile.Auxiliar, "auxiliar", "Auxiliar"),
        new(SystemProfile.Enfermeria, "enfermeria", "Enfermería"),
        new(SystemProfile.Medicina, "medicina", "Medicina"),
        new(SystemProfile.DireccionClinica, "direccion", "Dirección / Coordinación Clínica"),
        new(SystemProfile.Familiar, "familia", "Familia"),
    ];

    public static Entrada Of(SystemProfile profile) => Todos.Single(e => e.Perfil == profile);

    public static Entrada? PorTitulo(string titulo) => Todos.FirstOrDefault(e => e.Titulo == titulo);
}

public sealed class GuiaCatalogo
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // Un campo que falta o sobra en un JSON es un error de quien lo escribe: que falle al cargar, no en silencio.
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public IReadOnlyList<GuiaPerfil> Perfiles { get; }

    /// <summary>El capítulo «Para todos», si existe comun.json.</summary>
    public GuiaComun? Comun { get; }

    /// <summary>El glosario, si existe glosario.json.</summary>
    public GuiaGlosario? Glosario { get; }

    private GuiaCatalogo(IReadOnlyList<GuiaPerfil> perfiles, GuiaComun? comun, GuiaGlosario? glosario)
    {
        Perfiles = perfiles;
        Comun = comun;
        Glosario = glosario;
    }

    /// <summary>Lee un JSON por perfil de la carpeta (plataforma.json…), en el orden de <see cref="GuiaPerfiles.Todos"/>, y comun.json y glosario.json aparte.</summary>
    public static GuiaCatalogo Cargar(string carpeta)
    {
        var contexto = new NullabilityInfoContext();
        GuiaComun? comun = null;
        if (File.Exists(Path.Combine(carpeta, "comun.json")))
        {
            comun = JsonSerializer.Deserialize<GuiaComun>(File.ReadAllText(Path.Combine(carpeta, "comun.json")), Options)
                ?? throw new JsonException("comun.json está vacío.");
            Validar(comun, "comun.json", contexto);
        }

        GuiaGlosario? glosario = null;
        if (File.Exists(Path.Combine(carpeta, "glosario.json")))
        {
            glosario = JsonSerializer.Deserialize<GuiaGlosario>(File.ReadAllText(Path.Combine(carpeta, "glosario.json")), Options)
                ?? throw new JsonException("glosario.json está vacío.");
            Validar(glosario, "glosario.json", contexto);
        }

        var perfiles = Directory.EnumerateFiles(carpeta, "*.json").Where(file => Path.GetFileName(file) is not ("comun.json" or "glosario.json"))
            .Select(file =>
            {
                var perfil = JsonSerializer.Deserialize<GuiaPerfil>(File.ReadAllText(file), Options)
                    ?? throw new JsonException($"{Path.GetFileName(file)} está vacío.");
                Validar(perfil, Path.GetFileName(file), contexto);
                return perfil;
            })
            .OrderBy(p => GuiaPerfiles.Todos.ToList().FindIndex(e => e.Perfil == p.Profile))
            .ToList();
        return new GuiaCatalogo(perfiles, comun, glosario);
    }

    /// <summary>System.Text.Json deja en null un campo que falta, aunque no sea opcional: aquí se rechaza, indicando cuál es y en qué ficha.</summary>
    private static void Validar(object? valor, string ruta, NullabilityInfoContext contexto)
    {
        if (valor is null or string)
        {
            return;
        }

        if (valor is System.Collections.IEnumerable lista)
        {
            var i = 0;
            foreach (var elemento in lista)
            {
                Validar(elemento, $"{ruta}[{i++}]", contexto);
            }

            return;
        }

        if (valor.GetType().Namespace != typeof(GuiaPerfil).Namespace)
        {
            return;
        }

        var parametros = valor.GetType().GetConstructors().Single().GetParameters().Select(p => p.Name!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var propiedad in valor.GetType().GetProperties().Where(p => parametros.Contains(p.Name)))
        {
            var dato = propiedad.GetValue(valor);
            if (dato is null && contexto.Create(propiedad).ReadState != NullabilityState.Nullable)
            {
                throw new JsonException($"{ruta}: falta el campo «{propiedad.Name}» (los opcionales son Concede, Flujo, Acceso, Botones, Campos y Rutina).");
            }

            Validar(dato, $"{ruta}.{propiedad.Name}", contexto);
        }
    }

    public GuiaPerfil? Perfil(string slug) => Perfiles.FirstOrDefault(p => string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>Todas las fichas con el título y el «slug» de su guía («Para todos» incluido), por orden alfabético de la pregunta (índice de preguntas).</summary>
    public IEnumerable<(string Slug, string Titulo, GuiaFicha Ficha)> Preguntas() =>
        Perfiles.SelectMany(p => p.Fichas.Select(f => (Slug: p.Slug, Titulo: p.Titulo, Ficha: f)))
            .Concat((Comun?.Fichas ?? []).Select(f => (Slug: GuiaComun.Slug, Titulo: GuiaComun.Titulo, Ficha: f)))
            .OrderBy(x => x.Ficha.Pregunta.TrimStart('¿'), StringComparer.Create(new System.Globalization.CultureInfo("es-ES"), true));

    /// <summary>Todas las fichas con el «slug» de su guía, en el orden de lectura: «Para todos» y luego cada perfil.</summary>
    public IEnumerable<(string Slug, GuiaFicha Ficha)> Fichas() =>
        (Comun?.Fichas ?? []).Select(f => (Slug: GuiaComun.Slug, Ficha: f))
            .Concat(Perfiles.SelectMany(p => p.Fichas.Select(f => (Slug: p.Slug, Ficha: f))));

    /// <summary>Una guía (o «Para todos») por su «slug» y la ficha por su código, para enlazar desde el glosario.</summary>
    public string? SlugDe(string codigo) => Fichas().Where(x => x.Ficha.Codigo == codigo).Select(x => x.Slug).FirstOrDefault();

    /// <summary>Quién entrega qué a quién, sacado de «Qué pasa después y a quién le llega» de cada ficha: para cada guía de origen y cada destino
    /// (un perfil o «Personal del centro»), los códigos de las fichas. No cuenta lo que la guía se entrega a sí misma ni a «Tú».</summary>
    public IReadOnlyDictionary<(string Origen, string Destino), IReadOnlyList<string>> Entregas()
    {
        var resultado = new Dictionary<(string, string), List<string>>();
        foreach (var (slug, ficha) in Fichas())
        {
            foreach (var para in ficha.Despues.Select(d => d.Para).Distinct())
            {
                var destino = para == GuiaDespues.Personal ? GuiaMatriz.PersonalSlug : GuiaDespues.Perfil(para)?.Slug;
                if (destino is null || destino == slug)
                {
                    continue;
                }

                if (!resultado.TryGetValue((slug, destino), out var codigos))
                {
                    resultado[(slug, destino)] = codigos = [];
                }

                codigos.Add(ficha.Codigo);
            }
        }

        return resultado.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Value);
    }
}

/// <summary>Los nombres de las columnas de la matriz que no son un perfil.</summary>
public static class GuiaMatriz
{
    public const string PersonalSlug = "personal";
}

/// <summary>El texto de las fichas es texto plano con una marca mínima: **negrita** (botones y pantallas), *cursiva* y `código`. Todo lo demás se escapa.</summary>
public static partial class GuiaTexto
{
    public static HtmlString Html(string texto)
    {
        var html = WebUtility.HtmlEncode(texto);
        html = Negrita().Replace(html, "<strong>$1</strong>");
        html = Cursiva().Replace(html, "<em>$1</em>");
        html = Codigo().Replace(html, "<code>$1</code>");
        return new HtmlString(html);
    }

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Negrita();

    [GeneratedRegex(@"\*(.+?)\*")]
    private static partial Regex Cursiva();

    [GeneratedRegex("`(.+?)`")]
    private static partial Regex Codigo();
}
