using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Vertical Medicina, por ahora solo su historia 1 en lectura: MED-01 (inicio con el contador de
/// escalados), MED-02 (bandeja de escalados) y MED-03 (detalle del escalado, con las fuentes de solo
/// lectura). La valoración médica y la conducta llegarán con el resto del vertical. Traduce a
/// MedicinaApplicationService; la autorización y las reglas de negocio no viven aquí.
/// </summary>
public sealed class MedicinaController(MedicinaApplicationService service) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Index)) });
        }

        var escalados = await service.ListEscalationsAsync(
            new ListEscalationsCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        if (!escalados.Ok)
        {
            ModelState.AddModelError(string.Empty, escalados.Error!.Message);
        }
        return View(new MedicinaInicioViewModel(escalados.Ok ? escalados.Value!.Count : 0));
    }

    /// <summary>MED-02: bandeja de escalados, del más antiguo al más reciente.</summary>
    public async Task<IActionResult> Escalados(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Escalados)) });
        }

        var result = await service.ListEscalationsAsync(
            new ListEscalationsCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(Array.Empty<EscalationSummary>());
        }

        return View(result.Value);
    }

    /// <summary>MED-03: detalle de un escalado. Si no existe, no es un escalado o no está en el ámbito, se
    /// vuelve a la bandeja sin distinguir el motivo.</summary>
    public async Task<IActionResult> Escalado(Guid eventoId, CancellationToken ct)
    {
        if (eventoId == Guid.Empty)
        {
            return RedirectToAction(nameof(Escalados));
        }
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Escalado), new { eventoId }) });
        }

        var centroId = CenterId.From(activeScope.CenterId);
        var detail = await service.FindEscalationDetailAsync(new FindEscalationDetailCommand(activeScope.ProfileScopeId, centroId, eventoId), ct);
        if (!detail.Ok || detail.Value is null)
        {
            return RedirectToAction(nameof(Escalados));
        }

        var baseline = await service.ReadCurrentBaselineAsync(
            new ReadCurrentBaselineCommand(activeScope.ProfileScopeId, centroId, detail.Value.ResidentId), ct);
        return View(new MedicinaEscaladoViewModel(detail.Value, baseline.Ok ? baseline.Value : null));
    }
}
