using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Perfil de plataforma (script 0026): lista de centros y alta de un centro con su primera unidad y su primer administrador.
/// Ninguna pantalla muestra residentes ni contenido clínico. Traduce a PlatformApplicationService; la autorización no vive aquí.
/// </summary>
public sealed class PlataformaController(PlatformApplicationService service) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Index)) });
        }

        var result = await service.ListCentersAsync(Query(activeScope), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        return View(result.Value ?? []);
    }

    public IActionResult NuevoCentro()
    {
        if (ActiveProfileScopeCookie.Read(Request) is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(NuevoCentro)) });
        }

        return View(new NewCenterViewModel(new NewCenterFormModel { OperacionId = Guid.NewGuid() }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuevoCentro([Bind(Prefix = "Form")] NewCenterFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (ModelState.IsValid)
        {
            var result = await service.CreateCenterAsync(new CreateCenterCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.OperacionId, form.CodigoCentro, form.NombreCentro,
                form.CodigoUnidad, form.NombreUnidad, form.IdentificadorAdministrador, form.NombreAdministrador), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Centro creado. Su administrador ya puede entrar con su identificador de acceso.";
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict =>
                    "Ya existe un centro con ese código o una cuenta con ese identificador de acceso. Cambia los datos y vuelve a intentarlo.",
                ApplicationFailureCode.InvalidInput =>
                    "Revisa los datos: los códigos llevan de 2 a 64 caracteres (letras sin acentos, dígitos, «-» o «_»), el identificador de acceso de 3 a 200 sin espacios (letras, dígitos, «.», «_», «-» o «@») y los nombres son obligatorios.",
                _ => result.Error.Message,
            });
        }

        return View(new NewCenterViewModel(form));
    }

    private static PlatformQuery Query(ActiveProfileScopeCookieValue activeScope) =>
        new(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId));
}
