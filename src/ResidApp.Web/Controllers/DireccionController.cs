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

    /// <summary>DIR-12: estado de las derivaciones, solo lectura. Las de episodios abiertos siempre; las de episodios cerrados, si se pide
    /// y en el periodo elegido (CJ, 2026-10-07). No entrega el informe firmado.</summary>
    public async Task<IActionResult> Derivaciones(SupervisionReferralFilter filtro, CancellationToken ct)
    {
        // Una fecha mal formada en la URL se ignora (toma el valor por defecto), no se muestra como error.
        ModelState.Clear();
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Request.Path + Request.QueryString });
        }

        var query = new SupervisionQuery(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId));
        if (!filtro.Cerradas)
        {
            var open = await service.ListReferralsAsync(query, ct: ct);
            if (!open.Ok)
            {
                ModelState.AddModelError(string.Empty, open.Error!.Message);
                return View(new SupervisionReferralsViewModel([]));
            }

            return View(new SupervisionReferralsViewModel(open.Value!));
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var (from, to) = filtro.Period.Resolve(today);
        if (filtro.Period.Validate(today) is { } invalid)
        {
            ModelState.AddModelError(string.Empty, invalid);
            return View(new SupervisionReferralsViewModel([], true, from, to));
        }

        var result = await service.ListReferralsAsync(query, from, to, ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(new SupervisionReferralsViewModel([], true, from, to));
        }

        return View(new SupervisionReferralsViewModel(result.Value!, true, from, to));
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

    /// <summary>DIR-08 a DIR-10 y DIR-16 (bloque 4): indicadores agregados del periodo, por unidad, en total y mes a mes. La
    /// misma página es el informe de actividad imprimible.</summary>
    public async Task<IActionResult> Indicadores(IndicatorPeriodFilter filtro, CancellationToken ct)
    {
        // Una fecha mal formada en la URL se ignora (toma el valor por defecto), no se muestra como error.
        ModelState.Clear();
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Request.Path + Request.QueryString });
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var (from, to) = filtro.Resolve(today);
        if (filtro.Validate(today) is { } invalid)
        {
            ModelState.AddModelError(string.Empty, invalid);
            return View(new SupervisionIndicatorsViewModel(from, to, null, DateTime.Now));
        }

        var result = await service.ReadIndicatorsAsync(new ReadSupervisionIndicatorsQuery(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), from, to), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        return View(new SupervisionIndicatorsViewModel(from, to, result.Value, DateTime.Now));
    }

    /// <summary>DIR-11: revisión de calidad de proceso del periodo, solo lectura: hitos medidos y fuera de plazo por unidad y la lista de
    /// episodios con algún hito fuera de plazo o a punto de vencer, sin contenido clínico.</summary>
    public async Task<IActionResult> Calidad(IndicatorPeriodFilter filtro, CancellationToken ct)
    {
        // Una fecha mal formada en la URL se ignora (toma el valor por defecto), no se muestra como error.
        ModelState.Clear();
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Request.Path + Request.QueryString });
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var (from, to) = filtro.Resolve(today);
        if (filtro.Validate(today) is { } invalid)
        {
            ModelState.AddModelError(string.Empty, invalid);
            return View(new ProcessQualityViewModel(from, to, null));
        }

        var result = await service.ReadProcessQualityAsync(new ReadSupervisionIndicatorsQuery(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), from, to), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        var scope = await service.ReadScopeAsync(new SupervisionQuery(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        var canManage = scope.Ok && scope.Value!.Permissions.Contains(ResidApp.Domain.Accounts.ProfilePermissions.ProcessDeadlinesManage);
        return View(new ProcessQualityViewModel(from, to, result.Value, canManage));
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
