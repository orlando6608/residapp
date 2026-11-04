using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace ResidApp.Web.Security;

/// <summary>Cabeceras de seguridad de todas las respuestas (hallazgo M3 de la revisión de seguridad). Las de siempre van en todo, también en los estáticos; la política de
/// contenido (CSP) y la orden de no guardar copia en el navegador solo en las respuestas de la aplicación (controladores): los documentos estáticos de wwwroot y de
/// pendientes-cj llevan su propio JavaScript y no pasan por aquí. La CSP deja ejecutar solo scripts de la propia web o con el «nonce» de esa petición (lo añade
/// <see cref="CspNonceTagHelper"/> a los scripts escritos dentro de la página). Con Seguridad:CspSoloInforme=true la política solo avisa en la consola del navegador, sin bloquear.</summary>
public static class SecurityHeaders
{
    public const string NonceKey = "csp-nonce";

    // Sin cámara, ubicación, pagos ni USB; el micrófono, solo de la propia web (dictado por voz, ADR 0009).
    internal const string PermissionsPolicy = "camera=(), geolocation=(), microphone=(self), payment=(), usb=()";

    internal static string Policy(string nonce) =>
        $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; " +
        "media-src 'self' blob:; worker-src 'self'; manifest-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, bool cspReportOnly)
    {
        var cspHeader = cspReportOnly ? "Content-Security-Policy-Report-Only" : "Content-Security-Policy";
        return app.Use(async (context, next) =>
        {
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            context.Items[NonceKey] = nonce;
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                // Las invitaciones llevan su código en la dirección: nada de «Referer» hacia ninguna parte.
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = PermissionsPolicy;
                headers["X-Frame-Options"] = "DENY";
                if (context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
                {
                    headers[cspHeader] = Policy(nonce);
                    // Fichas, portal familiar, fotos: nada se queda en la caché de un equipo compartido. Lo que una acción haya fijado a propósito (el antiforgery ya
                    // pone no-store en las páginas con formulario) se respeta.
                    if (!headers.ContainsKey("Cache-Control"))
                    {
                        headers["Cache-Control"] = "no-store";
                        headers["Pragma"] = "no-cache";
                    }
                }

                return Task.CompletedTask;
            });
            await next(context);
        });
    }
}


/// <summary>Pone el «nonce» de la petición a los scripts escritos dentro de la página (los que no tienen src), que es lo único que la CSP deja ejecutar además de los de la web.</summary>
[HtmlTargetElement("script")]
public sealed class CspNonceTagHelper(IHttpContextAccessor accessor) : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (context.AllAttributes.ContainsName("src") || context.AllAttributes.ContainsName("nonce"))
        {
            return;
        }

        if (accessor.HttpContext?.Items[SecurityHeaders.NonceKey] is string nonce)
        {
            output.Attributes.SetAttribute("nonce", nonce);
        }
    }
}
