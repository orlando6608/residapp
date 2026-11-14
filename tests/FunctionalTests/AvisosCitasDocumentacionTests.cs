using ResidApp.Application.Email;

namespace ResidApp.FunctionalTests;

/// <summary>Que la documentación de usuario no se quede atrás del recordatorio de cita del día antes (ADR 0012; docs/privacidad/recordatorio-cita-por-correo.md, apartado 6): el
/// asunto del correo, que sale de la plantilla, aparece en el Manual, en la Guía por perfil y en la política de privacidad, y ningún sitio sigue diciendo que no hay recordatorios o
/// que los avisos «no dicen la fecha». Es una red mecánica: no sustituye a leer el texto.</summary>
public class AvisosCitasDocumentacionTests
{
    private static string Web(params string[] partes) => Path.Combine([GuiaDerivaTests.Raiz(), "src", "ResidApp.Web", .. partes]);

    private static readonly string Asunto = ReminderTemplate.Render("es", "https://dev.example.test").Subject;

    [Theory]
    [InlineData("Views", "Home", "Manual.cshtml")]
    [InlineData("Views", "Home", "Privacy.cshtml")]
    [InlineData("Guia", "familia.json")]
    public void ElAsuntoDelRecordatorio_AparecePublicadoEnLaDocumentacionDeLaFamilia(params string[] ruta)
    {
        var texto = File.ReadAllText(Web(ruta));

        Assert.Contains(Asunto.Replace(" en ResidApp", ""), texto);
    }

    [Theory]
    [InlineData("Views", "Home", "Manual.cshtml")]
    [InlineData("wwwroot", "flujo-de-trabajo.html")]
    [InlineData("Views", "Administracion", "Familiar.cshtml")]
    [InlineData("Guia", "administracion.json")]
    [InlineData("Guia", "familia.json")]
    public void NingunaDocumentacionDiceYaQueNoHayRecordatoriosNiQueLosAvisosNoDicenLaFecha(params string[] ruta)
    {
        var texto = File.ReadAllText(Web(ruta));

        Assert.DoesNotContain("Tampoco hay recordatorios de citas", texto);
        Assert.DoesNotContain("Todavía no hay recordatorios de citas", texto);
        Assert.DoesNotContain("Recordatorios de citas. Hoy solo hay dos avisos", texto);
        Assert.DoesNotContain("ni la fecha, ni el motivo: solo que entre en ResidApp", texto);
        Assert.DoesNotContain("ni la fecha ni el motivo.", texto);
    }
}
