using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>Traslado del residente por Enfermería, solo con el permiso RESIDENT_TRANSFER que concede Administración (script 0108; CJ, 2026-10-07:
/// «inicialmente Administración»). El formulario y las reglas son los del traslado de Administración; la autorización la vuelve a comprobar
/// <see cref="ResidentTransferApplicationService"/>, así que esta comprobación solo orienta.</summary>
public sealed partial class EnfermeriaController
{
    public async Task<IActionResult> Trasladar(Guid residenteId, CancellationToken ct)
    {
        if (residenteId == Guid.Empty)
        {
            return RedirectToAction(nameof(Residentes));
        }
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Trasladar), new { residenteId }) });
        }

        var found = await service.FindScopeResidentAsync(
            new FindScopeResidentCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(residenteId)), ct);
        if (!found.Ok || found.Value is null)
        {
            return RedirectToAction(nameof(Residentes));
        }
        if (!await CanTransferAsync(activeScope, ct))
        {
            TempData["Error"] = "No tienes permiso para trasladar residentes. Pídeselo a Administración.";
            return RedirectToAction(nameof(Residente), new { residenteId });
        }

        return View(await TransferViewAsync(activeScope, found.Value, new TransferResidentFormModel
        {
            ResidenteId = residenteId,
            UnidadOrigenId = found.Value.UnitId.Value,
            OperacionId = Guid.NewGuid(),
            UnidadDestinoId = found.Value.UnitId.Value,
        }, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Trasladar([Bind(Prefix = "Form")] TransferResidentFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var centerId = CenterId.From(activeScope.CenterId);
        var found = await service.FindScopeResidentAsync(
            new FindScopeResidentCommand(activeScope.ProfileScopeId, centerId, ResidentId.From(form.ResidenteId)), ct);
        if (!found.Ok || found.Value is null)
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
                TempData["Mensaje"] = TransferTexts.Success(result.Value!);
                return RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId });
            }

            ModelState.AddModelError(string.Empty, TransferTexts.Failure(result.Error!));
        }

        return View(await TransferViewAsync(activeScope, found.Value, form, ct));
    }

    /// <summary>Si Administración ha concedido a este ámbito de perfil el permiso de trasladar. Solo orienta (muestra u oculta el botón): el traslado se
    /// autoriza otra vez al guardarlo.</summary>
    private async Task<bool> CanTransferAsync(ActiveProfileScopeCookieValue activeScope, CancellationToken ct) =>
        await listPermissions.ExecuteAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ct)
            is { Ok: true } permissions
        && permissions.Value!.Contains(ProfilePermissions.ResidentTransfer);

    private async Task<TransferResidentViewModel> TransferViewAsync(
        ActiveProfileScopeCookieValue activeScope, ScopeResidentSummary resident, TransferResidentFormModel form, CancellationToken ct)
    {
        var centerId = CenterId.From(activeScope.CenterId);
        var units = await listUnits.ExecuteAsync(activeScope.ProfileScopeId, centerId, ct);
        var locations = await listLocations.ExecuteAsync(activeScope.ProfileScopeId, centerId, ct);
        return new TransferResidentViewModel(
            resident.ResidentId, resident.DisplayName, resident.UnitName ?? string.Empty, form,
            units.Ok ? units.Value!.Select(u => new SelectListItem(u.Name, u.UnitId.Value.ToString())).ToList() : [],
            locations.Ok ? locations.Value! : []);
    }
}
