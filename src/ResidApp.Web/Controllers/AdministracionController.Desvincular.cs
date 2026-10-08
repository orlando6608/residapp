using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Residents;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>Desvincular a un familiar de un residente con un motivo (script 0045; CJ, 2026-10-07).</summary>
public sealed partial class AdministracionController
{
    public async Task<IActionResult> DesvincularFamiliar(Guid residenteId, Guid vinculoId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(DesvincularFamiliar), new { residenteId, vinculoId }) });
        }

        var detail = await FindAsync(activeScope, residenteId, ct);
        var member = detail?.Family.FirstOrDefault(f => f.LinkId == vinculoId);
        if (member is null)
        {
            return detail is null ? RedirectToAction(nameof(Residentes)) : RedirectToAction(nameof(Residente), new { residenteId });
        }

        return View(new UnlinkFamilyViewModel(detail!.Resident, member, new UnlinkFamilyFormModel { ResidenteId = residenteId, VinculoId = vinculoId }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DesvincularFamiliar([Bind(Prefix = "Form")] UnlinkFamilyFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (ModelState.IsValid)
        {
            var current = await FindAsync(activeScope, form.ResidenteId, ct);
            if (current is not null && current.CurrentEmergencyContacts is [var only] && only == form.VinculoId)
            {
                // Al menos un contacto urgente (CJ, 2026-10-07); el repositorio lo vuelve a comprobar dentro de la transacción.
                ModelState.AddModelError(string.Empty, "Es el único contacto urgente del residente: designa antes otro y vuelve a desvincularlo.");
            }
        }

        if (ModelState.IsValid)
        {
            var result = await service.UnlinkFamilyMemberAsync(new UnlinkFamilyMemberCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(form.ResidenteId), form.VinculoId, form.Motivo), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Familiar desvinculado. El vínculo queda en el historial de la ficha.";
                return RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId });
            }

            if (result.Error!.Code == ApplicationFailureCode.AccessDenied)
            {
                return RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId });
            }

            ModelState.AddModelError(string.Empty, result.Error.Code switch
            {
                ApplicationFailureCode.Conflict => "Este familiar ya estaba desvinculado. Revisa la ficha.",
                ApplicationFailureCode.InvalidInput => "Revisa el motivo (obligatorio, hasta 500 caracteres). Si este familiar es el único contacto urgente, designa antes otro.",
                _ => result.Error.Message,
            });
        }

        var detail = await FindAsync(activeScope, form.ResidenteId, ct);
        var member = detail?.Family.FirstOrDefault(f => f.LinkId == form.VinculoId);
        return member is null
            ? RedirectToAction(nameof(Residentes))
            : View(new UnlinkFamilyViewModel(detail!.Resident, member, form));
    }
}
