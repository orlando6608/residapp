using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>Comunicados a la familia (script 0048; CJ, 2026-10-07): los de las unidades del ámbito con su hora de publicación, y la
/// publicación anticipada, que solo hace Administración.</summary>
public sealed partial class AdministracionController
{
    public async Task<IActionResult> Comunicados(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Comunicados)) });
        }

        var result = await familyCommunications.ListAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        return View(result.Value ?? []);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PublicarComunicado(Guid comunicadoId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await familyCommunications.PublishNowAsync(
            new PublishFamilyCommunicationCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), comunicadoId), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Comunicado publicado antes de su hora. Queda en la auditoría.";
        }
        else
        {
            TempData["Error"] = result.Error!.Code switch
            {
                ApplicationFailureCode.AccessDenied => "No se puede acceder a ese comunicado.",
                ApplicationFailureCode.Conflict => "Ese comunicado ya estaba publicado.",
                _ => result.Error.Message,
            };
        }

        return RedirectToAction(nameof(Comunicados));
    }
}
