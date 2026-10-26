using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Organization;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>Organigrama y cargos (ADM-07, script 0073, con las respuestas de CJ): cualquier Administración del centro crea y cambia los
/// cargos, pone o quita a las personas que los ocupan y carga la plantilla de CJ cuando el centro aún no tiene ninguno. Ocupar un cargo no
/// concede permisos. Los demás profesionales lo consultan en OrganigramaController. Traduce a AdministracionOrganigramaApplicationService;
/// la autorización no vive aquí.</summary>
public sealed partial class AdministracionController
{
    public async Task<IActionResult> Cargos(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Cargos)) });
        }

        var result = await organigrama.ListAsync(Query(activeScope), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        var positions = result.Value ?? [];
        return View(new OrganigramaViewModel(
            PositionTree.Nest(positions, p => p.PositionId, p => p.SuperiorId, p => p.Name), positions.Count, Manage: true,
            CanLoadTemplate: result.Ok && positions.Count == 0));
    }

    public async Task<IActionResult> NuevoCargo(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(NuevoCargo)) });
        }

        var model = await PositionFormAsync(activeScope, new PositionFormModel { OperacionId = Guid.NewGuid() }, isNew: true, ct);
        return model is null ? RedirectToAction(nameof(Cargos)) : View("CargoForm", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuevoCargo([Bind(Prefix = "Form")] PositionFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (ModelState.IsValid)
        {
            var result = await organigrama.CreateAsync(new CreatePositionCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.OperacionId, form.Nombre, form.UnaPersona, form.SuperiorId,
                form.Nota, form.Unidades.Select(UnitId.From).ToList()), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Cargo creado. Ahora puedes ponerle personas desde la lista de cargos.";
                return RedirectToAction(nameof(Cargos));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        var model = await PositionFormAsync(activeScope, form, isNew: true, ct);
        return model is null ? RedirectToAction(nameof(Cargos)) : View("CargoForm", model);
    }

    public async Task<IActionResult> EditarCargo(Guid cargoId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(EditarCargo), new { cargoId }) });
        }

        var positions = await organigrama.ListAsync(Query(activeScope), ct);
        var units = await organigrama.ListScopeUnitsAsync(Query(activeScope), ct);
        var position = positions.Value?.FirstOrDefault(p => p.PositionId == cargoId);
        if (position is null || !units.Ok)
        {
            return RedirectToAction(nameof(Cargos));
        }

        var form = new PositionFormModel
        {
            CargoId = cargoId, Nombre = position.Name, UnaPersona = position.OnePerson, SuperiorId = position.SuperiorId, Nota = position.Note,
            Unidades = position.Units.Where(u => units.Value!.Any(s => s.UnitId == u.UnitId)).Select(u => u.UnitId.Value).ToList(),
        };
        return View("CargoForm", BuildPositionForm(positions.Value!, units.Value!, form, isNew: false));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditarCargo([Bind(Prefix = "Form")] PositionFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (ModelState.IsValid)
        {
            var result = await organigrama.UpdateAsync(new UpdatePositionCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.CargoId, form.Nombre, form.UnaPersona, form.SuperiorId,
                form.Nota, form.Unidades.Select(UnitId.From).ToList()), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Cargo guardado.";
                return RedirectToAction(nameof(Cargos));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        var model = await PositionFormAsync(activeScope, form, isNew: false, ct);
        return model is null ? RedirectToAction(nameof(Cargos)) : View("CargoForm", model);
    }

    /// <summary>Activo es el estado que se quiere (false para inactivar, true para reactivar).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EstadoCargo(Guid cargoId, bool activo, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await organigrama.ChangeStatusAsync(new ChangePositionStatusCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), cargoId, activo), ct);
        PositionFeedback(result, activo ? "Cargo reactivado." : "Cargo inactivado: ya no sale en el organigrama que ve el personal.");
        return RedirectToAction(nameof(Cargos));
    }

    public async Task<IActionResult> OcuparCargo(Guid cargoId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(OcuparCargo), new { cargoId }) });
        }

        var model = await OccupantFormAsync(activeScope, new PositionOccupantFormModel { CargoId = cargoId }, ct);
        return model is null ? RedirectToAction(nameof(Cargos)) : View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OcuparCargo([Bind(Prefix = "Form")] PositionOccupantFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (ModelState.IsValid)
        {
            var result = await organigrama.AddOccupantAsync(new AddPositionOccupantCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.CargoId,
                form.CuentaId is { } account ? AccountId.From(account) : null, form.Nombre), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Persona puesta en el cargo.";
                return RedirectToAction(nameof(Cargos));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        var model = await OccupantFormAsync(activeScope, form, ct);
        return model is null ? RedirectToAction(nameof(Cargos)) : View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuitarOcupante(Guid cargoId, Guid ocupanteId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await organigrama.RemoveOccupantAsync(new RemovePositionOccupantCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), cargoId, ocupanteId), ct);
        PositionFeedback(result, "Persona quitada del cargo. El organigrama solo guarda lo actual.");
        return RedirectToAction(nameof(Cargos));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CargarPlantilla(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await organigrama.LoadTemplateAsync(new LoadPositionTemplateCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        PositionFeedback(result, "Plantilla cargada: cambia, quita o añade los cargos que haga falta.");
        return RedirectToAction(nameof(Cargos));
    }

    private void PositionFeedback(ApplicationResult<bool> result, string done)
    {
        if (result.Ok)
        {
            TempData["Mensaje"] = done;
        }
        else if (result.Error!.Code != ApplicationFailureCode.AccessDenied)
        {
            TempData["Error"] = result.Error.Message;
        }
    }

    private async Task<PositionFormViewModel?> PositionFormAsync(
        ActiveProfileScopeCookieValue activeScope, PositionFormModel form, bool isNew, CancellationToken ct)
    {
        var positions = await organigrama.ListAsync(Query(activeScope), ct);
        var units = await organigrama.ListScopeUnitsAsync(Query(activeScope), ct);
        return positions.Ok && units.Ok ? BuildPositionForm(positions.Value!, units.Value!, form, isNew) : null;
    }

    /// <summary>Los posibles superiores son los cargos activos que no crearían un ciclo (el propio cargo ni sus subordinados), más el actual.</summary>
    private static PositionFormViewModel BuildPositionForm(
        IReadOnlyList<PositionInfo> positions, IReadOnlyList<ScopeUnit> units, PositionFormModel form, bool isNew)
    {
        var superiors = positions.ToDictionary(p => p.PositionId, p => p.SuperiorId);
        var candidates = positions
            .Where(p => (p.Active || p.PositionId == form.SuperiorId) && !PositionTree.WouldCreateCycle(superiors, form.CargoId, p.PositionId))
            .OrderBy(p => p.Name).ToList();
        var otherUnits = isNew
            ? []
            : positions.FirstOrDefault(p => p.PositionId == form.CargoId)?.Units.Where(u => units.All(s => s.UnitId != u.UnitId)).ToList() ?? [];
        return new PositionFormViewModel(form, isNew, candidates, units, otherUnits);
    }

    private async Task<PositionOccupantViewModel?> OccupantFormAsync(
        ActiveProfileScopeCookieValue activeScope, PositionOccupantFormModel form, CancellationToken ct)
    {
        var positions = await organigrama.ListAsync(Query(activeScope), ct);
        var candidates = await organigrama.ListCandidatesAsync(Query(activeScope), ct);
        var position = positions.Value?.FirstOrDefault(p => p.PositionId == form.CargoId);
        if (position is null || !candidates.Ok)
        {
            return null;
        }

        var occupying = position.Occupants.Where(o => o.AccountId is not null).Select(o => o.AccountId!.Value).ToHashSet();
        return new PositionOccupantViewModel(position, form, candidates.Value!.Where(c => !occupying.Contains(c.AccountId)).ToList());
    }
}
