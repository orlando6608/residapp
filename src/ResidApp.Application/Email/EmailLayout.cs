using System.Net;
using System.Text;
using ResidApp.Application.Ports;

namespace ResidApp.Application.Email;

/// <summary>
/// El marco visual común de los correos de la app (guia-diseno-sistema-visual.md, paleta y modo oscuro): banda azul con el icono y el nombre de ResidApp, tarjeta
/// con los párrafos y pie. Cada plantilla solo aporta sus párrafos. HTML de correo a la antigua (tablas y estilos escritos en cada etiqueta, letra del sistema): los
/// clientes de correo no entienden hojas de estilo ni letras de Internet. El único icono viaja dentro del mensaje (<c>cid:</c>), nunca enlazado desde un servidor.
/// Es genérico a propósito: nada de centro, residente ni salud (ADR 0012, requisito 1).
/// </summary>
public static class EmailLayout
{
    public const string IconContentId = "residapp-icono";

    /// <summary>El icono de la app a 96 px (se muestra a 48 px, para pantallas de doble densidad), tomado del PNG de los PDF.</summary>
    public static readonly InlineImage Icon = new(IconContentId, "icono-residapp.png", "image/png", LoadIcon());

    private const string Font = "-apple-system,'Segoe UI',Roboto,Arial,sans-serif";

    /// <summary>Colores oscuros de la guía para quien tiene el correo en modo oscuro; la banda de marca no cambia. Gmail y Outlook a veces los ignoran y recolorean por su cuenta.</summary>
    private const string DarkMode =
        "@media (prefers-color-scheme: dark){"
        + ".fondo{background:#0F141B !important}"
        + ".tarjeta{background:#171D26 !important;border-color:#2A3441 !important}"
        + ".texto{color:#E3E8EF !important}"
        + ".destacado{background:#1B2A41 !important;border-left-color:#6EA8FE !important}"
        + ".pie{color:#A3ADBA !important;border-top-color:#2A3441 !important}"
        + "a{color:#6EA8FE !important}}";

    /// <summary>Cada párrafo ya viene en HTML; <c>Highlighted</c> lo destaca en un recuadro (el que lleva el enlace, que sigue siendo texto visible). <paramref name="footerHtml"/> también viene ya codificado.</summary>
    public static string Render(string language, string subject, IEnumerable<(string Html, bool Highlighted)> paragraphs, string footerHtml)
    {
        var html = new StringBuilder("<!doctype html>");
        html.Append($"<html lang=\"{WebUtility.HtmlEncode(language)}\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<meta name=\"color-scheme\" content=\"light dark\"><meta name=\"supported-color-schemes\" content=\"light dark\">")
            .Append($"<title>{WebUtility.HtmlEncode(subject)}</title><style>a{{color:#0A58CA}}{DarkMode}</style></head>")
            .Append("<body class=\"fondo\" style=\"margin:0;padding:0;background:#F5F7FA\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr><td align=\"center\" style=\"padding:24px 12px\">")
            .Append("<table role=\"presentation\" class=\"tarjeta\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" ")
            .Append("style=\"max-width:600px;background:#FFFFFF;border:1px solid #E3E8EF;border-radius:12px\">")
            .Append("<tr><td style=\"background:#0A58CA;border-radius:12px 12px 0 0;padding:16px 24px\">")
            .Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>")
            .Append($"<td style=\"padding-right:12px\"><img src=\"cid:{IconContentId}\" width=\"48\" height=\"48\" alt=\"\" style=\"display:block;border:0;border-radius:10px\"></td>")
            .Append($"<td style=\"font-family:{Font};font-size:22px;font-weight:700;color:#FFFFFF\">ResidApp</td>")
            .Append("</tr></table></td></tr>")
            .Append($"<tr><td style=\"padding:24px;font-family:{Font}\">");
        foreach (var (paragraph, highlighted) in paragraphs)
        {
            var box = highlighted ? "padding:12px 16px;background:#EAF2FF;border-left:4px solid #0D6EFD;" : "";
            html.Append($"<p class=\"texto{(highlighted ? " destacado" : "")}\" style=\"margin:0 0 16px;{box}font-size:16px;line-height:1.5;color:#1A2433;overflow-wrap:anywhere\">{paragraph}</p>");
        }

        html.Append($"<p class=\"pie\" style=\"margin:24px 0 0;padding-top:16px;border-top:1px solid #E3E8EF;font-size:14px;line-height:1.5;color:#5B6676\">{footerHtml}</p>")
            .Append("</td></tr></table></td></tr></table></body></html>");
        return html.ToString();
    }

    private static byte[] LoadIcon()
    {
        using var resource = typeof(EmailLayout).Assembly.GetManifestResourceStream("ResidApp.Email.icono-correo.png")
            ?? throw new InvalidOperationException("Falta el recurso incrustado del icono del correo.");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        return buffer.ToArray();
    }
}
