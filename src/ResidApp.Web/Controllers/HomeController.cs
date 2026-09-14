using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ResidApp.Web.Models;

namespace ResidApp.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
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
