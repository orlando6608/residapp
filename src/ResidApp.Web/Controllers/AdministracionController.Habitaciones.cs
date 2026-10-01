using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>Historia 2 (script 0029), fase 2: habitaciones y plazas de una unidad del ámbito. Crear o cambiar una habitación o una plaza no da
/// acceso a nadie ni cambia dónde está ningún residente. Traduce a AdministracionEstructuraApplicationService; la autorización no vive aquí.</summary>
public sealed partial class AdministracionController
{
    public async Task<IActionResult> Habitaciones(Guid unidadId, CancellationToken ct)
    {
        ModelState.Clear();
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Request.Path + Request.QueryString });
        }

        var unit = await FindStructureUnitAsync(activeScope, unidadId, ct);
        if (unit is null)
        {
            return RedirectToAction(nameof(Estructura));
        }

        var result = await estructura.ListRoomsAsync(new ListRoomsQuery(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), unit.UnitId), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        return View(new RoomsViewModel(unit, result.Value ?? []));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuevaHabitacion([Bind(Prefix = "Form")] NewLayoutFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (form.UnidadId is not { } unitId)
        {
            return RedirectToAction(nameof(Estructura));
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Escribe el nombre de la habitación (hasta 200 caracteres).";
            return RedirectToAction(nameof(Habitaciones), new { unidadId = unitId });
        }

        var result = await estructura.CreateRoomAsync(new CreateRoomCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.OperacionId, UnitId.From(unitId), form.Nombre), ct);
        return AfterRoomChange(unitId, result.Ok, result.Error, "Habitación creada.",
            "No se puede crear la habitación: escribe un nombre (hasta 200 caracteres) y usa una unidad activa.",
            "Ya existe una habitación con ese nombre en la unidad.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuevaPlaza([Bind(Prefix = "Form")] NewLayoutFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (form.UnidadId is not { } unitId)
        {
            return RedirectToAction(nameof(Estructura));
        }

        if (!ModelState.IsValid || form.HabitacionId is null)
        {
            TempData["Error"] = "Escribe el nombre de la plaza (hasta 200 caracteres).";
            return RedirectToAction(nameof(Habitaciones), new { unidadId = unitId });
        }

        var result = await estructura.CreatePlaceAsync(new CreatePlaceCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.OperacionId, form.HabitacionId.Value, form.Nombre), ct);
        return AfterRoomChange(unitId, result.Ok, result.Error, "Plaza creada.",
            "No se puede crear la plaza: escribe un nombre (hasta 200 caracteres) y usa una habitación activa.",
            "Ya existe una plaza con ese nombre en la habitación.");
    }

    public async Task<IActionResult> NombreHabitacion(Guid unidadId, Guid habitacionId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Request.Path + Request.QueryString });
        }

        var room = await FindRoomAsync(activeScope, unidadId, habitacionId, ct);
        return room is null
            ? RedirectToAction(nameof(Habitaciones), new { unidadId })
            : View("NombreEstructura", new RenameLayoutViewModel(
                "habitación", room.Name, new RenameLayoutFormModel { Id = habitacionId, UnidadId = unidadId, Nombre = room.Name }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NombreHabitacion([Bind(Prefix = "Form")] RenameLayoutFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (form.UnidadId is not { } unitId)
        {
            return RedirectToAction(nameof(Estructura));
        }

        var room = await FindRoomAsync(activeScope, unitId, form.Id, ct);
        if (room is null)
        {
            return RedirectToAction(nameof(Habitaciones), new { unidadId = unitId });
        }

        if (ModelState.IsValid)
        {
            var result = await estructura.RenameRoomAsync(new RenameRoomCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.Id, form.Nombre), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Nombre guardado.";
                return RedirectToAction(nameof(Habitaciones), new { unidadId = unitId });
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => "Ya existe otra habitación con ese nombre en la unidad.",
                ApplicationFailureCode.InvalidInput => "Escribe un nombre distinto del actual.",
                _ => result.Error.Message,
            });
        }

        return View("NombreEstructura", new RenameLayoutViewModel("habitación", room.Name, form));
    }

    public async Task<IActionResult> NombrePlaza(Guid unidadId, Guid plazaId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Request.Path + Request.QueryString });
        }

        var place = await FindPlaceAsync(activeScope, unidadId, plazaId, ct);
        return place is null
            ? RedirectToAction(nameof(Habitaciones), new { unidadId })
            : View("NombreEstructura", new RenameLayoutViewModel(
                "plaza", place.Name, new RenameLayoutFormModel { Id = plazaId, UnidadId = unidadId, Nombre = place.Name }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NombrePlaza([Bind(Prefix = "Form")] RenameLayoutFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (form.UnidadId is not { } unitId)
        {
            return RedirectToAction(nameof(Estructura));
        }

        var place = await FindPlaceAsync(activeScope, unitId, form.Id, ct);
        if (place is null)
        {
            return RedirectToAction(nameof(Habitaciones), new { unidadId = unitId });
        }

        if (ModelState.IsValid)
        {
            var result = await estructura.RenamePlaceAsync(new RenamePlaceCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.Id, form.Nombre), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Nombre guardado.";
                return RedirectToAction(nameof(Habitaciones), new { unidadId = unitId });
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => "Ya existe otra plaza con ese nombre en la habitación.",
                ApplicationFailureCode.InvalidInput => "Escribe un nombre distinto del actual.",
                _ => result.Error.Message,
            });
        }

        return View("NombreEstructura", new RenameLayoutViewModel("plaza", place.Name, form));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EstadoHabitacion(Guid unidadId, Guid habitacionId, bool activa, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await estructura.ChangeRoomStatusAsync(new ChangeRoomStatusCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), habitacionId, activa), ct);
        return AfterRoomChange(unidadId, result.Ok, result.Error, activa ? "Habitación reactivada." : "Habitación inactivada.",
            "No se puede inactivar una habitación con residentes ubicados en ella.", "La habitación ya estaba en ese estado.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EstadoPlaza(Guid unidadId, Guid plazaId, bool activa, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await estructura.ChangePlaceStatusAsync(new ChangePlaceStatusCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), plazaId, activa), ct);
        return AfterRoomChange(unidadId, result.Ok, result.Error, activa ? "Plaza reactivada." : "Plaza inactivada.",
            activa ? "No se puede reactivar una plaza de una habitación inactiva." : "No se puede inactivar una plaza con un residente ubicado en ella.",
            "La plaza ya estaba en ese estado.");
    }

    private async Task<LayoutRoom?> FindRoomAsync(ActiveProfileScopeCookieValue activeScope, Guid unidadId, Guid habitacionId, CancellationToken ct) =>
        (await estructura.ListRoomsAsync(new ListRoomsQuery(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), UnitId.From(unidadId)), ct)).Value?.FirstOrDefault(r => r.RoomId == habitacionId);

    private async Task<LayoutPlace?> FindPlaceAsync(ActiveProfileScopeCookieValue activeScope, Guid unidadId, Guid plazaId, CancellationToken ct) =>
        (await estructura.ListRoomsAsync(new ListRoomsQuery(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), UnitId.From(unidadId)), ct)).Value?
            .SelectMany(r => r.Places).FirstOrDefault(p => p.PlaceId == plazaId);

    /// <summary>Tras crear, renombrar o cambiar el estado de una habitación o plaza: el mensaje o el error vuelven con TempData a la lista de la unidad.</summary>
    private IActionResult AfterRoomChange(Guid unidadId, bool ok, ApplicationFailure? error, string done, string invalid, string conflict)
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

        return RedirectToAction(nameof(Habitaciones), new { unidadId });
    }
}
