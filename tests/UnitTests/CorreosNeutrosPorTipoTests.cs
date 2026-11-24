using ResidApp.Application.Email;

namespace ResidApp.UnitTests;

/// <summary>
/// Los correos de aviso no dicen de qué tipo de servicio es el centro (lectura del DPO, docs/privacidad/centro-de-dia-textos-fase3-lectura-dpo.md): decir «centro de día» revelaría una inferencia
/// sobre la persona (ADR 0012) y las plantillas no reciben el centro. Esta prueba falla si alguna dice «residencia» o «centro de día».
/// </summary>
public class CorreosNeutrosPorTipoTests
{
    private const string Url = "https://dev.example.test";

    public static TheoryData<string, string> Correos()
    {
        var data = new TheoryData<string, string>();
        foreach (var language in new[] { "es", "ca" })
        {
            foreach (var (name, email) in new (string, RenderedEmail)[]
            {
                ("propuesta", ProposalNoticeTemplate.Render(language, Url)),
                ("cambio", ChangeNoticeTemplate.Render(language, Url)),
                ("recordatorio", ReminderTemplate.Render(language, Url)),
                ("citas externas", ExternalNoticeTemplate.Render(language, Url, stillUncovered: false)),
                ("citas externas sin cubrir", ExternalNoticeTemplate.Render(language, Url, stillUncovered: true)),
            })
            {
                data.Add($"{name} ({language})", $"{email.Subject}\n{email.PlainText}\n{email.Html}");
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Correos))]
    public void Ningun_correo_dice_residencia_ni_centro_de_dia(string name, string text)
    {
        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.DoesNotContain("residencia", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("residència", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("centro de día", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("centre de dia", text, StringComparison.OrdinalIgnoreCase);
    }
}
