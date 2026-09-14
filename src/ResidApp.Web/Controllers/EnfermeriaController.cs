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
/// basal vigente que AUX-03); grupo E3: ENF-16 (registrar evento propio, solo alta y guardado). Traduce a
/// EnfermeriaApplicationService; la autorización y las reglas de negocio no viven aquí.
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

    /// <summary>ENF-16: formulario de alta. "Continuar directamente la valoración" queda pendiente hasta
    /// que exista esa pantalla (grupo E5); por ahora, guardar vuelve a la ficha del residente.</summary>
    public async Task<IActionResult> RegistrarEvento(Guid residenteId, CancellationToken ct)
    {
        var resolved = await ResolveScopeResidentAsync(residenteId, ct);
        if (resolved is null)
        {
            return RedirectToAction(nameof(Residentes));
        }

        ViewBag.Resident = resolved.Value.Resident;
        return View(new RegistrarEventoFormModel { ResidenteId = residenteId, OperacionId = Guid.NewGuid() });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarEvento(RegistrarEventoFormModel form, CancellationToken ct)
    {
        var resolved = await ResolveScopeResidentAsync(form.ResidenteId, ct);
        if (resolved is null)
        {
            return RedirectToAction(nameof(Residentes));
        }
        if (!ModelState.IsValid)
        {
            ViewBag.Resident = resolved.Value.Resident;
            return View(form);
        }

        var command = new RegisterClinicalEventCommand(
            resolved.Value.Scope.ProfileScopeId, CenterId.From(resolved.Value.Scope.CenterId), ResidentId.From(form.ResidenteId),
            form.Observacion, form.Clasificacion, form.DatosClinicosPertinentes, form.OperacionId);
        var result = await service.RegisterClinicalEventAsync(command, ct);

        TempData[result.Ok ? "Mensaje" : "Error"] = result.Ok
            ? "Evento registrado. La valoración se completará cuando exista la bandeja correspondiente."
            : result.Error!.Message;
        return RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId });
    }

    /// <summary>Compartido por Residente y RegistrarEvento: confirma que el residente está en el ámbito
    /// (ENF-17, mismo criterio que la lista). Si no lo está, el llamador redirige a ENF-17 sin distinguir
    /// "no está en el ámbito" de "no existe".</summary>
    private async Task<(ActiveProfileScopeCookieValue Scope, ScopeResidentSummary Resident)?> ResolveScopeResidentAsync(
        Guid residenteId, CancellationToken ct)
    {
        if (residenteId == Guid.Empty)
        {
            return null;
        }
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return null;
        }
        var centroId = CenterId.From(activeScope.CenterId);
        var findResult = await service.FindScopeResidentAsync(
            new FindScopeResidentCommand(activeScope.ProfileScopeId, centroId, ResidentId.From(residenteId)), ct);
        return !findResult.Ok || findResult.Value is null ? null : (activeScope, findResult.Value);
    }
}
