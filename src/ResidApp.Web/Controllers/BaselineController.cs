using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Cableado de los otros dos casos de uso ya construidos del vertical Residente/Basal: firma de basal y
/// lectura auditada para Dirección Clínica. La firma ya no tiene pantalla propia: se firma desde
/// EnfermeriaBasal/Confirmar (Enfermería y Medicina), que envía aquí el borrador en campos ocultos; un fallo
/// vuelve a esa confirmación con el mensaje.
/// </summary>
public sealed class BaselineController(ResidentBaselineApplicationService service, DireccionApplicationService direccionService) : Controller
{
    public IActionResult Sign() => BackToResidents();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sign(SignBaselineFormModel form, CancellationToken ct)
    {
        // Los campos los rellena la confirmación: si faltan, la petición no viene de ella.
        if (!ModelState.IsValid)
        {
            return BackToResidents();
        }

        var command = new SignBaselineCommand(
            form.AmbitoPerfilId!.Value, CenterId.From(form.CentroId!.Value), ResidentId.From(form.ResidenteId!.Value),
            BaselineDraftId.From(form.BorradorId!.Value), form.RevisionBorradorEsperada, form.OperacionId);

        var result = await service.SignBaselineAsync(command, ct);
        if (!result.Ok)
        {
            TempData["Error"] = result.Error!.Message;
            return RedirectToAction("Confirmar", "EnfermeriaBasal", new { residenteId = form.ResidenteId });
        }

        ViewBag.VersionNumber = result.Value!.VersionNumber;
        ViewBag.BaselineVersionId = result.Value.BaselineVersionId.Value;
        ViewBag.ResidenteId = form.ResidenteId;
        return View("Signed");
    }

    public async Task<IActionResult> Direction(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Direction)) });
        }

        await ShowResidentsAsync(activeScope, ct);
        return View(new DirectionBaselineQueryModel { OperacionId = Guid.NewGuid() });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Direction(DirectionBaselineQueryModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Direction)) });
        }

        if (!ModelState.IsValid)
        {
            await ShowResidentsAsync(activeScope, ct);
            return View(form);
        }

        var command = new ReadDirectionBaselineCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(form.ResidenteId!.Value),
            form.TipoRecurso, form.Proposito, form.OperacionId);

        var result = await service.ReadDirectionBaselineAsync(command, ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            await ShowResidentsAsync(activeScope, ct);
            return View(form);
        }

        ViewBag.Headers = result.Value;
        return View("DirectionResult", form);
    }

    private async Task ShowResidentsAsync(ActiveProfileScopeCookieValue activeScope, CancellationToken ct)
    {
        ViewBag.AmbitoCentroNombre = activeScope.CenterName;
        ViewBag.AmbitoPerfilLabel = SystemProfileDisplay.Label(activeScope.Profile);
        var result = await direccionService.ListResidentsAsync(
            new SupervisionQuery(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }
        ViewBag.Residentes = result.Value ?? [];
    }

    private IActionResult BackToResidents() =>
        RedirectToAction("Residentes", BaselineModuleDisplay.ProfileController(Request));
}
