using System.Net;

namespace ResidApp.Application.Email;

/// <summary>
/// Plantilla AVISO_CAMBIO_CITA, versión 1, en castellano y en catalán (ADR 0012; texto de docs/privacidad/aviso-cita-cambio-cancelacion-por-correo.md, 2.1, que lo valida
/// una persona antes de usarlo con direcciones reales; el catalán, quien sepa catalán). Un solo aviso para reprogramar y para cancelar: no dice cuál de las dos cosas
/// ha pasado. Es genérica a propósito: solo recibe el idioma y la dirección base de la aplicación, así que no puede escribir día, hora, equipo, modalidad, motivo,
/// residente ni centro. La palabra «centro» solo aparece en las frases comunes del final, nunca en el asunto ni en el primer párrafo (es lo que ve la vista previa).
/// El marco y el pie son los del aviso de propuesta.
/// </summary>
public static class ChangeNoticeTemplate
{
    public const string Version = "1";

    private const string Dash = "—";

    public static RenderedEmail Render(string language, string baseUrl)
    {
        var url = baseUrl.TrimEnd('/');
        var (subject, paragraphs, footer) = language switch
        {
            "es" => (
                "Tienes un cambio en una cita en ResidApp",
                new[]
                {
                    "Hay un cambio en una cita que tienes en ResidApp.",
                    $"Para verlo, entra en ResidApp: {url}",
                    "Aquí no ponemos los detalles, para proteger tu privacidad: los verás al entrar.",
                    "No respondas a este correo: nadie lo lee. Si necesitas algo, llama al centro.",
                    "Si no esperabas este aviso, ignóralo. ResidApp nunca te pedirá tu contraseña por correo, por teléfono ni por mensaje.",
                },
                ProposalNoticeTemplate.FooterEs),
            "ca" => (
                "Tens un canvi en una cita a ResidApp",
                new[]
                {
                    "Hi ha un canvi en una cita que tens a ResidApp.",
                    $"Per veure'l, entra a ResidApp: {url}\nDe moment, la pantalla a la qual porta l'enllaç és en castellà.",
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
