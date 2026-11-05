using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;
using ResidApp.Shared;

namespace ResidApp.Web.Models;

// Guía por perfil (/Guia): fichas «¿Cómo…?» guardadas en Guia/*.json (una por perfil) y dibujadas por las vistas de Views/Guia.

public sealed record GuiaReferencia(string Id, string Titulo);

/// <summary>Un paso numerado. «Botones» y «Campos» son los textos exactos de pantalla que cita el paso (los comprueba GuiaDerivaTests).</summary>
public sealed record GuiaPaso(string Texto, IReadOnlyList<string>? Botones = null, IReadOnlyList<string>? Campos = null);

/// <summary>Lo que pasa después para un perfil: «Para» es el título del perfil (Administración, Plataforma…).</summary>
public sealed record GuiaDespues(string Para, string Texto);

public sealed record GuiaDuda(string P, string R);

public sealed record GuiaFicha(
    string Codigo, string Pregunta, IReadOnlyList<string> Palabras, IReadOnlyList<string> Permisos, string? Concede,
    IReadOnlyList<string> Antes, IReadOnlyList<GuiaPaso> Pasos, IReadOnlyList<GuiaDespues> Despues, IReadOnlyList<GuiaDuda> Dudas,
    IReadOnlyList<string> Rutas, IReadOnlyList<string> Mensajes, GuiaReferencia Manual, GuiaReferencia Flujo, GuiaReferencia? Acceso);

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

    private GuiaCatalogo(IReadOnlyList<GuiaPerfil> perfiles) => Perfiles = perfiles;

    /// <summary>Lee un JSON por perfil de la carpeta (plataforma.json…), en el orden de <see cref="GuiaPerfiles.Todos"/>.</summary>
    public static GuiaCatalogo Cargar(string carpeta)
    {
        var perfiles = Directory.EnumerateFiles(carpeta, "*.json")
            .Select(file => JsonSerializer.Deserialize<GuiaPerfil>(File.ReadAllText(file), Options)
                ?? throw new InvalidDataException($"{Path.GetFileName(file)} está vacío."))
            .OrderBy(p => GuiaPerfiles.Todos.ToList().FindIndex(e => e.Perfil == p.Profile))
            .ToList();
        return new GuiaCatalogo(perfiles);
    }

    public GuiaPerfil? Perfil(string slug) => Perfiles.FirstOrDefault(p => p.Slug == slug);

    /// <summary>Todas las fichas con su perfil, por orden alfabético de la pregunta (índice de preguntas).</summary>
    public IEnumerable<(GuiaPerfil Perfil, GuiaFicha Ficha)> Preguntas() =>
        Perfiles.SelectMany(p => p.Fichas.Select(f => (p, f)))
            .OrderBy(x => x.f.Pregunta.TrimStart('¿'), StringComparer.Create(new System.Globalization.CultureInfo("es-ES"), true));
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
