using System.Text.Json;
using System.Text.RegularExpressions;

namespace ResidApp.Web.Models;

// Sección «Documentos» de Plataforma (/Plataforma/Documentos): documentos de trabajo pasados a HTML por tools\documentos-a-html.ps1 y guardados en Documentos/,
// junto a su manifiesto documentos.json. No son ficheros estáticos públicos: los sirve PlataformaController solo a Plataforma.

/// <summary>Un documento publicado. «Fuente» es el Markdown del que sale su página (el que se edita); «Tema» agrupa varios documentos en la lista.</summary>
public sealed record DocumentoPlataforma(string Id, string Fuente, string Tema, string Titulo, string ParaQuien, string Resumen);

public sealed partial class DocumentosCatalogo
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private readonly string folder;

    private DocumentosCatalogo(string folder, IReadOnlyList<DocumentoPlataforma> all)
    {
        this.folder = folder;
        All = all;
    }

    public IReadOnlyList<DocumentoPlataforma> All { get; }

    /// <summary>Lee el manifiesto y comprueba, al arrancar, que cada documento tiene su página y que el identificador solo lleva letras minúsculas, cifras y guiones
    /// (se usa para formar la ruta del fichero: nunca se toma de ahí nada que venga de la petición).</summary>
    public static DocumentosCatalogo Cargar(string folder)
    {
        var all = JsonSerializer.Deserialize<List<DocumentoPlataforma>>(File.ReadAllText(Path.Combine(folder, "documentos.json")), Options)
            ?? throw new JsonException("documentos.json está vacío.");
        foreach (var document in all)
        {
            if (string.IsNullOrWhiteSpace(document.Id) || !ValidId().IsMatch(document.Id))
            {
                throw new JsonException($"documentos.json: el identificador «{document.Id}» no es válido (minúsculas, cifras y guiones).");
            }

            if (string.IsNullOrWhiteSpace(document.Fuente) || string.IsNullOrWhiteSpace(document.Tema) || string.IsNullOrWhiteSpace(document.Titulo)
                || string.IsNullOrWhiteSpace(document.ParaQuien) || string.IsNullOrWhiteSpace(document.Resumen))
            {
                throw new JsonException($"documentos.json: al documento «{document.Id}» le falta algún campo.");
            }

            if (!File.Exists(Path.Combine(folder, document.Id + ".html")))
            {
                throw new FileNotFoundException($"Falta la página del documento «{document.Id}»: ejecuta tools\\documentos-a-html.ps1.");
            }
        }

        if (all.Select(d => d.Id).Distinct().Count() != all.Count)
        {
            throw new JsonException("documentos.json: hay identificadores repetidos.");
        }

        return new DocumentosCatalogo(folder, all);
    }

    /// <summary>El documento con ese identificador exacto, o null.</summary>
    public DocumentoPlataforma? Find(string? id) => All.FirstOrDefault(d => d.Id == id);

    public string PagePath(DocumentoPlataforma document) => Path.Combine(folder, document.Id + ".html");

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex ValidId();
}

public sealed record PlatformDocumentsViewModel(IReadOnlyList<DocumentoPlataforma> Documents);
