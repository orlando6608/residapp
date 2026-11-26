using System.Net;

namespace ResidApp.Application.Email;

/// <summary>
/// El aviso a la dirección anterior cuando Administración cambia o borra el correo con el que entra un familiar (ADR 0012, pieza 3), versión 1, en castellano y en catalán; texto
/// del borrador de privacidad docs/privacidad/codigo-acceso-familiar-por-correo.md (por validar, el pie por el DPO real y el catalán por una persona traductora). Solo recibe el
/// idioma: sin nombres, sin direcciones y sin enlace. Tras una errata puede recibirlo una persona ajena, y por eso el pie no dice que la dirección sea suya.
/// </summary>
public static class EmailChangeTemplate
{
    public const string Version = "1";

    private const string Dash = "—";

    private const string FooterEs = "Recibes este correo porque esta dirección constaba en ResidApp para enviar códigos de acceso. El responsable de tus datos es el centro que la registró; ResidApp los trata por su cuenta. Para saber más o ejercer tus derechos de protección de datos, dirígete al centro.";

    private const string FooterCa = "Reps aquest correu perquè aquesta adreça constava a ResidApp per enviar codis d'accés. El responsable de les teves dades és el centre que la va registrar; ResidApp les tracta per compte seu. Per saber-ne més o exercir els teus drets de protecció de dades, adreça't al centre.";

    public static RenderedEmail Render(string language)
    {
        var (subject, paragraphs, footer) = language switch
        {
            "es" => (
                "Cambio en tu cuenta de ResidApp",
                new[]
                {
                    "El centro ha cambiado o quitado el correo al que ResidApp envía tus códigos de acceso.",
                    "Si lo has pedido tú, no tienes que hacer nada. Si no, o si no sabes de qué va este correo, llama a tu centro.",
                    "No respondas a este correo.",
                },
                FooterEs),
            "ca" => (
                "Canvi al teu compte de ResidApp",
                new[]
                {
                    "El centre ha canviat o ha tret el correu on ResidApp envia els teus codis d'accés.",
                    "Si ho has demanat tu, no has de fer res. Si no, o si no saps de què va aquest correu, truca al teu centre.",
                    "No responguis aquest correu.",
                },
                FooterCa),
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Idioma no admitido (es o ca)."),
        };

        var plain = string.Join("\n\n", paragraphs) + $"\n\n{Dash}\n{footer}\n";
        var html = EmailLayout.Render(
            language, subject, paragraphs.Select(p => (WebUtility.HtmlEncode(p), false)), WebUtility.HtmlEncode(footer));
        return new RenderedEmail(subject, plain, html, [EmailLayout.Icon]);
    }
}
