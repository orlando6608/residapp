using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>Historia 2 (script 0029): edificios y plantas del centro, y colocar una unidad en un edificio y una planta. Son del centro,
/// no de una unidad. Colocar una unidad no da acceso clínico a nadie. Traduce a AdministracionEstructuraApplicationService; la
/// autorización no vive aquí.</summary>
public sealed partial class AdministracionController
{
    public async Task<IActionResult> Edificios(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Edificios)) });
        }

        var result = await estructura.ListBuildingsAsync(Query(activeScope), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        return View(new BuildingsViewModel(result.Value ?? []));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuevoEdificio([Bind(Prefix = "Form")] NewLayoutFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Escribe el nombre del edificio (hasta 200 caracteres).";
            return RedirectToAction(nameof(Edificios));
        }

        var result = await estructura.CreateBuildingAsync(new CreateBuildingCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.OperacionId, form.Nombre), ct);
        return AfterLayoutChange(result.Ok, result.Error, "Edificio creado.", "Escribe el nombre del edificio (hasta 200 caracteres).",
            "Ya existe un edificio con ese nombre en el centro.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuevaPlanta([Bind(Prefix = "Form")] NewLayoutFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (!ModelState.IsValid || form.EdificioId is null)
        {
            TempData["Error"] = "Escribe el nombre de la planta (hasta 200 caracteres).";
            return RedirectToAction(nameof(Edificios));
        }

        var result = await estructura.CreateFloorAsync(new CreateFloorCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.OperacionId, form.EdificioId.Value, form.Nombre), ct);
        return AfterLayoutChange(result.Ok, result.Error, "Planta creada.",
            "No se puede crear la planta: escribe un nombre (hasta 200 caracteres) y usa un edificio activo.",
            "Ya existe una planta con ese nombre en el edificio.");
    }

    public async Task<IActionResult> NombreEdificio(Guid edificioId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(NombreEdificio), new { edificioId }) });
        }

        var building = (await estructura.ListBuildingsAsync(Query(activeScope), ct)).Value?.FirstOrDefault(b => b.BuildingId == edificioId);
        return building is null
            ? RedirectToAction(nameof(Edificios))
            : View("NombreEstructura", new RenameLayoutViewModel("edificio", building.Name, new RenameLayoutFormModel { Id = edificioId, Nombre = building.Name }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NombreEdificio([Bind(Prefix = "Form")] RenameLayoutFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var building = (await estructura.ListBuildingsAsync(Query(activeScope), ct)).Value?.FirstOrDefault(b => b.BuildingId == form.Id);
        if (building is null)
        {
            return RedirectToAction(nameof(Edificios));
        }

        if (ModelState.IsValid)
        {
            var result = await estructura.RenameBuildingAsync(new RenameBuildingCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.Id, form.Nombre), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Nombre guardado.";
                return RedirectToAction(nameof(Edificios));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => "Ya existe otro edificio con ese nombre en el centro.",
                ApplicationFailureCode.InvalidInput => "Escribe un nombre distinto del actual.",
                _ => result.Error.Message,
            });
        }

        return View("NombreEstructura", new RenameLayoutViewModel("edificio", building.Name, form));
    }

    public async Task<IActionResult> NombrePlanta(Guid plantaId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(NombrePlanta), new { plantaId }) });
        }

        var floor = (await estructura.ListBuildingsAsync(Query(activeScope), ct)).Value?.SelectMany(b => b.Floors).FirstOrDefault(f => f.FloorId == plantaId);
        return floor is null
            ? RedirectToAction(nameof(Edificios))
            : View("NombreEstructura", new RenameLayoutViewModel("planta", floor.Name, new RenameLayoutFormModel { Id = plantaId, Nombre = floor.Name }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NombrePlanta([Bind(Prefix = "Form")] RenameLayoutFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var floor = (await estructura.ListBuildingsAsync(Query(activeScope), ct)).Value?.SelectMany(b => b.Floors).FirstOrDefault(f => f.FloorId == form.Id);
        if (floor is null)
        {
            return RedirectToAction(nameof(Edificios));
        }

        if (ModelState.IsValid)
        {
            var result = await estructura.RenameFloorAsync(new RenameFloorCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.Id, form.Nombre), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Nombre guardado.";
                return RedirectToAction(nameof(Edificios));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => "Ya existe otra planta con ese nombre en el edificio.",
                ApplicationFailureCode.InvalidInput => "Escribe un nombre distinto del actual.",
                _ => result.Error.Message,
            });
        }

        return View("NombreEstructura", new RenameLayoutViewModel("planta", floor.Name, form));
    }

    /// <summary>Activo es el estado que se quiere (false para inactivar, true para reactivar).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EstadoEdificio(Guid edificioId, bool activo, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await estructura.ChangeBuildingStatusAsync(new ChangeBuildingStatusCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), edificioId, activo), ct);
        return AfterLayoutChange(result.Ok, result.Error, activo ? "Edificio reactivado." : "Edificio inactivado.",
            "No se puede inactivar un edificio con plantas o unidades activas.", "El edificio ya estaba en ese estado.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EstadoPlanta(Guid plantaId, bool activa, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await estructura.ChangeFloorStatusAsync(new ChangeFloorStatusCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), plantaId, activa), ct);
        return AfterLayoutChange(result.Ok, result.Error, activa ? "Planta reactivada." : "Planta inactivada.",
            activa ? "No se puede reactivar una planta de un edificio inactivo." : "No se puede inactivar una planta con unidades activas.",
            "La planta ya estaba en ese estado.");
    }

    public async Task<IActionResult> UbicacionUnidad(Guid unidadId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(UbicacionUnidad), new { unidadId }) });
        }

        var unit = await FindStructureUnitAsync(activeScope, unidadId, ct);
        if (unit is null)
        {
            return RedirectToAction(nameof(Estructura));
        }

        var buildings = (await estructura.ListBuildingsAsync(Query(activeScope), ct)).Value ?? [];
        var current = unit.FloorId is { } floor ? $"f:{unit.BuildingId}:{floor}" : unit.BuildingId is { } building ? $"b:{building}" : "";
        return View(new UnitLocationViewModel(unit, buildings, new UnitLocationFormModel { UnidadId = unidadId, Ubicacion = current }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UbicacionUnidad([Bind(Prefix = "Form")] UnitLocationFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var unit = await FindStructureUnitAsync(activeScope, form.UnidadId, ct);
        if (unit is null)
        {
            return RedirectToAction(nameof(Estructura));
        }

        var buildings = (await estructura.ListBuildingsAsync(Query(activeScope), ct)).Value ?? [];
        if (form.Parse() is { } target)
        {
            var result = await estructura.SetUnitLocationAsync(new SetUnitLocationCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), UnitId.From(form.UnidadId), target.BuildingId, target.FloorId), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Ubicación de la unidad guardada.";
                return RedirectToAction(nameof(Estructura));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => "La unidad ya está en esa ubicación.",
                ApplicationFailureCode.InvalidInput => "Elige un edificio y una planta activos.",
                _ => result.Error.Message,
            });
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Elige una ubicación de la lista.");
        }

        return View(new UnitLocationViewModel(unit, buildings, form));
    }

    /// <summary>Tras crear, renombrar o cambiar el estado de un edificio o planta: el mensaje o el error vuelven con TempData a la lista.</summary>
    private IActionResult AfterLayoutChange(bool ok, ApplicationFailure? error, string done, string invalid, string conflict)
    {
        if (ok)
        {
            TempData["Mensaje"] = done;
        }
        else if (error!.Code != ApplicationFailureCode.AccessDenied)
        {
            TempData["Error"] = error.Code switch
            {
                ApplicationFailureCode.InvalidInput => invalid,
                ApplicationFailureCode.Conflict => conflict,
                _ => error.Message,
            };
        }

        return RedirectToAction(nameof(Edificios));
    }
}
