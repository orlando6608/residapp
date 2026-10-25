using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
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

    [HttpGet]
    public async Task<IActionResult> PublicarCorreccion(Guid comunicadoId, CancellationToken ct)
    {
        var item = await FindFamilyCommunicationAsync(comunicadoId, ct);
        if (item is null || !item.CanCorrect)
        {
            TempData["Error"] = "Ese comunicado no admite una corrección: no está publicado, está retirado, la familia ya no lo ve o no es de tu ámbito.";
            return RedirectToAction(nameof(Comunicados));
        }

        return View("PublicarCorreccion", new PublicarCorreccionViewModel(item, new PublicarCorreccionFormModel { ComunicadoId = comunicadoId }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PublicarCorreccion([Bind(Prefix = "Form")] PublicarCorreccionFormModel form, CancellationToken ct)
    {
        var item = await FindFamilyCommunicationAsync(form.ComunicadoId, ct);
        if (item is null)
        {
            return RedirectToAction(nameof(Comunicados));
        }
        if (!ModelState.IsValid)
        {
            return View("PublicarCorreccion", new PublicarCorreccionViewModel(item, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await familyCommunications.PublishCorrectiveAsync(new PublishFamilyCommunicationCorrectiveCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.ComunicadoId, form.Tipo, form.Texto), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Corrección publicada. La familia la ve junto al comunicado original. Queda en la auditoría.";
            return RedirectToAction(nameof(Comunicados));
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.AccessDenied => "No se puede acceder a ese comunicado.",
            ApplicationFailureCode.Conflict => "No se ha publicado: el comunicado aún no está publicado, se ha retirado o la familia ya no lo ve. Vuelve a la lista para ver su estado.",
            ApplicationFailureCode.InvalidInput => "Revisa los datos: elige el tipo y escribe un texto de hasta 2000 caracteres.",
            _ => result.Error.Message,
        });
        return View("PublicarCorreccion", new PublicarCorreccionViewModel(item, form));
    }

    [HttpGet]
    public async Task<IActionResult> RetirarComunicado(Guid comunicadoId, CancellationToken ct)
    {
        var item = await FindFamilyCommunicationAsync(comunicadoId, ct);
        if (item is null || !item.CanAmend)
        {
            TempData["Error"] = "Ese comunicado no se puede retirar: no está publicado, ya está retirado o no es de tu ámbito.";
            return RedirectToAction(nameof(Comunicados));
        }

        return View("RetirarComunicado", new RetirarComunicadoViewModel(item, new RetirarComunicadoFormModel { ComunicadoId = comunicadoId }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetirarComunicado([Bind(Prefix = "Form")] RetirarComunicadoFormModel form, CancellationToken ct)
    {
        var item = await FindFamilyCommunicationAsync(form.ComunicadoId, ct);
        if (item is null)
        {
            return RedirectToAction(nameof(Comunicados));
        }
        if (!ModelState.IsValid)
        {
            return View("RetirarComunicado", new RetirarComunicadoViewModel(item, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await familyCommunications.WithdrawAsync(new WithdrawFamilyCommunicationCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.ComunicadoId, form.Motivo), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Comunicado retirado. La familia ve que ya no está disponible. Queda en la auditoría con tu motivo.";
            return RedirectToAction(nameof(Comunicados));
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.AccessDenied => "No se puede acceder a ese comunicado.",
            ApplicationFailureCode.Conflict => "No se ha retirado: el comunicado aún no está publicado o ya estaba retirado. Vuelve a la lista para ver su estado.",
            ApplicationFailureCode.InvalidInput => "Revisa los datos: escribe el motivo de la retirada, de hasta 500 caracteres.",
            _ => result.Error.Message,
        });
        return View("RetirarComunicado", new RetirarComunicadoViewModel(item, form));
    }

    private async Task<FamilyCommunicationStatusView?> FindFamilyCommunicationAsync(Guid communicationId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return null;
        }

        var result = await familyCommunications.ListAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ct);
        return result.Ok ? result.Value!.FirstOrDefault(c => c.Communication.Id == communicationId) : null;
    }
}
