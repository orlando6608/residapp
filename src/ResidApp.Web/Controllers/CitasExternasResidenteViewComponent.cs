using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// El bloque «Citas externas» de la ficha de un residente (script 0101), común a Administración, Enfermería y Medicina: sus próximas citas abiertas y, a quien puede registrar, el botón. Lee con el
/// mismo caso de uso que la lista; si no se puede leer, no dibuja nada (la ficha sigue funcionando sin el bloque).
/// </summary>
public sealed class CitasExternasResidenteViewComponent(
    ExternalAppointmentsApplicationService appointments, ExternalAppointmentFamilySettingsApplicationService familySettings) : ViewComponent
{
    private const int Days = 60;

    public async Task<IViewComponentResult> InvokeAsync(Guid residenteId)
    {
        if (ActiveProfileScopeCookie.Read(Request) is not { } scope
            || scope.Profile is not (SystemProfile.Administracion or SystemProfile.Enfermeria or SystemProfile.Medicina))
        {
            return Content(string.Empty);
        }

        var today = appointments.TodayAtCenter();
        var result = await appointments.ListAsync(scope.ProfileScopeId, CenterId.From(scope.CenterId), today, today.AddDays(Days), ResidentId.From(residenteId));
        if (!result.Ok)
        {
            return Content(string.Empty);
        }

        var open = result.Value!.Appointments.Where(a => ExternalAppointmentRules.IsOpen(a.Status)).ToList();
        var overdue = await appointments.ListOverdueAsync(scope.ProfileScopeId, CenterId.From(scope.CenterId), ResidentId.From(residenteId));
        // Lo que ve la familia de esta persona; si no se puede leer, el bloque sigue sin esa línea.
        var family = await familySettings.ReadResidentAsync(scope.ProfileScopeId, CenterId.From(scope.CenterId), ResidentId.From(residenteId));
        return View(new ExternalAppointmentResidentBlockViewModel(residenteId, open, result.Value.CanManage, overdue.Ok ? overdue.Value! : [], family.Ok ? family.Value : null));
    }
}
