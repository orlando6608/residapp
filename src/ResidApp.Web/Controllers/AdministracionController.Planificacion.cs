using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Scheduling;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>Planificación puntual de turnos (historia 4, script 0027, fase 2): ver dos semanas, planificar un equipo en un turno para
/// varias fechas con aviso de solapamientos que Administración decide, y retirar. Planificar no concede acceso a nada.</summary>
public sealed partial class AdministracionController
{
    private const int ScheduleWindowDays = 14;

    /// <summary>ADM-14: la planificación de dos semanas desde una fecha (por defecto hoy). Un valor mal formado en la URL se ignora.</summary>
    public async Task<IActionResult> Planificacion(DateOnly? desde, Guid? unidad, CancellationToken ct)
    {
        ModelState.Clear();
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Request.Path + Request.QueryString });
        }

        var from = desde ?? DateOnly.FromDateTime(DateTime.Today);
        var to = from.AddDays(ScheduleWindowDays - 1);
        var units = await AdministratorUnitsAsync(activeScope, ct);
        var unit = unidad is { } id && units.Any(u => u.UnitId.Value == id) ? UnitId.From(id) : (UnitId?)null;
        var result = await turnos.ListScheduleAsync(new ListScheduleQuery(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), from, to, unit), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        return View(new ScheduleViewModel(from, to, unit, result.Value ?? [], units));
    }

    public async Task<IActionResult> PlanificarTurno(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(PlanificarTurno)) });
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        return View(await PlanViewAsync(activeScope, new PlanFormModel
        {
            OperacionId = Guid.NewGuid(),
            Desde = today,
            Hasta = today.AddDays(6),
            Dias = ScheduleConflictDisplay.WeekOrder.ToList(),
        }, null, ct));
    }

    /// <summary>ADM-15/17: sin solapamientos planifica directamente; con solapamientos los enseña y pide una justificación (se
    /// confirma volviendo a enviar el mismo formulario con ella). Si los solapamientos cambian entre una vista y otra, no se confirma.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlanificarTurno([Bind(Prefix = "Form")] PlanFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (!ModelState.IsValid)
        {
            return View(await PlanViewAsync(activeScope, form, null, ct));
        }

        var command = new PlanShiftCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.OperacionId, form.EquipoId!.Value, form.TurnoId!.Value,
            form.Dates(), form.Justificacion);
        var preview = await turnos.PreviewScheduleAsync(command, ct);
        if (!preview.Ok)
        {
            ModelState.AddModelError(string.Empty, PlanError(preview.Error!));
            return View(await PlanViewAsync(activeScope, form, null, ct));
        }

        if (preview.Value!.ToCreate.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Todas esas fechas ya tienen planificado ese equipo en ese turno.");
            return View(await PlanViewAsync(activeScope, form, preview.Value, ct));
        }

        var conflicts = preview.Value.Conflicts;
        if (conflicts.Count > 0)
        {
            var fingerprint = ScheduleConflictDisplay.Fingerprint(conflicts);
            var confirming = !string.IsNullOrWhiteSpace(form.ConflictosVistos);
            if (confirming && form.ConflictosVistos != fingerprint)
            {
                ModelState.AddModelError(string.Empty, "Los solapamientos han cambiado desde que los viste. Revísalos de nuevo antes de confirmar.");
            }
            else if (confirming && string.IsNullOrWhiteSpace(form.Justificacion))
            {
                ModelState.AddModelError("Form.Justificacion", "Escribe por qué sigues adelante a pesar de los solapamientos.");
            }

            if (!confirming || form.ConflictosVistos != fingerprint || string.IsNullOrWhiteSpace(form.Justificacion))
            {
                ModelState.Remove("Form.ConflictosVistos");
                form.ConflictosVistos = fingerprint;
                return View(await PlanViewAsync(activeScope, form, preview.Value, ct));
            }
        }

        var result = await turnos.PlanShiftAsync(command, ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, PlanError(result.Error!));
            return View(await PlanViewAsync(activeScope, form, preview.Value, ct));
        }

        var created = result.Value!.Created;
        TempData["Mensaje"] = $"Planificado en {created.Count} fecha(s)."
            + (result.Value.Skipped.Count > 0 ? $" {result.Value.Skipped.Count} ya estaban planificadas y se han omitido." : "")
            + (conflicts.Count > 0 ? " Queda anotada tu justificación del solapamiento." : "");
        return RedirectToAction(nameof(Planificacion), new { desde = created.Min().ToString("yyyy-MM-dd") });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetirarPlanificacion(Guid planificacionId, DateOnly? desde, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await turnos.RetireScheduleAsync(new RetireScheduleCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), planificacionId), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Planificación retirada.";
        }
        else if (result.Error!.Code != ApplicationFailureCode.AccessDenied)
        {
            TempData["Error"] = result.Error.Code switch
            {
                ApplicationFailureCode.InvalidInput => "Solo se pueden retirar las fechas de hoy en adelante.",
                ApplicationFailureCode.Conflict => "Esa planificación ya estaba retirada.",
                _ => result.Error.Message,
            };
        }

        return RedirectToAction(nameof(Planificacion), new { desde = desde?.ToString("yyyy-MM-dd") });
    }

    private async Task<PlanViewModel> PlanViewAsync(
        ActiveProfileScopeCookieValue activeScope, PlanFormModel form, SchedulePreview? preview, CancellationToken ct)
    {
        var teamsResult = await turnos.ListTeamsAsync(Query(activeScope), ct);
        var shiftsResult = await turnos.ListShiftsAsync(Query(activeScope), ct);
        if (!teamsResult.Ok && !ModelState.ContainsKey(string.Empty))
        {
            ModelState.AddModelError(string.Empty, teamsResult.Error!.Message);
        }

        var teams = teamsResult.Value?.Where(t => t.Active).ToList() ?? [];
        var shifts = shiftsResult.Value?.Where(s => s.Active).ToList() ?? [];
        return new PlanViewModel(form, teams, shifts, preview);
    }

    private static string PlanError(ApplicationFailure error) => error.Code switch
    {
        ApplicationFailureCode.InvalidInput =>
            "Revisa los datos: hacen falta de 1 a 62 fechas distintas entre hoy y un año vista, un equipo y un turno activos, y una justificación de hasta 500 caracteres.",
        ApplicationFailureCode.Conflict => "Los solapamientos han cambiado. Revísalos de nuevo antes de confirmar.",
        _ => error.Message,
    };
}
