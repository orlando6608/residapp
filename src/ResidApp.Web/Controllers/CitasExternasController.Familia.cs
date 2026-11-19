using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Los dos interruptores de las citas externas para la familia (script 0104; PR 5). «Ajustes para la familia» es del centro y solo lo cambia Administración, con tres confirmaciones al encender.
/// El interruptor del residente lo cambian Administración y la Enfermería de su unidad (sin el permiso de gestionar citas: es una herramienta de privacidad); Medicina solo lo mira. La
/// autorización no vive aquí: cada caso de uso la comprueba. Los mensajes no repiten nada de las citas.
/// </summary>
public sealed partial class CitasExternasController
{
    private const string FamilyCenterAdminMessage = "Los ajustes de lo que ve la familia los cambia Administración.";

    private const string FamilyCenterInvalidMessage = "Para abrir las citas externas a las familias tienes que marcar las tres confirmaciones.";

    private const string FamilyConflictMessage = "Otra persona ha cambiado esto mientras lo mirabas. Aquí tienes cómo está ahora.";

    private const string FamilyResidentMessage = "Solo Administración y la Enfermería de la unidad de esta persona pueden cambiar lo que ve su familia.";

    [HttpGet]
    public async Task<IActionResult> AjustesFamilia(CancellationToken ct)
    {
        if (Scope() is not { Profile: SystemProfile.Administracion } scope)
        {
            TempData["Error"] = FamilyCenterAdminMessage;
            return RedirectToAction(nameof(Index));
        }

        var state = await familySettings.ReadCenterAsync(scope.ProfileScopeId, CenterId.From(scope.CenterId), ct);
        if (!state.Ok)
        {
            TempData["Error"] = state.Error!.Message;
            return RedirectToAction(nameof(Index));
        }

        return View(new ExternalFamilyCenterViewModel(state.Value!, null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AjustesFamilia(int version, bool encender, bool informada, bool voluntad, bool eipd, CancellationToken ct)
    {
        if (Scope() is not { Profile: SystemProfile.Administracion } scope)
        {
            TempData["Error"] = FamilyCenterAdminMessage;
            return RedirectToAction(nameof(Index));
        }

        var centerId = CenterId.From(scope.CenterId);
        var result = await familySettings.SetCenterAsync(new SetExternalFamilyCenterCommand(scope.ProfileScopeId, centerId, version, encender, informada, voluntad, eipd), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = encender ? "Las familias ya pueden ver las citas externas." : "Las familias ya no ven las citas externas.";
            return RedirectToAction(nameof(AjustesFamilia));
        }

        if (result.Error!.Code == ApplicationFailureCode.InvalidInput)
        {
            var state = await familySettings.ReadCenterAsync(scope.ProfileScopeId, centerId, ct);
            return state.Ok ? View(new ExternalFamilyCenterViewModel(state.Value!, FamilyCenterInvalidMessage)) : RedirectToAction(nameof(Index));
        }

        TempData["Error"] = result.Error.Code == ApplicationFailureCode.Conflict ? FamilyConflictMessage : result.Error.Message;
        return RedirectToAction(nameof(AjustesFamilia));
    }

    [HttpGet]
    public async Task<IActionResult> FamiliaResidente(Guid residenteId, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        var state = await familySettings.ReadResidentAsync(scope.ProfileScopeId, CenterId.From(scope.CenterId), ResidentId.From(residenteId), ct);
        if (!state.Ok)
        {
            TempData["Error"] = state.Error!.Message;
            return RedirectToAction(nameof(Index));
        }

        return View(new ExternalFamilyResidentViewModel(residenteId, state.Value!, scope.Profile != SystemProfile.Medicina, null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FamiliaResidente(Guid residenteId, int version, bool restringir, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        if (scope.Profile == SystemProfile.Medicina)
        {
            TempData["Error"] = FamilyResidentMessage;
            return RedirectToAction(nameof(FamiliaResidente), new { residenteId });
        }

        var result = await familySettings.SetResidentAsync(
            new SetExternalFamilyResidentCommand(scope.ProfileScopeId, CenterId.From(scope.CenterId), ResidentId.From(residenteId), version, restringir), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = restringir ? "La familia ya no ve las citas externas de esta persona." : "La familia vuelve a ver las citas externas de esta persona.";
        }
        else
        {
            TempData["Error"] = result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => FamilyConflictMessage,
                ApplicationFailureCode.AccessDenied => FamilyResidentMessage,
                _ => result.Error.Message,
            };
        }

        return RedirectToAction(nameof(FamiliaResidente), new { residenteId });
    }
}
