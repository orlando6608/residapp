using ResidApp.Application.Email;

namespace ResidApp.UnitTests;

/// <summary>Las plantillas de los avisos de citas externas (PR 6; docs/privacidad/citas-externas-lectura-dpo.md, apartado 5.2): texto cerrado que no dice nada de la cita, con la frase de baja aparte.</summary>
public class ExternalNoticeTemplateTests
{
    private const string Url = "https://dev.example.test";

    private static readonly string[] Forbidden = ["residente", "resident", "salud", "salut", "médic", "metge", "hospital", "especialidad", "especialitat", "hora ", "mañana", "demà", "pacient", "paciente"];

    [Theory]
    [InlineData("es", false, "Tienes un aviso sobre una cita en ResidApp")]
    [InlineData("es", true, "Una cita en ResidApp todavía necesita ayuda")]
    [InlineData("ca", false, "Tens un avís sobre una cita a ResidApp")]
    [InlineData("ca", true, "Una cita a ResidApp encara necessita ajuda")]
    public void CadaPlantilla_TieneSuAsunto_ElEnlaceUnico_YLaFraseDeBaja(string language, bool uncovered, string subject)
    {
        var email = ExternalNoticeTemplate.Render(language, Url + "/", uncovered);

        Assert.Equal(subject, email.Subject);
        Assert.Contains(Url, email.PlainText);
        Assert.Contains(language == "es" ? ExternalNoticeTemplate.OptOutEs : ExternalNoticeTemplate.OptOutCa, email.PlainText);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(email.PlainText, "https://"));
    }

    [Theory]
    [InlineData("es", false)]
    [InlineData("es", true)]
    [InlineData("ca", false)]
    [InlineData("ca", true)]
    public void ElTexto_NoDiceNadaDeLaCita_NiDelResidente(string language, bool uncovered)
    {
        var email = ExternalNoticeTemplate.Render(language, Url, uncovered);
        var all = (email.Subject + " " + email.PlainText).ToLowerInvariant();
        // Solo el pie común y la frase de «llama al centro» mencionan el centro; nada más.
        var withoutCommon = all.Replace(ProposalNoticeTemplateFooter(language).ToLowerInvariant(), "");

        Assert.All(Forbidden, word => Assert.DoesNotContain(word, withoutCommon));
        Assert.DoesNotMatch(@"\d{1,2}:\d{2}", all);
    }

    [Fact]
    public void UnIdiomaNoAdmitido_Falla()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExternalNoticeTemplate.Render("fr", Url, false));
    }

    private static string ProposalNoticeTemplateFooter(string language) =>
        ExternalNoticeTemplate.Render(language, Url, false).PlainText.Split("\n—\n").Last().Trim();
}
