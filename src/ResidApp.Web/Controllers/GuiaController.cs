using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResidApp.Web.Models;

namespace ResidApp.Web.Controllers;

/// <summary>Guía por perfil: «soy X, ¿cómo hago Y?». Es pública y sin sesión, como el Manual y el Flujo de trabajo: no contiene datos de nadie.</summary>
[AllowAnonymous]
public class GuiaController(GuiaCatalogo catalogo) : Controller
{
    public IActionResult Index() => View(catalogo);

    /// <summary>El capítulo «Para todos». La ruta literal gana a «Guia/{slug}».</summary>
    [HttpGet("Guia/" + GuiaComun.Slug)]
    public IActionResult Comun() => catalogo.Comun is { } comun ? View(comun) : NotFound();

    [HttpGet("Guia/{slug}")]
    public IActionResult Perfil(string slug)
    {
        var perfil = catalogo.Perfil(slug);
        return perfil is null ? NotFound() : View(perfil);
    }
}
