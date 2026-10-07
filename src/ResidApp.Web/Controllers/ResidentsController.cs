using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using ResidApp.Application.Errors;
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
    ResidentBaselineApplicationService service, ListActiveScopeUnits listUnits, ListActiveScopeLocations listLocations,
    ListActiveScopePermissions listPermissions) : Controller
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

        try
        {
            _ = ResidApp.Domain.Families.ResidentFamilyAtAdmission.Validate(form.FamilyInputs());
        }
        catch (DomainValidationException)
        {
            ModelState.AddModelError(nameof(form.Familiares),
                "Revisa los familiares: tiene que haber al menos un contacto prioritario, y cada familiar necesita nombre, relación y un teléfono válido (y un correo válido si lo escribes).");
            await ShowActiveScopeAsync(ActiveProfileScopeCookie.Read(Request), ct);
            return View(form);
        }

        if (form.ParseLocation() is not { } location)
        {
            ModelState.AddModelError(nameof(form.Ubicacion), "Elige una ubicación de la lista.");
            await ShowActiveScopeAsync(ActiveProfileScopeCookie.Read(Request), ct);
            return View(form);
        }

        // Edificio y planta no se mandan: el servidor usa los de la unidad.
        var command = new CreateResidentCommand(
            form.AmbitoPerfilId, CenterId.From(form.CentroId), UnitId.From(form.UnidadId!.Value), form.NombreVisible,
            form.FechaNacimiento!.Value, form.SexoDocumentadoCodigo, form.ReferenciaInterna,
            EdificioId: null, PlantaId: null, location.RoomId, location.PlaceId, form.OperacionId, form.FamilyInputs());

        var result = await service.CreateResidentAsync(command, ct);
        if (!result.Ok)
        {
            var chosen = location.RoomId is not null || location.PlaceId is not null;
            ModelState.AddModelError(string.Empty, chosen && result.Error!.Code == ApplicationFailureCode.Conflict
                ? "Esa plaza ya está ocupada por otro residente. Elige otra o deja la ubicación vacía."
                : chosen && result.Error!.Code == ApplicationFailureCode.InvalidInput
                    ? "La habitación o la plaza elegida no está disponible en esa unidad. Elige otra o deja la ubicación vacía."
                    : result.Error!.Message);
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
        var locations = activeScope is null
            ? null
            : await listLocations.ExecuteAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ct);
        ViewBag.Ubicaciones = locations is { Ok: true } ? locations.Value! : (IReadOnlyList<LocationOption>)[];
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
