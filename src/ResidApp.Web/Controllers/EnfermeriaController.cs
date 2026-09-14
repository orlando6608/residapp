using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Vertical Enfermería, grupo E1 (navegación base): ENF-01 (inicio, cascarón hasta que existan bandejas
/// que contar), ENF-17 (residentes del ámbito) y ENF-18 (ficha del residente, con el mismo resumen de
/// basal vigente que AUX-03). Traduce a EnfermeriaApplicationService; la autorización y las reglas de
/// negocio no viven aquí.
/// </summary>
public sealed class EnfermeriaController(EnfermeriaApplicationService service) : Controller
{
    public IActionResult Index()
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        return activeScope is null
            ? RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Index)) })
            : View();
    }

    public async Task<IActionResult> Residentes(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Residentes)) });
        }

        var command = new ListScopeResidentsCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId));
        var result = await service.ListScopeResidentsAsync(command, ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(Array.Empty<ScopeResidentSummary>());
        }

        return View(result.Value);
    }

    public async Task<IActionResult> Residente(Guid residenteId, CancellationToken ct)
    {
        if (residenteId == Guid.Empty)
        {
            return RedirectToAction(nameof(Residentes));
        }
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Residente), new { residenteId }) });
        }

        var centroId = CenterId.From(activeScope.CenterId);
        var findResult = await service.FindScopeResidentAsync(
            new FindScopeResidentCommand(activeScope.ProfileScopeId, centroId, ResidentId.From(residenteId)), ct);
        if (!findResult.Ok || findResult.Value is null)
        {
            return RedirectToAction(nameof(Residentes));
        }

        var baselineResult = await service.ReadCurrentBaselineAsync(
            new ReadCurrentBaselineCommand(activeScope.ProfileScopeId, centroId, findResult.Value.ResidentId), ct);
        return View(new EnfermeriaResidentDetailViewModel(findResult.Value, baselineResult.Ok ? baselineResult.Value : null));
    }
}
