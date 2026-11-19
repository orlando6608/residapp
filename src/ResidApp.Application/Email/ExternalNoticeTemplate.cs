using System.Net;

namespace ResidApp.Application.Email;

/// <summary>
/// Plantillas AVISO_CITA_EXTERNA (a 7 días y la víspera) y AVISO_CITA_EXTERNA_SIN_CUBRIR (a 2 días, solo si sigue sin cubrir), versión 1, en castellano y en catalán (PR 6 de las citas externas; texto
/// de docs/privacidad/citas-externas-lectura-dpo.md, apartado 5.2, que lo valida una persona antes de usarlo con direcciones reales; el catalán, quien sepa catalán). No son el recordatorio de cita
/// (RECORDATORIO_CITA): ese dice «tienes una cita mañana» a quien reservó SU cita, y en una cita externa la cita es del residente, así que el mismo texto daría a entender que la persona tiene una
/// cita propia. Aquí solo se dice que hay un aviso sobre una cita: ni cuándo, ni dónde, ni de qué, ni «residente», «salud» o «médico» (solo la frase común de «llama al centro» menciona el centro).
/// Solo recibe el idioma y la dirección base de la aplicación, así que no puede escribir nada de la cita. Lleva aparte, al margen de cualquier otra información (art. 21.4 RGPD), cómo dejar de
/// recibir estos avisos: decírselo a la Administración del centro, que registra la baja.
/// </summary>
public static class ExternalNoticeTemplate
{
    public const string Version = "1";

    private const string Dash = "—";

    public static RenderedEmail Render(string language, string baseUrl, bool stillUncovered)
    {
        var url = baseUrl.TrimEnd('/');
        var (subject, paragraphs, footer) = (language, stillUncovered) switch
        {
            ("es", false) => (
                "Tienes un aviso sobre una cita en ResidApp",
                new[]
                {
                    "Tienes un aviso sobre una cita en ResidApp.",
                    $"Para verlo, entra en ResidApp: {url}",
                    "Aquí no ponemos los detalles, para proteger tu privacidad: los verás al entrar.",
                    OptOutEs,
                    "No respondas a este correo: nadie lo lee. Si necesitas algo, llama al centro.",
                    "Si no esperabas este aviso, ignóralo. ResidApp nunca te pedirá tu contraseña por correo, por teléfono ni por mensaje.",
                },
                ProposalNoticeTemplate.FooterEs),
            ("es", true) => (
                "Una cita en ResidApp todavía necesita ayuda",
                new[]
                {
                    "Hay una cita en ResidApp que todavía necesita ayuda.",
                    $"Para verla, entra en ResidApp: {url}",
                    "Aquí no ponemos los detalles, para proteger tu privacidad: los verás al entrar.",
                    OptOutEs,
                    "No respondas a este correo: nadie lo lee. Si necesitas algo, llama al centro.",
                    "Si no esperabas este aviso, ignóralo. ResidApp nunca te pedirá tu contraseña por correo, por teléfono ni por mensaje.",
                },
                ProposalNoticeTemplate.FooterEs),
            ("ca", false) => (
                "Tens un avís sobre una cita a ResidApp",
                new[]
                {
                    "Tens un avís sobre una cita a ResidApp.",
                    $"Per veure'l, entra a ResidApp: {url}\nDe moment, la pantalla a la qual porta l'enllaç és en castellà.",
                    "Aquí no posem els detalls, per protegir la teva privadesa: els veuràs en entrar.",
                    OptOutCa,
                    "No responguis a aquest correu: no el llegeix ningú. Si necessites alguna cosa, truca al centre.",
                    "Si no esperaves aquest avís, ignora'l. ResidApp mai no et demanarà la contrasenya per correu, per telèfon ni per missatge.",
                },
                ProposalNoticeTemplate.FooterCa),
            ("ca", true) => (
                "Una cita a ResidApp encara necessita ajuda",
                new[]
                {
                    "Hi ha una cita a ResidApp que encara necessita ajuda.",
                    $"Per veure-la, entra a ResidApp: {url}\nDe moment, la pantalla a la qual porta l'enllaç és en castellà.",
                    "Aquí no posem els detalls, per protegir la teva privadesa: els veuràs en entrar.",
                    OptOutCa,
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

    /// <summary>La frase de baja, la misma en las dos plantillas: se dice aparte para cumplir el art. 21.4 del RGPD.</summary>
    public const string OptOutEs = "Si no quieres recibir estos avisos, díselo a la Administración del centro.";

    public const string OptOutCa = "Si no vols rebre aquests avisos, digues-ho a l'Administració del centre.";
}
