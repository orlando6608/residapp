using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>Hora de publicación y margen de los comunicados a la familia del centro (script 0051; CJ, 2026-10-10): los fija Administración. Solo
/// afectan a los comunicados que se guarden después: la hora de los ya guardados queda fijada.</summary>
public sealed partial class AdministracionController
{
    /// <summary>Con <paramref name="cj"/> el formulario trae el horario de CJ (19:00 y 60 minutos), sin guardarlo.</summary>
    public async Task<IActionResult> HorarioComunicados(bool cj = false, CancellationToken ct = default)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(HorarioComunicados)) });
        }

        var result = await communicationSchedule.ReadAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(new HorarioComunicadosViewModel(null, new HorarioComunicadosFormModel()));
        }

        return View(new HorarioComunicadosViewModel(
            result.Value, HorarioComunicadosFormModel.From(cj ? FamilyCommunicationScheduleSettings.Default : result.Value!.Settings, result.Value!.Version),
            RestoredDefaults: cj));
    }

    /// <summary>Ante un conflicto de versión no se sobrescribe el cambio ajeno: se vuelve a mostrar lo escrito y se pide recargar.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HorarioComunicados([Bind(Prefix = "Form")] HorarioComunicadosFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(HorarioComunicados)) });
        }

        var centerId = CenterId.From(activeScope.CenterId);
        if (ModelState.IsValid)
        {
            var result = await communicationSchedule.SaveAsync(new SaveFamilyCommunicationScheduleCommand(
                activeScope.ProfileScopeId, centerId, form.Version, form.HoraPublicacion, form.MargenMinutos), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Horario de los comunicados guardado. Se aplica a los comunicados que se guarden a partir de ahora.";
                return RedirectToAction(nameof(HorarioComunicados));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => "Otra persona ha cambiado el horario desde que abriste la pantalla. Recarga para ver el vigente; lo que has escrito sigue aquí.",
                ApplicationFailureCode.InvalidInput => "Revisa la hora y el margen: el margen va de 0 a 1440 minutos.",
                ApplicationFailureCode.AccessDenied => "No tienes acceso a esta pantalla.",
                _ => result.Error.Message,
            });
        }

        var view = await communicationSchedule.ReadAsync(activeScope.ProfileScopeId, centerId, ct);
        return View(new HorarioComunicadosViewModel(view.Ok ? view.Value : null, form));
    }
}
