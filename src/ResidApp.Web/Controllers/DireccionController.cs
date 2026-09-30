using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Vertical Dirección/Coordinación Clínica, bloque 1: DIR-01/DIR-02 (inicio con los contadores por unidad), DIR-03
/// (pendientes filtrables por tipo y unidad), DIR-04 (detalle operativo de un episodio, sin contenido clínico) y DIR-17
/// (mi ámbito de supervisión). Solo lectura: ninguna acción escribe. Traduce a DireccionApplicationService; la
/// autorización no vive aquí. Un acceso denegado y un fallo técnico salen con mensajes distintos (DIR-18), los del
/// resultado de la aplicación.
/// </summary>
public sealed class DireccionController(DireccionApplicationService service) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Index)) });
        }

        var result = await service.ReadPanelAsync(new SupervisionQuery(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(new DireccionInicioViewModel([]));
        }

        return View(new DireccionInicioViewModel(result.Value!));
    }

    public async Task<IActionResult> Pendientes(SupervisionFilter filtro, CancellationToken ct)
    {
        // DIR-03: un valor del filtro mal formado en la URL se ignora (queda sin filtrar), no se muestra como error.
        ModelState.Clear();
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Request.Path + Request.QueryString });
        }

        var centerId = CenterId.From(activeScope.CenterId);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var scope = await service.ReadScopeAsync(new SupervisionQuery(activeScope.ProfileScopeId, centerId), ct);
        var list = await service.ListPendingAsync(new ListSupervisionPendingQuery(
            activeScope.ProfileScopeId, centerId, today, filtro.Tipo, filtro.Unidad), ct);
        if (!scope.Ok || !list.Ok)
        {
            ModelState.AddModelError(string.Empty, (scope.Error ?? list.Error)!.Message);
            return View(new SupervisionPendingViewModel(new SupervisionPendingList([], 0), filtro, [], today));
        }

        return View(new SupervisionPendingViewModel(list.Value!, filtro, scope.Value!.Units.Select(u => (u.Id.Value, u.Name)).ToList(), today));
    }

    /// <summary>DIR-04: detalle operativo. Si no existe, está cerrado o no está en el ámbito, se vuelve a los pendientes
    /// sin distinguir el motivo.</summary>
    public async Task<IActionResult> Episodio(Guid eventoId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Episodio), new { eventoId }) });
        }

        var result = await service.FindEpisodeAsync(
            new FindSupervisionEpisodeQuery(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), eventoId), ct);
        return result.Ok ? View(result.Value) : RedirectToAction(nameof(Pendientes));
    }

    public async Task<IActionResult> Ambito(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Ambito)) });
        }

        var result = await service.ReadScopeAsync(new SupervisionQuery(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View((SupervisionScopeInfo?)null);
        }

        return View(result.Value);
    }
}
