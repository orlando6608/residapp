using Microsoft.AspNetCore.Mvc;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Selector de identidad ficticia para desarrollo local, no autenticación real (ver
/// DevSessionIdentityProvider). Permite escribir el sujeto_externo de una cuenta ya sembrada en
/// dbo.cuentas (por ejemplo, "dev-admin" del script database/seed/dev_seed_residente_basal.sql) para
/// ejercitar el motor de autorización deny-by-default sin depender de un proveedor productivo todavía sin
/// decidir.
/// </summary>
public sealed class DevAuthController : Controller
{
    public IActionResult Login() => View((object?)Request.Cookies[DevSessionIdentityProvider.CookieName]);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Login(string externalSubject)
    {
        if (string.IsNullOrWhiteSpace(externalSubject))
        {
            ModelState.AddModelError(string.Empty, "Escribe el sujeto externo de una cuenta sembrada.");
            return View((object?)null);
        }

        Response.Cookies.Append(DevSessionIdentityProvider.CookieName, externalSubject.Trim(), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
        });
        ActiveProfileScopeCookie.Clear(Response);
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        Response.Cookies.Delete(DevSessionIdentityProvider.CookieName);
        ActiveProfileScopeCookie.Clear(Response);
        return RedirectToAction("Index", "Home");
    }

    /// <summary>Maqueta visual, no funcional: la creación de cuentas depende del proveedor de
    /// autenticación productivo, todavía sin decidir (ver docs/producto/roadmap.md, "Decisiones
    /// abiertas"), y de la gestión de identidades del vertical Administración, aún no iniciado.</summary>
    public IActionResult Register() => View();
}
