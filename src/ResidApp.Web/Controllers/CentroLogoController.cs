using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Ports;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// El logo del centro del ámbito activo (script 0068), para el encabezado de las pantallas. El ámbito de la cookie solo vale si pertenece a la
/// cuenta de la sesión (SqlCenterBrandingDirectory); sin sesión, con un ámbito ajeno, de Plataforma o sin logo, 404 sin distinguir el motivo (y
/// nunca una redirección, que el &lt;img&gt; no sabría mostrar). La dirección cambia con cada logo nuevo (?v=), así que el navegador puede guardarlo
/// un día.
/// </summary>
public sealed class CentroLogoController(ICenterBrandingDirectory branding) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Ver(CancellationToken ct)
    {
        var subject = SessionAuthentication.Subject(User);
        var activeScope = subject is null ? null : ActiveProfileScopeCookie.Read(Request);
        if (subject is null || activeScope is null)
        {
            return NotFound();
        }

        var logo = await branding.ReadLogoForScopeAsync(subject, activeScope.ProfileScopeId, ct);
        if (logo is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=86400";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        return File(logo.Content, logo.ContentType);
    }
}
