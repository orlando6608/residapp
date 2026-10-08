using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Residents;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>Baja y reactivación del residente y suspensión por ingreso hospitalario prolongado (script 0041; CJ, 2026-10-07).</summary>
public sealed partial class AdministracionController
{
    public async Task<IActionResult> Bajas(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Bajas)) });
        }

        var result = await statuses.ListDischargedAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        return View(new DischargedResidentsViewModel(result.Value ?? [], DateOnly.FromDateTime(DateTime.Today)));
    }

    public async Task<IActionResult> DarDeBaja(Guid residenteId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(DarDeBaja), new { residenteId }) });
        }

        var found = await FindActiveResidentAsync(activeScope, residenteId, ct);
        return found is null
            ? RedirectToAction(nameof(Residentes))
            : View(new DischargeResidentViewModel(found, new DischargeResidentFormModel { ResidenteId = residenteId, OperacionId = Guid.NewGuid() }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DarDeBaja([Bind(Prefix = "Form")] DischargeResidentFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var found = await FindActiveResidentAsync(activeScope, form.ResidenteId, ct);
        if (found is null)
        {
            // Un reenvío ya hecho: el residente ya no está activo y la lista de bajas lo enseña.
            return RedirectToAction(nameof(Bajas));
        }

        if (ModelState.IsValid)
        {
            var result = await statuses.DischargeAsync(new DischargeResidentCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(form.ResidenteId), form.OperacionId,
                form.Motivo, form.MotivoTexto), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = $"{found.DisplayName} se ha dado de baja. Sus datos se conservan {ResidentDischarge.RetentionYears} años."
                    + (result.Value > 0 ? $" Se {(result.Value == 1 ? "cerró 1 episodio abierto" : $"cerraron {result.Value} episodios abiertos")} por fallecimiento." : string.Empty);
                return RedirectToAction(nameof(Bajas));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.InvalidInput => "Si el motivo es «Otro motivo», escribe cuál (hasta 500 caracteres).",
                ApplicationFailureCode.Conflict => "El estado del residente ha cambiado mientras preparabas la baja. Revisa su ficha.",
                _ => result.Error.Message,
            });
        }

        return View(new DischargeResidentViewModel(found, form));
    }

    public async Task<IActionResult> Reactivar(Guid residenteId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Reactivar), new { residenteId }) });
        }

        var found = await statuses.FindDischargedAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(residenteId), ct);
        return found.Ok
            ? View(await ReactivateViewAsync(activeScope, found.Value!, new ReactivateResidentFormModel { ResidenteId = residenteId, OperacionId = Guid.NewGuid() }, ct))
            : RedirectToAction(nameof(Bajas));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivar([Bind(Prefix = "Form")] ReactivateResidentFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var centerId = CenterId.From(activeScope.CenterId);
        var found = await statuses.FindDischargedAsync(activeScope.ProfileScopeId, centerId, ResidentId.From(form.ResidenteId), ct);
        if (!found.Ok)
        {
            // Un reenvío ya hecho: la baja ya no está pendiente y el residente vuelve a estar en la lista.
            return RedirectToAction(nameof(Residentes));
        }

        var location = form.ParseLocation();
        if (location is null)
        {
            ModelState.AddModelError(nameof(form.Ubicacion), "Elige una ubicación de la lista.");
        }

        if (ModelState.IsValid)
        {
            var result = await statuses.ReactivateAsync(new ReactivateResidentCommand(
                activeScope.ProfileScopeId, centerId, ResidentId.From(form.ResidenteId), form.OperacionId, UnitId.From(form.UnidadId!.Value),
                location!.Value.RoomId, location.Value.PlaceId), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Residente reactivado.";
                return RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId });
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => "Esa plaza ya está ocupada, o el estado del residente ha cambiado. Revisa la lista de bajas.",
                ApplicationFailureCode.InvalidInput =>
                    "Revisa el destino: tiene que ser una unidad activa de tu ámbito, con una habitación o plaza disponibles de esa unidad.",
                _ => result.Error.Message,
            });
        }

        return View(await ReactivateViewAsync(activeScope, found.Value!, form, ct));
    }

    public async Task<IActionResult> Suspender(Guid residenteId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Suspender), new { residenteId }) });
        }

        var found = await FindActiveResidentAsync(activeScope, residenteId, ct);
        return found is null
            ? RedirectToAction(nameof(Residentes))
            : View(new SuspendResidentViewModel(found, new SuspendResidentFormModel { ResidenteId = residenteId, OperacionId = Guid.NewGuid() }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Suspender([Bind(Prefix = "Form")] SuspendResidentFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var found = await FindActiveResidentAsync(activeScope, form.ResidenteId, ct);
        if (found is null)
        {
            return RedirectToAction(nameof(Residentes));
        }

        if (ModelState.IsValid)
        {
            var result = await statuses.SuspendAsync(new SuspendResidentCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(form.ResidenteId), form.OperacionId, form.Nota), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Residente suspendido: no se puede actuar sobre él hasta que lo reanudes.";
                return RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId });
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code == ApplicationFailureCode.Conflict
                ? "El residente ya estaba suspendido o ha cambiado de estado. Revisa su ficha."
                : result.Error.Message);
        }

        return View(new SuspendResidentViewModel(found, form));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reanudar(Guid residenteId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await statuses.ResumeAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(residenteId), ct);
        TempData["Mensaje"] = result.Ok
            ? "Atención reanudada: ya se puede actuar sobre el residente."
            : result.Error!.Code == ApplicationFailureCode.Conflict ? "El residente no estaba suspendido." : result.Error.Message;
        return RedirectToAction(nameof(Residente), new { residenteId });
    }

    private async Task<AdministrativeResidentSummary?> FindActiveResidentAsync(ActiveProfileScopeCookieValue activeScope, Guid residenteId, CancellationToken ct)
    {
        var found = await service.FindResidentAsync(new FindAdministrativeResidentQuery(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(residenteId)), ct);
        return found.Ok ? found.Value!.Resident : null;
    }

    private async Task<ReactivateResidentViewModel> ReactivateViewAsync(
        ActiveProfileScopeCookieValue activeScope, DischargedResident resident, ReactivateResidentFormModel form, CancellationToken ct)
    {
        var centerId = CenterId.From(activeScope.CenterId);
        var units = await listUnits.ExecuteAsync(activeScope.ProfileScopeId, centerId, ct);
        var locations = await listLocations.ExecuteAsync(activeScope.ProfileScopeId, centerId, ct);
        return new ReactivateResidentViewModel(
            resident, form,
            units.Ok ? units.Value!.Select(u => new SelectListItem(u.Name, u.UnitId.Value.ToString())).ToList() : [],
            locations.Ok ? locations.Value! : []);
    }
}
