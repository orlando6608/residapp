using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Vertical Dirección/Coordinación Clínica, bloque 1: DIR-01/DIR-02 (inicio con los contadores por unidad), DIR-03
/// (pendientes filtrables por tipo y unidad), DIR-04 (detalle operativo de un episodio, sin contenido clínico) y DIR-17
/// (mi ámbito de supervisión). Supervisión en solo lectura, salvo publicar un comunicado a la familia antes de su hora (CJ, 2026-10-10). Traduce a
/// DireccionApplicationService; la autorización no vive aquí. Un acceso denegado y un fallo técnico salen con mensajes distintos (DIR-18), los
/// del resultado de la aplicación.
/// </summary>
public sealed class DireccionController(
    DireccionApplicationService service, FamilyCommunicationPublicationApplicationService familyCommunications) : Controller
{
    /// <summary>Comunicados a la familia de los residentes del ámbito de Dirección, con su hora de publicación; Dirección Clínica puede publicar uno
    /// antes de su hora (CJ, 2026-10-10; 5.3). Se audita con su perfil.</summary>
    public async Task<IActionResult> Comunicados(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Comunicados)) });
        }

        var result = await familyCommunications.ListAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        return View(result.Value ?? []);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PublicarComunicado(Guid comunicadoId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await familyCommunications.PublishNowAsync(
            new PublishFamilyCommunicationCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), comunicadoId), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Comunicado publicado antes de su hora. Queda en la auditoría.";
        }
        else
        {
            TempData["Error"] = result.Error!.Code switch
            {
                ApplicationFailureCode.AccessDenied => "No se puede acceder a ese comunicado.",
                ApplicationFailureCode.Conflict => "Ese comunicado ya estaba publicado.",
                _ => result.Error.Message,
            };
        }

        return RedirectToAction(nameof(Comunicados));
    }

    [HttpGet]
    public async Task<IActionResult> PublicarCorreccion(Guid comunicadoId, CancellationToken ct)
    {
        var item = await FindFamilyCommunicationAsync(comunicadoId, ct);
        if (item is null || !item.CanCorrect)
        {
            TempData["Error"] = "Ese comunicado no admite una corrección: no está publicado, está retirado, la familia ya no lo ve o no es de tu ámbito.";
            return RedirectToAction(nameof(Comunicados));
        }

        return View("PublicarCorreccion", new PublicarCorreccionViewModel(item, new PublicarCorreccionFormModel { ComunicadoId = comunicadoId }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PublicarCorreccion([Bind(Prefix = "Form")] PublicarCorreccionFormModel form, CancellationToken ct)
    {
        var item = await FindFamilyCommunicationAsync(form.ComunicadoId, ct);
        if (item is null)
        {
            return RedirectToAction(nameof(Comunicados));
        }
        if (!ModelState.IsValid)
        {
            return View("PublicarCorreccion", new PublicarCorreccionViewModel(item, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await familyCommunications.PublishCorrectiveAsync(new PublishFamilyCommunicationCorrectiveCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.ComunicadoId, form.Tipo, form.Texto), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Corrección publicada. La familia la ve junto al comunicado original. Queda en la auditoría.";
            return RedirectToAction(nameof(Comunicados));
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.AccessDenied => "No se puede acceder a ese comunicado.",
            ApplicationFailureCode.Conflict => "No se ha publicado: el comunicado aún no está publicado, se ha retirado o la familia ya no lo ve. Vuelve a la lista para ver su estado.",
            ApplicationFailureCode.InvalidInput => "Revisa los datos: elige el tipo y escribe un texto de hasta 2000 caracteres.",
            _ => result.Error.Message,
        });
        return View("PublicarCorreccion", new PublicarCorreccionViewModel(item, form));
    }

    [HttpGet]
    public async Task<IActionResult> RetirarComunicado(Guid comunicadoId, CancellationToken ct)
    {
        var item = await FindFamilyCommunicationAsync(comunicadoId, ct);
        if (item is null || !item.CanAmend)
        {
            TempData["Error"] = "Ese comunicado no se puede retirar: no está publicado, ya está retirado o no es de tu ámbito.";
            return RedirectToAction(nameof(Comunicados));
        }

        return View("RetirarComunicado", new RetirarComunicadoViewModel(item, new RetirarComunicadoFormModel { ComunicadoId = comunicadoId }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetirarComunicado([Bind(Prefix = "Form")] RetirarComunicadoFormModel form, CancellationToken ct)
    {
        var item = await FindFamilyCommunicationAsync(form.ComunicadoId, ct);
        if (item is null)
        {
            return RedirectToAction(nameof(Comunicados));
        }
        if (!ModelState.IsValid)
        {
            return View("RetirarComunicado", new RetirarComunicadoViewModel(item, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await familyCommunications.WithdrawAsync(new WithdrawFamilyCommunicationCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.ComunicadoId, form.Motivo), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Comunicado retirado. La familia ve que ya no está disponible. Queda en la auditoría con tu motivo.";
            return RedirectToAction(nameof(Comunicados));
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.AccessDenied => "No se puede acceder a ese comunicado.",
            ApplicationFailureCode.Conflict => "No se ha retirado: el comunicado aún no está publicado o ya estaba retirado. Vuelve a la lista para ver su estado.",
            ApplicationFailureCode.InvalidInput => "Revisa los datos: escribe el motivo de la retirada, de hasta 500 caracteres.",
            _ => result.Error.Message,
        });
        return View("RetirarComunicado", new RetirarComunicadoViewModel(item, form));
    }

    private async Task<FamilyCommunicationStatusView?> FindFamilyCommunicationAsync(Guid communicationId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return null;
        }

        var result = await familyCommunications.ListAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ct);
        return result.Ok ? result.Value!.FirstOrDefault(c => c.Communication.Id == communicationId) : null;
    }

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

        // Las tarjetas de Indicadores y Calidad solo se enseñan con el permiso; solo orienta, cada pantalla vuelve a autorizar.
        var scope = await service.ReadScopeAsync(new SupervisionQuery(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        var canReviewQuality = scope.Ok && scope.Value!.Permissions.Contains(ResidApp.Domain.Accounts.ProfilePermissions.QualityReview);
        return View(new DireccionInicioViewModel(result.Value!, canReviewQuality));
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

    /// <summary>Cierre excepcional de un episodio con indicaciones sin cumplir (CJ, tercera 5.1; script 0075): solo Dirección Clínica, con motivo y confirmación. Las
    /// indicaciones no se tocan. Queda auditado con el nombre de quien cierra.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CerrarEpisodio([Bind(Prefix = "Form")] CerrarEpisodioExcepcionalFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (!ModelState.IsValid || !form.Confirmo)
        {
            TempData["Error"] = form.Confirmo
                ? "Escribe el motivo del cierre (hasta " + ResidApp.Domain.Supervision.ExceptionalEpisodeClosure.MaxReasonLength + " caracteres)."
                : "Marca la casilla para confirmar que las indicaciones se quedan sin resolver y escribe el motivo.";
            return RedirectToAction(nameof(Episodio), new { eventoId = form.EventoId });
        }

        var result = await service.CloseEpisodeExceptionallyAsync(
            new CloseEpisodeExceptionallyCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Motivo), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Episodio cerrado. Las indicaciones médicas se quedan sin resolver y el cierre queda en la auditoría con tu motivo.";
            return RedirectToAction(nameof(Pendientes));
        }

        TempData["Error"] = result.Error!.Code switch
        {
            ApplicationFailureCode.AccessDenied => "No se puede cerrar ese episodio: no está abierto o no es de tu ámbito.",
            ApplicationFailureCode.Conflict => "Este episodio no tiene indicaciones médicas abiertas, o cambió mientras lo mirabas: vuelve a abrirlo.",
            ApplicationFailureCode.InvalidInput => "Revisa el motivo: es obligatorio y admite hasta " + ResidApp.Domain.Supervision.ExceptionalEpisodeClosure.MaxReasonLength + " caracteres.",
            _ => result.Error.Message,
        };
        return RedirectToAction(nameof(Episodio), new { eventoId = form.EventoId });
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
        if (QualityReviewDenied(result.Error))
        {
            return RedirectToAction(nameof(Index));
        }

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
        if (QualityReviewDenied(result.Error))
        {
            return RedirectToAction(nameof(Index));
        }

        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        var scope = await service.ReadScopeAsync(new SupervisionQuery(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        var canManage = scope.Ok && scope.Value!.Permissions.Contains(ResidApp.Domain.Accounts.ProfilePermissions.ProcessDeadlinesManage);
        return View(new ProcessQualityViewModel(from, to, result.Value, canManage));
    }

    /// <summary>Sin el permiso QUALITY_REVIEW (o fuera de su ámbito) Indicadores y Calidad no se abren: se vuelve al inicio con el motivo.</summary>
    private bool QualityReviewDenied(ApplicationFailure? error)
    {
        if (error?.Code != ApplicationFailureCode.AccessDenied)
        {
            return false;
        }

        TempData["Error"] = "No puedes abrir esa pantalla: hace falta el permiso para ver los indicadores y la calidad de proceso, que concede Administración.";
        return true;
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
