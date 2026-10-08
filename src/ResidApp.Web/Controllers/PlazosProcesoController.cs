using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Plazos de los hitos del proceso del centro (CJ, 2026-10-07; DIR-11): los fija Dirección / Coordinación Clínica con el permiso
/// PROCESS_DEADLINES_MANAGE. Solo sirven para medir los hitos y avisar al equipo responsable: no cambian ninguna categoría ni umbral clínico.
/// Traduce a ProcessDeadlinesApplicationService; la autorización no vive aquí.
/// </summary>
public sealed class PlazosProcesoController(ProcessDeadlinesApplicationService service) : Controller
{
    /// <summary>Con <paramref name="cj"/> el formulario trae los plazos de CJ, sin guardarlos.</summary>
    public async Task<IActionResult> Index(bool cj = false, CancellationToken ct = default)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Index)) });
        }

        var result = await service.ReadAsync(new ReadProcessDeadlinesCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        if (!result.Ok)
        {
            return View(new PlazosProcesoViewModel(null, new PlazosProcesoFormModel()));
        }

        return View(cj
            ? new PlazosProcesoViewModel(result.Value, PlazosProcesoFormModel.Defaults(result.Value!.Version), RestoredDefaults: true)
            : new PlazosProcesoViewModel(result.Value, PlazosProcesoFormModel.From(result.Value!)));
    }

    /// <summary>Ante un conflicto de versión no se sobrescribe el cambio ajeno: se vuelve a mostrar lo escrito, con la versión antigua, y se
    /// pide recargar.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index([Bind(Prefix = "Form")] PlazosProcesoFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Index)) });
        }

        var centerId = CenterId.From(activeScope.CenterId);
        if (ModelState.IsValid)
        {
            var result = await service.SaveAsync(new SaveProcessDeadlinesCommand(
                activeScope.ProfileScopeId, centerId, form.Version,
                form.Plazos.Select(p => new ProcessDeadlineCommandItem(p.Hito, p.PlazoMinutos, p.PlazoPrioritarioMinutos)).ToList()), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Plazos de los hitos guardados.";
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict =>
                    "Otra persona ha cambiado los plazos mientras tenías la pantalla abierta. Recarga para ver los valores actuales; lo que has escrito sigue aquí para que puedas copiarlo.",
                ApplicationFailureCode.InvalidInput => "Revisa los plazos: van de 1 minuto a 10080 (una semana), o vacíos si el hito no se mide.",
                _ => result.Error.Message,
            });
        }

        var current = await service.ReadAsync(new ReadProcessDeadlinesCommand(activeScope.ProfileScopeId, centerId), ct);
        return View(new PlazosProcesoViewModel(current.Ok ? current.Value : null, form));
    }
}
