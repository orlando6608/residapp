using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>Primera pantalla real del vertical Residente/Basal: alta de residente. Traduce el formulario
/// directamente a CreateResidentCommand; la autorización y las reglas de negocio viven en
/// ResidentBaselineApplicationService, no aquí.</summary>
public sealed class ResidentsController(
    ResidentBaselineApplicationService service, ListActiveScopeUnits listUnits, ListActiveScopePermissions listPermissions) : Controller
{
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Create)) });
        }

        var units = await ShowActiveScopeAsync(activeScope, ct);
        return View(new CreateResidentFormModel
        {
            OperacionId = Guid.NewGuid(),
            AmbitoPerfilId = activeScope.ProfileScopeId,
            CentroId = activeScope.CenterId,
            // Con una sola unidad no hay nada que elegir.
            UnidadId = units.Count == 1 ? units[0].UnitId.Value : null,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateResidentFormModel form, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await ShowActiveScopeAsync(ActiveProfileScopeCookie.Read(Request), ct);
            return View(form);
        }

        var command = new CreateResidentCommand(
            form.AmbitoPerfilId, CenterId.From(form.CentroId), UnitId.From(form.UnidadId!.Value), form.NombreVisible,
            form.FechaNacimiento!.Value, form.SexoDocumentadoCodigo, form.ReferenciaInterna,
            EdificioId: null, PlantaId: null, HabitacionId: null, PlazaId: null, form.OperacionId);

        var result = await service.CreateResidentAsync(command, ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            await ShowActiveScopeAsync(ActiveProfileScopeCookie.Read(Request), ct);
            return View(form);
        }

        TempData["ResidentId"] = result.Value!.ResidentId.Value.ToString();
        return RedirectToAction(nameof(Confirmation));
    }

    public IActionResult Confirmation()
    {
        ViewBag.ResidentId = TempData["ResidentId"];
        return View();
    }

    private async Task<IReadOnlyList<ScopeUnit>> ShowActiveScopeAsync(ActiveProfileScopeCookieValue? activeScope, CancellationToken ct)
    {
        ViewBag.AmbitoCentroNombre = activeScope?.CenterName;
        ViewBag.AmbitoPerfilLabel = activeScope is null ? null : SystemProfileDisplay.Label(activeScope.Profile);
        var result = activeScope is null
            ? null
            : await listUnits.ExecuteAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ct);
        IReadOnlyList<ScopeUnit> units = result is { Ok: true } ? result.Value! : [];
        ViewBag.Unidades = units.Select(u => new SelectListItem(u.Name, u.UnitId.Value.ToString())).ToList();
        // Misma regla que ResidentBaselinePolicy: Administración siempre; Enfermería solo con el permiso. Solo orienta:
        // el alta vuelve a autorizarse al guardarla.
        ViewBag.PuedeDarDeAlta = activeScope?.Profile == SystemProfile.Administracion
            || (activeScope?.Profile == SystemProfile.Enfermeria
                && await listPermissions.ExecuteAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ct)
                    is { Ok: true } permissions
                && permissions.Value!.Contains(ProfilePermissions.ResidentIdentityCreate));
        return units;
    }
}
