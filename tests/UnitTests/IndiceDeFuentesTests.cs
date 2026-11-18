using System.Text.RegularExpressions;

namespace ResidApp.UnitTests;

/// <summary>docs/INDICE.md (el índice de fuentes de verdad) no se queda atrás en lo mecánico: ningún enlace apunta a algo que ya no existe, ningún ADR
/// numerado queda sin enlazar y ninguna carpeta de docs/ queda fuera. Lo que una prueba no puede comprobar (que la fuente que dice cada fila siga siendo la
/// correcta) se revisa a mano, y el sello «Revisado para ResidApp vX.Y.Z» obliga a hacerlo con cada versión MENOR: la prueba falla si el sello es de una
/// versión MENOR anterior a la de la aplicación (el PARCHE no cuenta, igual que en los sellos de la documentación de usuario).</summary>
public class IndiceDeFuentesTests
{
    private static readonly Regex Enlace = new(@"\]\((<[^>]+>|[^)\s]+)\)");
    private static readonly Regex AdrNumerado = new(@"^\d{4}-.+\.md$");
    private static readonly Regex SelloDelIndice = new(@"Revisado para ResidApp v(\d+)\.(\d+)\.\d+");
    private static readonly Regex VersionDeLaApp = new(@"<Version>(\d+)\.(\d+)\.\d+</Version>");
    private static readonly string Docs = BuscarDocs();

    [Fact]
    public void TodoEnlaceDelIndice_ApuntaAUnFicheroOUnaCarpetaQueExiste()
    {
        var rotos = Destinos().Where(d => !File.Exists(d) && !Directory.Exists(d)).Select(Relativa).ToList();

        Assert.True(rotos.Count == 0, "Enlaces de docs/INDICE.md que ya no existen: " + string.Join(", ", rotos));
    }

    [Fact]
    public void TodoAdrNumerado_TieneSuEnlaceEnElIndice()
    {
        var destinos = Destinos();
        var sinEnlace = Directory.EnumerateFiles(Path.Combine(Docs, "decisiones-arquitectura"), "*.md")
            .Where(f => AdrNumerado.IsMatch(Path.GetFileName(f)) && !destinos.Contains(f))
            .Select(Relativa).ToList();

        Assert.True(sinEnlace.Count == 0, "ADR sin enlace en docs/INDICE.md (o enlazados con otras mayúsculas): " + string.Join(", ", sinEnlace));
    }

    [Fact]
    public void TodaCarpetaDeDocs_TieneAlgunEnlaceEnElIndice()
    {
        var destinos = Destinos();
        var sinEnlace = Directory.EnumerateDirectories(Docs)
            .Where(c => Directory.EnumerateFiles(c, "*", SearchOption.AllDirectories).Any(f => Path.GetFileName(f) != ".gitkeep"))
            .Where(c => !destinos.Any(d => d == c || d.StartsWith(c + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            .Select(Relativa).ToList();

        Assert.True(sinEnlace.Count == 0, "Carpetas de docs/ sin ningún enlace en docs/INDICE.md: " + string.Join(", ", sinEnlace));
    }

    [Fact]
    public void ElSelloDelIndice_NoEsDeUnaVersionMenorAnteriorALaActual()
    {
        var sello = SelloDelIndice.Match(File.ReadAllText(Path.Combine(Docs, "INDICE.md")));
        var version = VersionDeLaApp.Match(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Docs)!, "src", "Directory.Build.props")));

        Assert.True(sello.Success, "docs/INDICE.md no lleva el sello «Revisado para ResidApp vX.Y.Z».");
        Assert.True(version.Success, "No encuentro <Version> en src/Directory.Build.props.");
        var delSello = (int.Parse(sello.Groups[1].Value), int.Parse(sello.Groups[2].Value));
        var deLaApp = (int.Parse(version.Groups[1].Value), int.Parse(version.Groups[2].Value));
        Assert.True(delSello.CompareTo(deLaApp) >= 0,
            $"docs/INDICE.md está sellado para v{delSello.Item1}.{delSello.Item2} y la aplicación es v{deLaApp.Item1}.{deLaApp.Item2}: repasa cada fila contra el repositorio y actualiza el sello.");
    }

    /// <summary>Ruta completa de cada enlace del índice que apunta a una ruta del repositorio (se descartan las direcciones http y las anclas).</summary>
    private static List<string> Destinos() =>
    [
        .. Enlace.Matches(File.ReadAllText(Path.Combine(Docs, "INDICE.md")))
            .Select(m => m.Groups[1].Value.Trim('<', '>'))
            .Where(d => !d.StartsWith("http", StringComparison.Ordinal) && !d.StartsWith('#'))
            .Select(d => Path.GetFullPath(Path.Combine(Docs, Uri.UnescapeDataString(d.Split('#')[0])))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
    ];

    private static string Relativa(string ruta) => Path.GetRelativePath(Path.GetDirectoryName(Docs)!, ruta).Replace('\\', '/');

    private static string BuscarDocs()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null && !File.Exists(Path.Combine(directorio.FullName, "docs", "INDICE.md")))
            directorio = directorio.Parent;
        return directorio is null
            ? throw new InvalidOperationException("No encuentro docs/INDICE.md subiendo desde " + AppContext.BaseDirectory)
            : Path.Combine(directorio.FullName, "docs");
    }
}
