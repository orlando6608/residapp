using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>Datos del propio centro (script 0068): Administración cambia su nombre y su logo, que sustituyen a la marca de ResidApp en el
/// encabezado, en la impresión y en los PDF de quien trabaja en el centro. Plataforma no los cambia una vez creado el centro (ADR 0006).</summary>
public sealed partial class AdministracionController
{
    public async Task<IActionResult> DatosCentro(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(DatosCentro)) });
        }

        var result = await centro.GetAsync(Query(activeScope), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(new DatosCentroViewModel(null, new DatosCentroFormModel()));
        }

        return View(new DatosCentroViewModel(result.Value, new DatosCentroFormModel { Nombre = result.Value!.Name }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DatosCentro([Bind(Prefix = "Form")] DatosCentroFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(DatosCentro)) });
        }

        if (ModelState.IsValid)
        {
            var result = await centro.RenameAsync(new RenameCenterCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.Nombre), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Nombre del centro guardado.";
                return RedirectToAction(nameof(DatosCentro));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.InvalidInput => "Escribe un nombre distinto del actual, de 1 a 200 caracteres.",
                ApplicationFailureCode.AccessDenied => "No tienes acceso a esta pantalla.",
                _ => result.Error.Message,
            });
        }

        var current = await centro.GetAsync(Query(activeScope), ct);
        return View(new DatosCentroViewModel(current.Ok ? current.Value : null, form));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(CenterLogoForm.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = CenterLogoForm.MaxRequestBytes)]
    public async Task<IActionResult> LogoCentro([Bind(Prefix = "Form")] LogoCentroFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(DatosCentro)) });
        }

        string? error = null;
        if (form.Logo is null || form.Logo.Length == 0)
        {
            error = "Elige un logo.";
        }
        else if (CenterLogoForm.TooLarge(form.Logo))
        {
            error = CenterLogoForm.Invalid;
        }
        else
        {
            var result = await centro.SetLogoAsync(new SetCenterLogoCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), await CenterLogoForm.ReadAsync(form.Logo, ct)), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Logo guardado.";
            }
            else
            {
                error = result.Error!.Code == ApplicationFailureCode.InvalidInput ? CenterLogoForm.Invalid : result.Error.Message;
            }
        }

        if (error is not null)
        {
            TempData["LogoError"] = error;
        }
        return RedirectToAction(nameof(DatosCentro));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuitarLogoCentro(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(DatosCentro)) });
        }

        var result = await centro.RemoveLogoAsync(Query(activeScope), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = result.Value ? "Logo quitado." : "El centro ya no tenía logo.";
        }
        else
        {
            TempData["LogoError"] = result.Error!.Message;
        }
        return RedirectToAction(nameof(DatosCentro));
    }
}
