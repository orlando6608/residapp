using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>Turnos y equipos (historia 4, script 0027): catálogo de turnos del centro, equipos de las unidades del ámbito y sus
/// miembros. Planificar no concede acceso a nada. Traduce a AdministracionApplicationService; la autorización no vive aquí.</summary>
public sealed partial class AdministracionController
{
    /// <summary>ADM-14: el catálogo de turnos del centro.</summary>
    public async Task<IActionResult> Turnos(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Turnos)) });
        }

        var result = await service.ListShiftsAsync(Query(activeScope), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        return View(result.Value ?? []);
    }

    public IActionResult NuevoTurno()
    {
        if (ActiveProfileScopeCookie.Read(Request) is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(NuevoTurno)) });
        }

        return View(new NewShiftViewModel(new NewShiftFormModel { OperacionId = Guid.NewGuid() }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuevoTurno([Bind(Prefix = "Form")] NewShiftFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (ModelState.IsValid)
        {
            var result = await service.CreateShiftAsync(new CreateShiftCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.OperacionId, form.Nombre, form.Inicio, form.Fin), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Turno creado. Sus horas no se pueden cambiar después: si hacen falta otras, crea otro turno.";
                return RedirectToAction(nameof(Turnos));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => "Ya existe un turno con ese nombre en el centro.",
                ApplicationFailureCode.InvalidInput => "Revisa los datos: el nombre es obligatorio y hacen falta las dos horas.",
                _ => result.Error.Message,
            });
        }

        return View(new NewShiftViewModel(form));
    }

    public async Task<IActionResult> NombreTurno(Guid turnoId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(NombreTurno), new { turnoId }) });
        }

        var shift = await FindShiftAsync(activeScope, turnoId, ct);
        return shift is null
            ? RedirectToAction(nameof(Turnos))
            : View(new RenameShiftViewModel(shift, new RenameShiftFormModel { TurnoId = turnoId, Nombre = shift.Name }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NombreTurno([Bind(Prefix = "Form")] RenameShiftFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var shift = await FindShiftAsync(activeScope, form.TurnoId, ct);
        if (shift is null)
        {
            return RedirectToAction(nameof(Turnos));
        }

        if (ModelState.IsValid)
        {
            var result = await service.RenameShiftAsync(new RenameShiftCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.TurnoId, form.Nombre), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Nombre guardado.";
                return RedirectToAction(nameof(Turnos));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => "Ya existe otro turno con ese nombre en el centro.",
                ApplicationFailureCode.InvalidInput => "Escribe un nombre distinto del actual.",
                _ => result.Error.Message,
            });
        }

        return View(new RenameShiftViewModel(shift, form));
    }

    /// <summary>ADM-14: Activo es el estado que se quiere (false para inactivar, true para reactivar).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EstadoTurno(Guid turnoId, bool activo, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await service.ChangeShiftStatusAsync(new ChangeShiftStatusCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), turnoId, activo), ct);
        SetFeedback(result, activo ? "Turno reactivado." : "Turno inactivado: ya no se podrá planificar.", "El turno ya estaba en ese estado.");
        return RedirectToAction(nameof(Turnos));
    }

    /// <summary>ADM-14: los equipos de las unidades del ámbito, con sus miembros.</summary>
    public async Task<IActionResult> Equipos(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Equipos)) });
        }

        var result = await service.ListTeamsAsync(Query(activeScope), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        return View(result.Value ?? []);
    }

    public async Task<IActionResult> NuevoEquipo(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(NuevoEquipo)) });
        }

        var units = await AdministratorUnitsAsync(activeScope, ct);
        return View(new NewTeamViewModel(new NewTeamFormModel
        {
            OperacionId = Guid.NewGuid(),
            UnidadId = units.Count == 1 ? units[0].UnitId.Value : null,
        }, units));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuevoEquipo([Bind(Prefix = "Form")] NewTeamFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (ModelState.IsValid)
        {
            var result = await service.CreateTeamAsync(new CreateTeamCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.OperacionId, UnitId.From(form.UnidadId!.Value), form.Nombre), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Equipo creado. Añade a sus miembros desde «Miembros».";
                return RedirectToAction(nameof(Equipos));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => "Ya existe un equipo con ese nombre en la unidad.",
                ApplicationFailureCode.InvalidInput => "Revisa los datos: el nombre es obligatorio y la unidad tiene que ser una activa de tu ámbito.",
                _ => result.Error.Message,
            });
        }

        return View(new NewTeamViewModel(form, await AdministratorUnitsAsync(activeScope, ct)));
    }

    public async Task<IActionResult> NombreEquipo(Guid equipoId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(NombreEquipo), new { equipoId }) });
        }

        var team = await FindTeamAsync(activeScope, equipoId, ct);
        return team is null
            ? RedirectToAction(nameof(Equipos))
            : View(new RenameTeamViewModel(team, new RenameTeamFormModel { EquipoId = equipoId, Nombre = team.Name }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NombreEquipo([Bind(Prefix = "Form")] RenameTeamFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var team = await FindTeamAsync(activeScope, form.EquipoId, ct);
        if (team is null)
        {
            return RedirectToAction(nameof(Equipos));
        }

        if (ModelState.IsValid)
        {
            var result = await service.RenameTeamAsync(new RenameTeamCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EquipoId, form.Nombre), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Nombre guardado.";
                return RedirectToAction(nameof(Equipos));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => "Ya existe otro equipo con ese nombre en la unidad.",
                ApplicationFailureCode.InvalidInput => "Escribe un nombre distinto del actual.",
                _ => result.Error.Message,
            });
        }

        return View(new RenameTeamViewModel(team, form));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EstadoEquipo(Guid equipoId, bool activo, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await service.ChangeTeamStatusAsync(new ChangeTeamStatusCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), equipoId, activo), ct);
        SetFeedback(result, activo ? "Equipo reactivado." : "Equipo inactivado: ya no se podrá planificar.", "El equipo ya estaba en ese estado.");
        return RedirectToAction(nameof(Equipos));
    }

    /// <summary>ADM-14: miembros vigentes del equipo y cuentas que se pueden añadir.</summary>
    public async Task<IActionResult> MiembrosEquipo(Guid equipoId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(MiembrosEquipo), new { equipoId }) });
        }

        var team = await FindTeamAsync(activeScope, equipoId, ct);
        if (team is null)
        {
            return RedirectToAction(nameof(Equipos));
        }

        var eligible = (await service.ListEligibleTeamMembersAsync(Query(activeScope), equipoId, ct)).Value ?? [];
        return View(new TeamMembersViewModel(team, eligible));
    }

    /// <summary>ADM-14: anadir es true para añadir a la cuenta al equipo y false para darla de baja.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MiembroEquipo(Guid equipoId, Guid cuentaId, bool anadir, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await service.ChangeTeamMemberAsync(new ChangeTeamMemberCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), equipoId, AccountId.From(cuentaId), anadir), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = anadir ? "Miembro añadido al equipo." : "Miembro dado de baja del equipo.";
        }
        else if (result.Error!.Code == ApplicationFailureCode.AccessDenied)
        {
            return RedirectToAction(nameof(Equipos));
        }
        else
        {
            TempData["Error"] = result.Error.Code switch
            {
                ApplicationFailureCode.InvalidInput => "No se puede añadir: el equipo tiene que estar activo y la cuenta, activa y con un perfil de Auxiliar, Enfermería o Medicina que tenga la unidad del equipo.",
                ApplicationFailureCode.Conflict => anadir ? "La cuenta ya es miembro del equipo." : "La cuenta ya no era miembro del equipo.",
                _ => result.Error.Message,
            };
        }

        return RedirectToAction(nameof(MiembrosEquipo), new { equipoId });
    }

    private void SetFeedback(ApplicationResult<bool> result, string done, string conflict)
    {
        if (result.Ok)
        {
            TempData["Mensaje"] = done;
        }
        else if (result.Error!.Code != ApplicationFailureCode.AccessDenied)
        {
            TempData["Error"] = result.Error.Code == ApplicationFailureCode.Conflict ? conflict : result.Error.Message;
        }
    }

    private async Task<ShiftInfo?> FindShiftAsync(ActiveProfileScopeCookieValue activeScope, Guid shiftId, CancellationToken ct) =>
        (await service.ListShiftsAsync(Query(activeScope), ct)).Value?.FirstOrDefault(s => s.ShiftId == shiftId);

    private async Task<TeamInfo?> FindTeamAsync(ActiveProfileScopeCookieValue activeScope, Guid teamId, CancellationToken ct) =>
        (await service.ListTeamsAsync(Query(activeScope), ct)).Value?.FirstOrDefault(t => t.TeamId == teamId);
}
