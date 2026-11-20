namespace ResidApp.FunctionalTests;

/// <summary>
/// La hora prevista de un seguimiento o una indicación es opcional, orientativa y necesita una fecha, y con ella el vencimiento llega cuando pasa la hora
/// (script 0106; lectura del DPO del 2026-11-20: decirlo igual en el Manual, la Guía y el Flujo, y no llamarla «exacta»). Si la regla queda en un solo
/// sitio, los demás documentos vuelven a contradecirla.
/// </summary>
public class HoraPrevistaDocumentacionTests
{
    private static string Raiz()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "ResidApp.Web", "Guia")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("No se encuentra la raíz del repositorio.");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Raiz(), "src", "ResidApp.Web", .. parts]));

    [Theory]
    [InlineData("Views", "Home", "Manual.cshtml")]
    [InlineData("wwwroot", "flujo-de-trabajo.html")]
    [InlineData("Guia", "enfermeria.json")]
    [InlineData("Guia", "medicina.json")]
    public void ElManualElFlujoYLasFichas_DicenQueLaHoraEsOrientativaYNecesitaFecha(params string[] parts)
    {
        var text = Read(parts);

        Assert.Contains("orientativa", text);
        Assert.Contains("necesita una fecha", text);
        Assert.DoesNotContain("hora exacta", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Views", "Enfermeria", "IniciarSeguimiento.cshtml")]
    [InlineData("Views", "Medicina", "IniciarSeguimiento.cshtml")]
    [InlineData("Views", "Medicina", "Indicacion.cshtml")]
    [InlineData("Views", "Enfermeria", "Seguimiento.cshtml")]
    [InlineData("Views", "Medicina", "Seguimiento.cshtml")]
    public void LasPantallas_ExplicanLaHoraSinErratas(params string[] parts)
    {
        var text = Read(parts);

        Assert.Contains("orientativa", text);
        Assert.DoesNotContain("el la ", text);
    }

    [Fact]
    public void LaGuia1DeCJ_YaNoDiceQueLaFechaPrevistaEsSoloUnDia()
    {
        var docs = Path.Combine(Raiz(), "docs", "pendientes-cj");
        foreach (var file in new[] { "pruebas-1-auxiliar-enfermeria.html", "pruebas-1-auxiliar-enfermeria-flujo.html" })
        {
            var text = File.ReadAllText(Path.Combine(docs, file));
            Assert.DoesNotContain("solo se indica el día", text);
            Assert.DoesNotContain("(solo el día)", text);
            Assert.DoesNotContain("es solo un día, sin hora", text);
        }
    }
}
