using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>Administración «principal» del centro (script 0047; CJ, 2026-10-07): ve todas las unidades del centro, se añade las que no tiene y
/// marca a otras Administraciones como principales.</summary>
public sealed partial class AdministracionController
{
    public async Task<IActionResult> AmbitoCentro(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(AmbitoCentro)) });
        }

        var result = await scopeAdmin.ReadAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        return View(new AdministrationScopeViewModel(result.Value ?? new AdministrationScopeView(false, [])));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnadirUnidadAlAmbito(Guid unidadId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await scopeAdmin.AddUnitAsync(new AddUnitToOwnScopeCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), UnitId.From(unidadId)), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Unidad añadida a tu ámbito. Queda en la auditoría.";
        }
        else
        {
            TempData["Error"] = result.Error!.Code switch
            {
                ApplicationFailureCode.AccessDenied => "Solo la Administración principal del centro puede añadirse unidades.",
                ApplicationFailureCode.Conflict => "Esa unidad ya está en tu ámbito.",
                ApplicationFailureCode.InvalidInput => "Esa unidad no existe o no está activa en este centro.",
                _ => result.Error.Message,
            };
        }

        return RedirectToAction(nameof(AmbitoCentro));
    }

    /// <summary>Marca o desmarca como principal el ámbito de Administración de otra cuenta. Vuelve a la ficha de ese perfil.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarcarPrincipal(Guid cuentaId, Guid ambitoId, bool principal, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await scopeAdmin.SetPrincipalAsync(new SetAdministrationPrincipalCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ambitoId, principal), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = principal ? "Marcada como Administración principal." : "Ya no es Administración principal.";
        }
        else
        {
            TempData["Error"] = result.Error!.Code switch
            {
                ApplicationFailureCode.AccessDenied => "Solo la Administración principal del centro puede marcar o quitar la marca de principal.",
                ApplicationFailureCode.Conflict => "Ese perfil ya estaba así. Revisa la ficha.",
                ApplicationFailureCode.InvalidInput => "El centro tiene que conservar al menos una Administración principal: marca antes a otra.",
                _ => result.Error.Message,
            };
        }

        return RedirectToAction(nameof(PerfilUsuario), new { cuentaId, ambitoId });
    }
}
