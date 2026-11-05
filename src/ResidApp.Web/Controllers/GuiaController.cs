using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResidApp.Web.Models;

namespace ResidApp.Web.Controllers;

/// <summary>Guía por perfil: «soy X, ¿cómo hago Y?». Es pública y sin sesión, como el Manual y el Flujo de trabajo: no contiene datos de nadie.</summary>
[AllowAnonymous]
public class GuiaController(GuiaCatalogo catalogo) : Controller
{
    public IActionResult Index() => View(catalogo);

    [HttpGet("Guia/{slug}")]
    public IActionResult Perfil(string slug)
    {
        var perfil = catalogo.Perfil(slug);
        return perfil is null ? NotFound() : View(perfil);
    }
}
