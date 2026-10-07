using Microsoft.AspNetCore.Mvc;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Formulario de la clave de los documentos de CJ (/pendientes-cj): sin ella, el middleware de Program.cs redirige aquí. Anónimo.
/// Ver PendientesCjAccess: es un freno para el desarrollo, no autenticación real.
/// </summary>
public sealed class AccesoCjController(PendientesCjAccess access) : Controller
{
    [HttpGet]
    public IActionResult Index(string? returnUrl) =>
        access.HasAccess(Request) ? Redirect(Destination(returnUrl)) : View(new AccesoCjViewModel(returnUrl, false));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(string? clave, string? returnUrl)
    {
        if (access.KeyMatches(clave?.Trim()))
        {
            access.Grant(HttpContext);
            return Redirect(Destination(returnUrl));
        }

        // Una pausa por intento fallido: no hace fuerte la clave, solo frena los intentos a ritmo de máquina.
        await Task.Delay(TimeSpan.FromSeconds(1), HttpContext.RequestAborted);
        return View(new AccesoCjViewModel(returnUrl, true));
    }

    /// <summary>Un GET basta: solo cierra este acceso, no toca datos.</summary>
    [HttpGet]
    public IActionResult Salir()
    {
        access.Revoke(Response);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Solo se vuelve a una ruta local de los documentos de CJ; cualquier otra cosa va al índice.</summary>
    private string Destination(string? returnUrl) =>
        returnUrl is not null && Url.IsLocalUrl(returnUrl)
        && returnUrl.StartsWith(PendientesCjAccess.Prefix, StringComparison.OrdinalIgnoreCase)
            ? returnUrl
            : PendientesCjAccess.Home;
}
