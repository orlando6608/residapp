namespace ResidApp.FunctionalTests;

/// <summary>
/// Desde el PR 5 la familia ve las citas externas cuando el centro las abre. Ni el Manual, ni la Guía por perfil, ni las vistas pueden seguir diciendo que «aún no» o «todavía no» las ve
/// (plan del apartado 10 de docs/tareas/alta-prioridad/pendientes-citas-externas.md). Si algún día hace falta una frase así de verdad, se cambia esta prueba a propósito.
/// </summary>
public class CitasExternasFamiliaDocumentacionTests
{
    private static readonly string[] Stale =
    [
        "aún no ve las citas externas", "todavía no ve las citas externas", "aun no ve las citas externas", "familia aún no ve las citas", "la familia todavía no ve las citas",
    ];

    private static string Raiz()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "ResidApp.Web", "Guia")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("No se encuentra la raíz del repositorio.");
    }

    [Fact]
    public void ElManualLaGuiaYLasVistas_NoDicenQueLaFamiliaAunNoVeLasCitasExternas()
    {
        var web = Path.Combine(Raiz(), "src", "ResidApp.Web");
        var files = Directory.EnumerateFiles(Path.Combine(web, "Views"), "*.cshtml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(web, "Guia"), "*.json"))
            .Concat(Directory.EnumerateFiles(Path.Combine(web, "wwwroot"), "flujo-de-trabajo.html"));

        var found = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            found.AddRange(Stale.Where(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase)).Select(phrase => $"{Path.GetFileName(file)}: «{phrase}»"));
        }

        Assert.True(found.Count == 0, "La documentación o las vistas dicen que la familia aún no ve las citas externas: " + string.Join("; ", found));
    }
}
