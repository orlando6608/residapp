using System.Net;

namespace ResidApp.Application.Email;

/// <summary>
/// El correo con el código para que entre la familia (ADR 0012, pieza 3), versión 1, en castellano y en catalán; texto del borrador de privacidad
/// docs/privacidad/codigo-acceso-familiar-por-correo.md, apartado 7 (el pie es de ingeniería; todo por validar, y el catalán por una persona traductora). Genérico: solo recibe el
/// idioma y el código. El código NO va en el asunto (se ve con la pantalla bloqueada) y el correo NO lleva ningún enlace: quien lo pide ya está en la pantalla.
/// </summary>
public static class AccessCodeTemplate
{
    public const string Version = "1";

    private const string Dash = "—";

    private const string FooterEs = "Recibes este correo porque esta dirección figura en ResidApp como la tuya para entrar. El responsable de tus datos es el centro que la registró; ResidApp los trata por su cuenta. Para saber más o ejercer tus derechos de protección de datos, dirígete al centro. En el apartado «Privacidad» del portal tienes la información completa.";

    private const string FooterCa = "Reps aquest correu perquè aquesta adreça figura a ResidApp com la teva per entrar. El responsable de les teves dades és el centre que la va registrar; ResidApp les tracta per compte seu. Per saber-ne més o exercir els teus drets de protecció de dades, adreça't al centre. A l'apartat «Privacidad» del portal (en castellà) trobaràs la informació completa.";

    public static RenderedEmail Render(string language, string code)
    {
        var (subject, codeLine, paragraphs, footer) = language switch
        {
            "es" => (
                "Tu código para entrar en ResidApp",
                $"Tu código es: {code}",
                new[]
                {
                    "Sirve una sola vez y caduca en 10 minutos. Escríbelo en la pantalla donde has puesto tu contraseña.",
                    "ResidApp nunca te pedirá este código por teléfono ni por mensaje. No se lo des a nadie.",
                    "¿No estás intentando entrar? Puede que alguien sepa tu contraseña. Cámbiala (entra en ResidApp y abre «Mi cuenta») y avisa a tu centro.",
                    "No respondas a este correo.",
                },
                FooterEs),
            "ca" => (
                "El teu codi per entrar a ResidApp",
                $"El teu codi és: {code}",
                new[]
                {
                    "Només serveix una vegada i caduca al cap de 10 minuts. Escriu-lo a la pantalla on has posat la contrasenya.",
                    "ResidApp mai no et demanarà aquest codi per telèfon ni per missatge. No el donis a ningú.",
                    "No estàs intentant entrar? Pot ser que algú sàpiga la teva contrasenya. Canvia-la (entra a ResidApp i obre «Mi cuenta») i avisa el teu centre.\nLa pantalla de ResidApp és en castellà.",
                    "No responguis aquest correu.",
                },
                FooterCa),
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Idioma no admitido (es o ca)."),
        };

        var plain = codeLine + "\n\n" + string.Join("\n\n", paragraphs) + $"\n\n{Dash}\n{footer}\n";
        var html = EmailLayout.Render(
            language, subject,
            paragraphs.Select(p => (WebUtility.HtmlEncode(p).Replace("\n", "<br>"), false)).Prepend((WebUtility.HtmlEncode(codeLine), true)),
            WebUtility.HtmlEncode(footer));
        return new RenderedEmail(subject, plain, html, [EmailLayout.Icon]);
    }
}
