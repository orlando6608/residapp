using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

// Inicio, Privacidad, Manual, la página sin conexión de la PWA y la de error se ven sin sesión.
[AllowAnonymous]
public class HomeController(ListActiveScopePermissions listPermissions) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Sin sesión (por ejemplo, caducada) no cuenta la cookie de ámbito que haya quedado.
        var signedIn = User.Identity?.IsAuthenticated == true;
        var activeScope = signedIn ? ActiveProfileScopeCookie.Read(Request) : null;
        if (signedIn && activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Index)) });
        }

        // El familiar tiene su propio portal, con su propio aspecto y sin nada del personal.
        if (activeScope?.Profile == SystemProfile.Familiar)
        {
            return RedirectToAction("Index", "PortalFamiliar");
        }

        // Las fichas que dependen de un permiso solo se enseñan si el ámbito activo lo tiene (0024). Solo orienta: cada
        // pantalla vuelve a autorizar.
        IReadOnlyList<string> permissions = activeScope is null
            ? []
            : (await listPermissions.ExecuteAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ct)).Value ?? [];
        ViewBag.Permisos = permissions;
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    public IActionResult Manual()
    {
        return View();
    }

    /// <summary>Fallback de navegación del service worker (wwwroot/sw.js) cuando no hay red — puramente
    /// informativa, sin formularios ni datos locales. Ver docs/decisiones-arquitectura/directrices-pwa-movil.md,
    /// punto 5: no es un modo de trabajo offline.</summary>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Offline()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
