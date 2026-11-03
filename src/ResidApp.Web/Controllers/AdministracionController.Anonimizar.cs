using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>Anonimizar a un familiar que pide que se borren sus datos (script 0087; Orlando, 2026-11-03). Solo la Administración principal.</summary>
public sealed partial class AdministracionController
{
    public async Task<IActionResult> AnonimizarFamiliar(Guid residenteId, Guid vinculoId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(AnonimizarFamiliar), new { residenteId, vinculoId }) });
        }

        var model = await AnonymizeViewAsync(activeScope, new AnonymizeFamilyFormModel { ResidenteId = residenteId, VinculoId = vinculoId }, ct);
        return model is null ? RedirectToAction(nameof(Residente), new { residenteId }) : View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnonimizarFamiliar([Bind(Prefix = "Form")] AnonymizeFamilyFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (ModelState.IsValid)
        {
            var result = await service.AnonymizeFamilyMemberAsync(new AnonymizeFamilyMemberCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(form.ResidenteId), form.VinculoId,
                form.Motivo, form.Confirmado, form.Version), ct);
            if (result.Ok)
            {
                // Sin el nombre: los mensajes de una pantalla a otra viajan en una cookie.
                TempData["Mensaje"] = "Familiar anonimizado. Anótalo en el registro de derechos de tu centro.";
                return RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId });
            }

            if (result.Error!.Code == ApplicationFailureCode.AccessDenied)
            {
                return RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId });
            }

            ModelState.AddModelError(string.Empty, result.Error.Message);
        }

        // Se vuelve a leer lo que se enseña, con su versión nueva: lo que ve quien confirma otra vez es lo vigente.
        ModelState.Remove("Form.Version");
        form.Version = string.Empty;
        var model = await AnonymizeViewAsync(activeScope, form, ct);
        return model is null ? RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId }) : View(model);
    }

    private async Task<AnonymizeFamilyViewModel?> AnonymizeViewAsync(ActiveProfileScopeCookieValue activeScope, AnonymizeFamilyFormModel form, CancellationToken ct)
    {
        var detail = await FindAsync(activeScope, form.ResidenteId, ct);
        var member = detail?.Family.FirstOrDefault(f => f.LinkId == form.VinculoId);
        if (member is null)
        {
            return null;
        }

        var preview = await service.PreviewFamilyAnonymizationAsync(new PreviewFamilyAnonymizationQuery(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(form.ResidenteId), form.VinculoId), ct);
        if (!preview.Ok)
        {
            return null;
        }

        form.Version = preview.Value!.Version;
        return new AnonymizeFamilyViewModel(detail!.Resident, member, preview.Value, form);
    }
}
