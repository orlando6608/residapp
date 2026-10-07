using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Selector de identidad ficticia para desarrollo local, no autenticación real (ver
/// DevSessionIdentityProvider). Permite escribir el sujeto_externo de una cuenta ya sembrada en
/// dbo.cuentas (por ejemplo, "dev-admin" del script database/seed/dev_seed_residente_basal.sql) para
/// ejercitar el motor de autorización deny-by-default sin depender de un proveedor productivo todavía sin
/// decidir.
/// </summary>
public sealed class DevAuthController(IProfileScopeDirectoryProvider directory, ClinicalAccessDeclarations declarations) : Controller
{
    public IActionResult Login() => View((object?)Request.Cookies[DevSessionIdentityProvider.CookieName]);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string externalSubject, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(externalSubject))
        {
            ModelState.AddModelError(string.Empty, "Escribe el sujeto externo de una cuenta sembrada.");
            return View((object?)Request.Cookies[DevSessionIdentityProvider.CookieName]);
        }

        // Sin cuenta activa con algún ámbito activo no se podría operar: se rechaza aquí en vez de dejar la sesión
        // atrapada en la selección de ámbito. La identidad anterior, si la había, se conserva.
        if ((await directory.ListActiveAsync(externalSubject.Trim(), ct)).Count == 0)
        {
            ModelState.AddModelError(string.Empty, "No existe ninguna cuenta con ese usuario, o no tiene ningún ámbito activo. Revisa el nombre.");
            return View((object?)Request.Cookies[DevSessionIdentityProvider.CookieName]);
        }

        Response.Cookies.Append(DevSessionIdentityProvider.CookieName, externalSubject.Trim(), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
        });
        ActiveProfileScopeCookie.Clear(Response);
        ClinicalAccessDeclarationCookie.Clear(Response);
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        // Cerrar sesión termina las declaraciones de acceso clínico (CJ, 2026-10-06), mientras la identidad y el ámbito aún están.
        await declarations.EndAllAsync(ct);
        Response.Cookies.Delete(DevSessionIdentityProvider.CookieName);
        ActiveProfileScopeCookie.Clear(Response);
        ClinicalAccessDeclarationCookie.Clear(Response);
        return RedirectToAction("Index", "Home");
    }

    /// <summary>Maqueta visual, no funcional: la creación de cuentas depende del proveedor de
    /// autenticación productivo, todavía sin decidir (ver docs/producto/roadmap.md, "Decisiones
    /// abiertas"), y de la gestión de identidades del vertical Administración, aún no iniciado.</summary>
    public IActionResult Register() => View();
}
