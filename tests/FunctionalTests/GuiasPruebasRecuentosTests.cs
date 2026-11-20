using System.Text.RegularExpressions;

namespace ResidApp.FunctionalTests;

/// <summary>
/// Los recuentos de las guías de pruebas para CJ (<c>docs/pendientes-cj/pruebas-N-*.html</c>) se repiten a mano en cuatro sitios: el texto de la propia guía («14 pruebas. 5 son imprescindibles»,
/// «(5 de 14)», «Guía 1 de 9»), la tarjeta de <c>pruebas.html</c>, la tabla del README y el total del índice («103 pruebas (36 imprescindibles) en 9 guías»). Al añadir, quitar o marcar una prueba,
/// alguno se queda atrás. Aquí se cuentan las pruebas y las etiquetas «Imprescindible» de cada guía y se exige que los cuatro sitios digan lo mismo. Solo cuentan las pruebas (<c>article.q</c>
/// con su etiqueta en <c>prueba-meta</c>), no las preguntas finales o abiertas, que no llevan ese marcado.
/// </summary>
public class GuiasPruebasRecuentosTests
{
    private static string Carpeta()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "ResidApp.Web", "Guia")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new InvalidOperationException("No se encuentra la raíz del repositorio.");
        return Path.Combine(root, "docs", "pendientes-cj");
    }

    /// <summary>Las guías (<c>pruebas-N-nombre.html</c>), sin las páginas de flujo ni el índice.</summary>
    private static IEnumerable<string> Guias() =>
        Directory.GetFiles(Carpeta(), "pruebas-*.html").Select(Path.GetFileName).Cast<string>()
            .Where(name => Regex.IsMatch(name, @"^pruebas-\d+-.+\.html$") && !name.EndsWith("-flujo.html", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    public static TheoryData<string> NombresDeGuias()
    {
        var data = new TheoryData<string>();
        foreach (var name in Guias())
        {
            data.Add(name);
        }

        return data;
    }

    /// <summary>El texto de una página sin etiquetas ni saltos de línea, para buscar frases aunque el HTML las parta.</summary>
    private static string Texto(string name) =>
        Regex.Replace(Regex.Replace(File.ReadAllText(Path.Combine(Carpeta(), name)), "<[^>]+>", " "), @"\s+", " ");

    private static (int Pruebas, int Imprescindibles) Contar(string guia)
    {
        var html = File.ReadAllText(Path.Combine(Carpeta(), guia));
        var pruebas = Regex.Matches(html, @"<article class=""q""").Count;
        var imprescindibles = Regex.Matches(html, @"<div class=""prueba-meta"">\s*<span class=""tag-imp"">").Count;
        return (pruebas, imprescindibles);
    }

    [Fact]
    public void Hay_UnaGuiaPorNumero_SinHuecos()
    {
        var numbers = Guias().Select(name => int.Parse(Regex.Match(name, @"^pruebas-(\d+)-").Groups[1].Value)).Order().ToList();

        Assert.NotEmpty(numbers);
        Assert.Equal(Enumerable.Range(1, numbers.Count), numbers);
    }

    [Theory]
    [MemberData(nameof(NombresDeGuias))]
    public void LaGuia_DiceSusPropiosRecuentos_ComoLosTiene(string guia)
    {
        var (pruebas, imprescindibles) = Contar(guia);
        var text = Texto(guia);
        var number = int.Parse(Regex.Match(guia, @"^pruebas-(\d+)-").Groups[1].Value);

        Assert.True(pruebas > 0, $"{guia}: no se encuentra ninguna prueba (article.q).");
        Assert.True(imprescindibles > 0 && imprescindibles <= pruebas, $"{guia}: {imprescindibles} imprescindibles de {pruebas} pruebas.");
        Assert.Contains($"Guía {number} de {Guias().Count()}", text);
        Assert.Matches($@"\b{pruebas} pruebas\b[^.]*\. {imprescindibles} son imprescindibles", text);
        Assert.Contains($"({imprescindibles} de {pruebas})", text);
    }

    [Theory]
    [MemberData(nameof(NombresDeGuias))]
    public void ElIndiceYElReadme_DicenLosMismosRecuentosQueLaGuia(string guia)
    {
        var (pruebas, imprescindibles) = Contar(guia);
        var expected = $"{pruebas} pruebas, {imprescindibles} imprescindibles";

        var card = Regex.Match(File.ReadAllText(Path.Combine(Carpeta(), "pruebas.html")), $@"href=""{Regex.Escape(guia)}""[\s\S]{{0,600}}?(\d+ pruebas, \d+ imprescindibles)");
        Assert.True(card.Success, $"pruebas.html no tiene la tarjeta de {guia} con «N pruebas, M imprescindibles».");
        Assert.Equal(expected, card.Groups[1].Value);

        var row = File.ReadAllLines(Path.Combine(Carpeta(), "README.md")).FirstOrDefault(line => line.Contains($"[{guia}]", StringComparison.Ordinal));
        Assert.True(row is not null, $"El README no tiene la fila de {guia}.");
        Assert.Contains($"({expected})", row);
    }

    [Fact]
    public void ElTotalDelIndice_EsLaSumaDeLasGuias()
    {
        var totals = Guias().Select(Contar).ToList();
        var match = Regex.Match(Texto("pruebas.html"), @"Son (\d+) pruebas \((\d+) imprescindibles\) repartidas en (\d+) guías");

        Assert.True(match.Success, "pruebas.html no dice «Son N pruebas (M imprescindibles) repartidas en G guías».");
        Assert.Equal(totals.Sum(t => t.Pruebas), int.Parse(match.Groups[1].Value));
        Assert.Equal(totals.Sum(t => t.Imprescindibles), int.Parse(match.Groups[2].Value));
        Assert.Equal(totals.Count, int.Parse(match.Groups[3].Value));
    }
}
