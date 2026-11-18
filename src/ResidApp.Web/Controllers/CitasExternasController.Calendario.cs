using Microsoft.AspNetCore.Mvc;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;
using ResidApp.Web.Models;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Calendario del personal de las citas externas, mensual y semanal (PR 3; bocetos A y B de CJ). Lee con el mismo caso de uso que la lista «Próximas», así que cada perfil ve las citas de su ámbito
/// (Administración, todo el centro; Enfermería y Medicina, sus unidades). Un mes o un día muy lejanos en la URL se acercan al límite de cinco años. Los filtros de unidad y de «solo las que tienen algo
/// sin cubrir» se aplican aquí, sobre lo que el servicio ya dejó ver.
/// </summary>
public sealed partial class CitasExternasController
{
    private const int CalendarYears = 5;

    [HttpGet]
    public async Task<IActionResult> Mes(int? anio, int? mes, Guid? unidad, bool sinCubrir, CancellationToken ct)
    {
        ModelState.Clear();
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        var today = appointments.TodayAtCenter();
        var firstOfToday = new DateOnly(today.Year, today.Month, 1);
        var first = new DateOnly(Math.Clamp(anio ?? today.Year, today.Year - CalendarYears, today.Year + CalendarYears), Math.Clamp(mes ?? today.Month, 1, 12), 1);
        first = first < firstOfToday.AddYears(-CalendarYears) ? firstOfToday.AddYears(-CalendarYears) : first > firstOfToday.AddYears(CalendarYears) ? firstOfToday.AddYears(CalendarYears) : first;
        var from = ExternalAppointmentCalendarDisplay.MondayOf(first);
        var to = ExternalAppointmentCalendarDisplay.MondayOf(first.AddMonths(1).AddDays(-1)).AddDays(6);
        return await CalendarAsync(scope, "Mes", first, from, to, today, unidad, sinCubrir, ct);
    }

    [HttpGet]
    public async Task<IActionResult> Semana(DateOnly? dia, Guid? unidad, bool sinCubrir, CancellationToken ct)
    {
        ModelState.Clear();
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        var today = appointments.TodayAtCenter();
        var day = dia is { } chosen ? DateOnly.FromDayNumber(Math.Clamp(chosen.DayNumber, today.AddYears(-CalendarYears).DayNumber, today.AddYears(CalendarYears).DayNumber)) : today;
        var monday = ExternalAppointmentCalendarDisplay.MondayOf(day);
        return await CalendarAsync(scope, "Semana", monday, monday, monday.AddDays(6), today, unidad, sinCubrir, ct);
    }

    private async Task<IActionResult> CalendarAsync(
        Security.ActiveProfileScopeCookieValue scope, string mode, DateOnly reference, DateOnly from, DateOnly to, DateOnly today, Guid? unit, bool onlyUncovered, CancellationToken ct)
    {
        var result = await appointments.ListAsync(scope.ProfileScopeId, CenterId.From(scope.CenterId), from, to, null, ct);
        if (!result.Ok)
        {
            TempData["Error"] = result.Error!.Message;
            return RedirectToAction("Index", "Home");
        }

        var all = result.Value!.Appointments;
        var units = all.Where(a => a.UnitId is not null && a.UnitName is not null).GroupBy(a => a.UnitId!.Value.Value)
            .Select(g => new ExternalAppointmentUnitOption(g.Key, g.First().UnitName!)).OrderBy(u => u.Name).ToList();
        if (unit is { } chosenUnit && units.All(u => u.Id != chosenUnit))
        {
            // La unidad elegida sin citas en estas fechas: sigue en el desplegable, para que se vea que la página está filtrada.
            units.Add(new ExternalAppointmentUnitOption(chosenUnit, "La unidad elegida (sin citas en estas fechas)"));
        }

        var items = all
            .Where(a => unit is not { } wanted || (a.UnitId is { } u && u.Value == wanted))
            .Where(a => !onlyUncovered || (ExternalAppointmentRules.IsOpen(a.Status) && a.Appointment.Date >= today && !a.Appointment.Covered))
            .ToList();
        return View(mode, new ExternalAppointmentCalendarViewModel(mode, from, to, reference, today, items, result.Value.CanManage, unit, onlyUncovered, units));
    }
}
