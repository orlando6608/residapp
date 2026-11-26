using ResidApp.Application.Email;

namespace ResidApp.FunctionalTests;

/// <summary>Que la documentación de usuario no vuelva a decir que la familia entra solo con la contraseña, y que quien busca el correo del código encuentre su asunto, que sale de la
/// plantilla (ADR 0012, pieza 3). Es una red mecánica: no sustituye a leer el texto.</summary>
public class CodigoAccesoDocumentacionTests
{
    private static string Lee(params string[] partes) => File.ReadAllText(Path.Combine([GuiaDerivaTests.Raiz(), .. partes]));

    [Theory]
    [InlineData("Views", "Home", "Manual.cshtml")]
    [InlineData("Guia", "familia.json")]
    public void ElAsuntoDelCorreoDelCodigo_ApareceEnLaDocumentacionDeLaFamilia(params string[] ruta)
    {
        var texto = Lee(["src", "ResidApp.Web", .. ruta]);

        Assert.Contains(AccessCodeTemplate.Render("es", "000000").Subject, texto);
    }

    [Theory]
    [InlineData("src/ResidApp.Web/Views/Home/Manual.cshtml")]
    [InlineData("src/ResidApp.Web/Views/Home/Privacy.cshtml")]
    [InlineData("src/ResidApp.Web/wwwroot/flujo-de-trabajo.html")]
    [InlineData("src/ResidApp.Web/wwwroot/acceso-y-sesion.html")]
    [InlineData("src/ResidApp.Web/Guia/familia.json")]
    [InlineData("src/ResidApp.Web/Guia/comun.json")]
    [InlineData("src/ResidApp.Web/Guia/administracion.json")]
    [InlineData("docs/pendientes-cj/pruebas.html")]
    public void NingunDocumentoDiceYaQueLaFamiliaEntraSoloConLaContrasena(string ruta)
    {
        var texto = Lee(ruta.Split('/'));

        foreach (var frase in new[]
        {
            "por ahora, solo con la contraseña", "Por ahora el familiar entra solo", "Las familias entran con usuario y contraseña.", "Los familiares entran solo con contraseña",
            "entra solo con la contraseña y no tiene estos códigos", "solo con usuario y contraseña", "segundo factor de las familias está por decidir", "su segundo factor está por decidir",
        })
        {
            Assert.DoesNotContain(frase, texto);
        }
    }
}
