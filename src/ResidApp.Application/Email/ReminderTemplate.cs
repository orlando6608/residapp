using System.Net;

namespace ResidApp.Application.Email;

/// <summary>
/// Plantilla RECORDATORIO_CITA, versión 1, en castellano y en catalán (ADR 0012; CJ, tercera tanda 6.2 B; texto de docs/privacidad/recordatorio-cita-por-correo.md, apartado 1,
/// que lo valida una persona antes de usarlo con direcciones reales; el catalán, quien sepa catalán). Es el único aviso que dice algo de la cita: que es «mañana». Por eso el
/// código que lo envía tiene que garantizar que es verdad (ReminderService). Solo recibe el idioma y la dirección base de la aplicación, así que no puede escribir hora,
/// equipo, modalidad, motivo, residente ni centro. «Centro» solo aparece en la frase común de «llama al centro», nunca en el asunto ni en el primer párrafo (es lo que ve la
/// vista previa). El marco y el pie son los de los otros avisos.
/// </summary>
public static class ReminderTemplate
{
    public const string Version = "1";

    private const string Dash = "—";

    public static RenderedEmail Render(string language, string baseUrl)
    {
        var url = baseUrl.TrimEnd('/');
        var (subject, paragraphs, footer) = language switch
        {
            "es" => (
                "Tienes una cita mañana en ResidApp",
                new[]
                {
                    "Tienes una cita mañana en ResidApp.",
                    $"Para ver los detalles, entra en ResidApp: {url}",
                    "Aquí no ponemos los detalles, para proteger tu privacidad: los verás al entrar.",
                    "No respondas a este correo: nadie lo lee. Si necesitas algo, llama al centro.",
                    "Si no esperabas este aviso, ignóralo. ResidApp nunca te pedirá tu contraseña por correo, por teléfono ni por mensaje.",
                },
                ProposalNoticeTemplate.FooterEs),
            "ca" => (
                "Tens una cita demà a ResidApp",
                new[]
                {
                    "Tens una cita demà a ResidApp.",
                    $"Per veure'n els detalls, entra a ResidApp: {url}\nDe moment, la pantalla a la qual porta l'enllaç és en castellà.",
                    "Aquí no posem els detalls, per protegir la teva privadesa: els veuràs en entrar.",
                    "No responguis a aquest correu: no el llegeix ningú. Si necessites alguna cosa, truca al centre.",
                    "Si no esperaves aquest avís, ignora'l. ResidApp mai no et demanarà la contrasenya per correu, per telèfon ni per missatge.",
                },
                ProposalNoticeTemplate.FooterCa),
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Idioma no admitido (es o ca)."),
        };

        var plain = string.Join("\n\n", paragraphs) + $"\n\n{Dash}\n{footer}\n";
        var html = EmailLayout.Render(
            language, subject, paragraphs.Select(p => (ProposalNoticeTemplate.LinkOrText(p, url), p.Contains(url))), WebUtility.HtmlEncode(footer));
        return new RenderedEmail(subject, plain, html, [EmailLayout.Icon]);
    }
}
