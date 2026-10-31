using System.Net;
using ResidApp.Application.Ports;

namespace ResidApp.Application.Email;

/// <summary>El correo ya redactado: asunto, texto plano y HTML (con un solo enlace, sin seguimiento y sin más imagen que el icono de ResidApp dentro del mensaje).</summary>
public sealed record RenderedEmail(string Subject, string PlainText, string Html, IReadOnlyList<InlineImage>? InlineImages = null);

/// <summary>
/// Plantilla AVISO_PROPUESTA_CITA, versión 2 (el mismo asunto y texto que la 1; la 2 añade el HTML con el estilo de la app y el icono), en castellano y en catalán (ADR 0012, requisitos 1 y 7; texto de docs/privacidad/aviso-cita-por-correo.md, 2.2).
/// Es genérica a propósito: solo recibe el idioma y la dirección base de la aplicación. No recibe residente, familiar, centro, unidad, equipo, motivo, fecha ni
/// profesional, así que no puede escribirlos. Las dos lenguas comparten identificador y versión y se revisan juntas (el catalán, una persona que sepa catalán).
/// Falta, a propósito, la línea con el titular de ResidApp que propone el pie: la decide Orlando.
/// </summary>
public static class ProposalNoticeTemplate
{
    public const string Version = "2";

    public static readonly IReadOnlyList<string> Languages = ["es", "ca"];

    private const string Dash = "—";

    /// <summary>El pie es el mismo en todos los avisos («solo para enviarte este aviso» vale con cualquier plantilla).</summary>
    internal const string FooterEs = "Recibes este aviso porque esta dirección figura como contacto en ResidApp. El responsable de tus datos es el centro que la registró; ResidApp los trata por su cuenta y solo para enviarte este aviso. Para saber más, ejercer tus derechos de protección de datos o dejar de recibir estos avisos, dirígete al centro. En el apartado «Privacidad» del portal tienes la información completa.";

    internal const string FooterCa = "Reps aquest avís perquè aquesta adreça figura com a contacte a ResidApp. El responsable de les teves dades és el centre que la va registrar; ResidApp les tracta per compte seu i només per enviar-te aquest avís. Per saber-ne més, exercir els teus drets de protecció de dades o deixar de rebre aquests avisos, adreça't al centre. A l'apartat «Privacidad» del portal (en castellà) trobaràs la informació completa.";

    public static RenderedEmail Render(string language, string baseUrl)
    {
        var url = baseUrl.TrimEnd('/');
        var (subject, paragraphs, footer) = language switch
        {
            "es" => (
                "Tienes una propuesta de cita pendiente en ResidApp",
                new[]
                {
                    "Hay una propuesta de cita pendiente de tu respuesta en ResidApp.",
                    $"Para verla, entra en ResidApp: {url}",
                    "Las propuestas tienen un plazo para responder.",
                    "No respondas a este correo: nadie lo lee. Si necesitas algo, llama al centro.",
                    "Si no esperabas este aviso, ignóralo. ResidApp nunca te pedirá tu contraseña por correo, por teléfono ni por mensaje.",
                },
                FooterEs),
            "ca" => (
                "Tens una proposta de cita pendent a ResidApp",
                new[]
                {
                    "Hi ha una proposta de cita pendent de la teva resposta a ResidApp.",
                    $"Per veure-la, entra a ResidApp: {url}\nDe moment, la pantalla a la qual porta l'enllaç és en castellà.",
                    "Les propostes tenen un termini per respondre.",
                    "No responguis a aquest correu: no el llegeix ningú. Si necessites alguna cosa, truca al centre.",
                    "Si no esperaves aquest avís, ignora'l. ResidApp mai no et demanarà la contrasenya per correu, per telèfon ni per missatge.",
                },
                FooterCa),
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Idioma no admitido (es o ca)."),
        };

        var plain = string.Join("\n\n", paragraphs) + $"\n\n{Dash}\n{footer}\n";
        var html = EmailLayout.Render(language, subject, paragraphs.Select(p => (LinkOrText(p, url), p.Contains(url))), WebUtility.HtmlEncode(footer));
        return new RenderedEmail(subject, plain, html, [EmailLayout.Icon]);
    }

    /// <summary>El enlace va en texto visible (nunca oculto tras otra palabra): quien lo lee ve a dónde lleva.</summary>
    internal static string LinkOrText(string paragraph, string url)
    {
        var encodedUrl = WebUtility.HtmlEncode(url);
        return WebUtility.HtmlEncode(paragraph).Replace("\n", "<br>").Replace(encodedUrl, $"<a href=\"{encodedUrl}\">{encodedUrl}</a>");
    }
}
