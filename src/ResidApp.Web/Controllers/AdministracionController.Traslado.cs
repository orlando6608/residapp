using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>Traslado del residente a otra unidad del centro, o a otra habitación o plaza (script 0040; CJ, 2026-10-07).</summary>
public sealed partial class AdministracionController
{
    public async Task<IActionResult> Trasladar(Guid residenteId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Trasladar), new { residenteId }) });
        }

        var found = await service.FindResidentAsync(new FindAdministrativeResidentQuery(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(residenteId)), ct);
        if (!found.Ok)
        {
            return RedirectToAction(nameof(Residentes));
        }

        var resident = found.Value!.Resident;
        return View(await TransferViewAsync(activeScope, resident, new TransferResidentFormModel
        {
            ResidenteId = residenteId,
            UnidadOrigenId = resident.UnitId.Value,
            OperacionId = Guid.NewGuid(),
            UnidadDestinoId = resident.UnitId.Value,
        }, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Trasladar(
        [Bind(Prefix = "Form")] TransferResidentFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var centerId = CenterId.From(activeScope.CenterId);
        var found = await service.FindResidentAsync(new FindAdministrativeResidentQuery(
            activeScope.ProfileScopeId, centerId, ResidentId.From(form.ResidenteId)), ct);
        if (!found.Ok)
        {
            return RedirectToAction(nameof(Residentes));
        }

        var location = form.ParseLocation();
        if (location is null)
        {
            ModelState.AddModelError(nameof(form.Ubicacion), "Elige una ubicación de la lista.");
        }

        if (ModelState.IsValid)
        {
            var result = await transfers.TransferAsync(new TransferResidentCommand(
                activeScope.ProfileScopeId, centerId, ResidentId.From(form.ResidenteId), UnitId.From(form.UnidadOrigenId),
                UnitId.From(form.UnidadDestinoId!.Value), location!.Value.RoomId, location.Value.PlaceId, form.OperacionId), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = TransferMessage(result.Value!);
                return RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId });
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict =>
                    "El residente ya no está donde lo veías, esa plaza está ocupada o el traslado ya se hizo. Revisa la ficha y vuelve a intentarlo.",
                ApplicationFailureCode.InvalidInput =>
                    "Revisa el destino: tiene que ser una unidad activa de tu ámbito, con una habitación o plaza disponibles de esa unidad, y algo tiene que cambiar.",
                _ => result.Error.Message,
            });
        }

        return View(await TransferViewAsync(activeScope, found.Value!.Resident, form, ct));
    }

    private static string TransferMessage(TransferResidentResult result) =>
        "Residente trasladado."
        + (result.OpenEventsMoved > 0
            ? $" {result.OpenEventsMoved} {(result.OpenEventsMoved == 1 ? "episodio abierto pasa" : "episodios abiertos pasan")} a la unidad de destino."
            : string.Empty)
        + (result.BaselineDraftCancelled ? " Se canceló el borrador de basal: se rehace en la unidad de destino." : string.Empty);

    private async Task<TransferResidentViewModel> TransferViewAsync(
        ActiveProfileScopeCookieValue activeScope, AdministrativeResidentSummary resident, TransferResidentFormModel form, CancellationToken ct)
    {
        var centerId = CenterId.From(activeScope.CenterId);
        var units = await listUnits.ExecuteAsync(activeScope.ProfileScopeId, centerId, ct);
        var locations = await listLocations.ExecuteAsync(activeScope.ProfileScopeId, centerId, ct);
        return new TransferResidentViewModel(
            resident, form,
            units.Ok ? units.Value!.Select(u => new SelectListItem(u.Name, u.UnitId.Value.ToString())).ToList() : [],
            locations.Ok ? locations.Value! : []);
    }
}
