using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Rangos de referencia de constantes del centro (decisión de 2026-09-28): los fija un perfil clínico con
/// el permiso REFERENCE_RANGES_MANAGE, nunca Administración. Solo alimentan un aviso visual en la valoración
/// de Enfermería. Traduce a ReferenceRangesApplicationService; la autorización no vive aquí.
/// </summary>
public sealed class RangosReferenciaController(ReferenceRangesApplicationService service) : Controller
{
    /// <summary>Con <paramref name="sugeridos"/> el formulario trae los valores propuestos por CJ, sin guardarlos.</summary>
    public async Task<IActionResult> Index(bool sugeridos = false, CancellationToken ct = default)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Index)) });
        }

        var result = await service.ReadAsync(new ReadReferenceRangesCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        if (!result.Ok)
        {
            return View(new RangosReferenciaViewModel(null, new RangosReferenciaFormModel()));
        }
        return View(sugeridos
            ? new RangosReferenciaViewModel(result.Value, RangosReferenciaFormModel.FromSuggested(result.Value!), SugeridosCargados: true)
            : new RangosReferenciaViewModel(result.Value, RangosReferenciaFormModel.From(result.Value!)));
    }

    /// <summary>Ante un conflicto de versión no se sobrescribe el cambio ajeno: se vuelve a mostrar lo
    /// escrito, con la versión antigua, y se pide recargar.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index([Bind(Prefix = "Form")] RangosReferenciaFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Index)) });
        }
        var centroId = CenterId.From(activeScope.CenterId);

        if (ModelState.IsValid)
        {
            var result = await service.SaveAsync(new SaveReferenceRangesCommand(
                activeScope.ProfileScopeId, centroId, form.Version,
                form.Rangos.Select(r => new ReferenceRangeCommandItem(r.Constante, r.Minimo, r.Maximo)).ToList()), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Rangos de referencia guardados.";
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict =>
                    "Otra persona ha cambiado los rangos mientras tenías la pantalla abierta. Recarga para ver los valores actuales; lo que has escrito sigue aquí para que puedas copiarlo.",
                ApplicationFailureCode.InvalidInput =>
                    "Revisa los rangos: cada uno necesita al menos un límite, el mínimo debe ser menor que el máximo y la saturación no puede superar el 100 %.",
                _ => result.Error.Message,
            });
        }

        var current = await service.ReadAsync(new ReadReferenceRangesCommand(activeScope.ProfileScopeId, centroId), ct);
        return View(new RangosReferenciaViewModel(current.Ok ? current.Value : null, form));
    }
}
