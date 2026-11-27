using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>El cuadro de mando de Administración («Panel de gestión»): cifras y gráficos calculados al abrirlo. Los avisos siguen en el Inicio.</summary>
public sealed partial class AdministracionController
{
    public async Task<IActionResult> Panel(
        string? periodo, [FromServices] AdministrationDashboardApplicationService dashboard, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Panel), new { periodo }) });
        }

        var scopeId = activeScope.ProfileScopeId;
        var center = CenterId.From(activeScope.CenterId);
        var now = DateTimeOffset.UtcNow;
        var period = DashboardPeriod.At(AdministrationPanelDisplay.Range(periodo), now, CenterTime.Zone);
        // Cada bloque se lee por separado: si uno falla, la pantalla lo dice en su sitio y enseña los demás.
        var occupancy = await dashboard.ReadOccupancyAsync(scopeId, center, period, ct);
        var movements = await dashboard.ReadMovementsAsync(scopeId, center, period, ct);
        var appointments = await dashboard.ReadAppointmentsAsync(scopeId, center, period, ct);
        var authorizations = await dashboard.ReadAuthorizationsAsync(scopeId, center, period, ct);
        var attention = await overview.ReadAsync(scopeId, center, ct);
        var communications = await familyCommunications.ListAsync(scopeId, center, ct);
        if (occupancy.Error is { Code: ApplicationFailureCode.AccessDenied } denied)
        {
            ModelState.AddModelError(string.Empty, denied.Message);
        }

        return View(new AdministrationPanelViewModel(
            period, CenterTime.Now, occupancy.Value, movements.Value, appointments.Value, authorizations.Value, attention.Value,
            communications.Ok ? AdministrationCommunicationsSummary.From(communications.Value!, now) : null,
            ResidApp.Domain.Platform.CurrentCenterService.IsDayCenter));
    }
}
