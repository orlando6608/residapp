using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Vertical Enfermería, grupo E1 (navegación base): ENF-01 (inicio, con contadores de las bandejas ya
/// construidas), ENF-17 (residentes del ámbito) y ENF-18 (ficha del residente, con el mismo resumen de
/// basal vigente que AUX-03); grupo E3: ENF-16 (registrar evento propio); grupo E4: ENF-02/ENF-03/ENF-04
/// (bandejas de ordinarios/prioritarios con los cambios de Auxiliar y los eventos propios, y su detalle);
/// grupo E5: ENF-03 a ENF-05 (empezar y guardar la valoración); historia 3: ENF-06/ENF-07A (decisión
/// asistencial y cierre) con la comunicación familiar pendiente de aprobación (ENF-14/ENF-15); historia 4:
/// ENF-07B a ENF-09 (seguimiento, su bandeja y la transferencia de turno). Traduce a EnfermeriaApplicationService; la
/// autorización y las reglas de negocio no viven aquí.
/// </summary>
public sealed class EnfermeriaController(EnfermeriaApplicationService service) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Index)) });
        }

        var centroId = CenterId.From(activeScope.CenterId);
        var ordinarios = await service.ListPendingChangesAsync(
            new ListPendingChangesCommand(activeScope.ProfileScopeId, centroId, DailyChangeClassification.Ordinario), ct);
        var prioritarios = await service.ListPendingChangesAsync(
            new ListPendingChangesCommand(activeScope.ProfileScopeId, centroId, DailyChangeClassification.Prioritario), ct);
        var comunicaciones = await service.ListPendingFamilyCommunicationsAsync(
            new ListPendingFamilyCommunicationsCommand(activeScope.ProfileScopeId, centroId), ct);
        var seguimientos = await service.ListFollowUpsAsync(new ListFollowUpsCommand(activeScope.ProfileScopeId, centroId), ct);
        return View(new EnfermeriaInicioViewModel(
            ordinarios.Ok ? ordinarios.Value!.Count : 0, prioritarios.Ok ? prioritarios.Value!.Count : 0,
            seguimientos.Ok ? seguimientos.Value!.Count : 0,
            seguimientos.Ok ? seguimientos.Value!.Count(s => FollowUpDisplay.IsOverdue(s.DueDate)) : 0,
            comunicaciones.Ok ? comunicaciones.Value!.Count : 0));
    }

    /// <summary>ENF-08: bandeja compartida de seguimientos abiertos, vencidos incluidos.</summary>
    public async Task<IActionResult> Seguimientos(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Seguimientos)) });
        }

        var result = await service.ListFollowUpsAsync(
            new ListFollowUpsCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(Array.Empty<FollowUpSummary>());
        }

        return View(result.Value);
    }

    /// <summary>Comunicaciones familiares preparadas al cerrar un evento y pendientes de aprobación. Su
    /// aprobación y publicación llegan con el Portal Familiar.</summary>
    public async Task<IActionResult> Comunicaciones(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Comunicaciones)) });
        }

        var result = await service.ListPendingFamilyCommunicationsAsync(
            new ListPendingFamilyCommunicationsCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(Array.Empty<PendingFamilyCommunicationSummary>());
        }

        return View(result.Value);
    }

    /// <summary>ENF-02: bandeja de cambios ordinarios (AUX-11A) y eventos propios ordinarios (ENF-16).</summary>
    public async Task<IActionResult> Ordinarios(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Ordinarios)) });
        }

        var result = await service.ListPendingChangesAsync(new ListPendingChangesCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), DailyChangeClassification.Ordinario), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(Array.Empty<PendingChangeSummary>());
        }

        return View(result.Value);
    }

    /// <summary>ENF-03: bandeja prioritaria (AUX-11B/AUX-12). Mismo alcance E4 que Ordinarios.</summary>
    public async Task<IActionResult> Prioritarios(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Prioritarios)) });
        }

        var result = await service.ListPendingChangesAsync(new ListPendingChangesCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), DailyChangeClassification.Prioritario), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(Array.Empty<PendingChangeSummary>());
        }

        return View(result.Value);
    }

    /// <summary>ENF-04: detalle de un evento recibido (cambio de Auxiliar o evento propio), con el basal
    /// vigente resumido del residente y el estado de su valoración. La línea temporal queda pendiente.</summary>
    public async Task<IActionResult> DetalleCambio(Guid eventoId, CancellationToken ct)
    {
        if (eventoId == Guid.Empty)
        {
            return RedirectToAction(nameof(Index));
        }
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(DetalleCambio), new { eventoId }) });
        }

        var centroId = CenterId.From(activeScope.CenterId);
        var detailResult = await service.FindPendingChangeDetailAsync(
            new FindPendingChangeDetailCommand(activeScope.ProfileScopeId, centroId, eventoId), ct);
        if (!detailResult.Ok || detailResult.Value is null)
        {
            return RedirectToAction(nameof(Index));
        }

        var baselineResult = await service.ReadCurrentBaselineAsync(
            new ReadCurrentBaselineCommand(activeScope.ProfileScopeId, centroId, detailResult.Value.ResidentId), ct);
        return View(new EnfermeriaChangeDetailViewModel(detailResult.Value, baselineResult.Ok ? baselineResult.Value : null));
    }

    /// <summary>ENF-03 "empezar valoración": registra profesional y hora en servidor. Si el evento cambió
    /// desde que se abrió el detalle, no se empieza y se vuelve al detalle ya recargado.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EmpezarValoracion(Guid eventoId, int revision, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null || eventoId == Guid.Empty)
        {
            return RedirectToAction(nameof(Index));
        }

        var result = await service.StartNursingAssessmentAsync(
            new StartNursingAssessmentCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), eventoId, revision), ct);
        if (result.Ok)
        {
            return RedirectToAction(nameof(Valoracion), new { eventoId });
        }

        TempData["Error"] = result.Error!.Code == ApplicationFailureCode.Conflict ? ConcurrencyMessage : result.Error.Message;
        return RedirectToAction(nameof(DetalleCambio), new { eventoId });
    }

    /// <summary>ENF-05: formulario de valoración, solo sobre un evento cuya valoración ya se empezó.</summary>
    public async Task<IActionResult> Valoracion(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Index));
        }
        if (detail.Status != ClinicalEventStatus.EnValoracion)
        {
            return RedirectToAction(nameof(DetalleCambio), new { eventoId });
        }

        return View(new ValoracionViewModel(detail, ValoracionFormModel.From(detail)));
    }

    /// <summary>ENF-05 "guardar borrador". Ante un conflicto de concurrencia no se sobrescribe el trabajo
    /// ajeno: se vuelve a mostrar lo escrito, con la revisión antigua, y se pide recargar.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Valoracion([Bind(Prefix = "Form")] ValoracionFormModel form, CancellationToken ct)
    {
        var detail = await FindEventAsync(form.EventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Index));
        }
        if (!ModelState.IsValid)
        {
            return View(new ValoracionViewModel(detail, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await service.SaveNursingAssessmentAsync(new SaveNursingAssessmentCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Revision,
            form.Hallazgos, form.Valoracion, form.Actuaciones, form.Comunicaciones, form.Resultado,
            form.TemperaturaCelsius, form.TensionSistolica, form.TensionDiastolica, form.FrecuenciaCardiaca,
            form.FrecuenciaRespiratoria, form.SaturacionO2, form.SoporteRespiratorio, form.FlujoO2, form.Glucemia,
            form.OtraConstanteNombre, form.OtraConstanteValor, form.OtraConstanteUnidad), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Valoración guardada.";
            return RedirectToAction(nameof(DetalleCambio), new { eventoId = form.EventoId });
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code == ApplicationFailureCode.Conflict
            ? ConcurrencyMessage + " Lo que has escrito sigue aquí para que puedas copiarlo."
            : "Revisa los datos: la valoración necesita al menos un dato, la PA con ambas cifras, el flujo de O₂ solo con oxigenoterapia y la otra constante con nombre y valor.");
        return View(new ValoracionViewModel(detail, form));
    }

    /// <summary>ENF-06 "decisión asistencial": las cuatro salidas, desde una valoración ya guardada o desde un
    /// seguimiento que se resuelve. Cerrar e iniciar seguimiento están disponibles; escalado y protocolo
    /// urgente llegan con las historias 5 y 6.</summary>
    public async Task<IActionResult> Decision(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Index));
        }
        if (!CanDecide(detail))
        {
            return RedirectToAction(nameof(DetalleCambio), new { eventoId });
        }

        return View(detail);
    }

    private static bool CanDecide(PendingChangeDetail detail) =>
        detail.Assessment is not null
        && detail.Status is ClinicalEventStatus.EnValoracion or ClinicalEventStatus.EnSeguimiento;

    /// <summary>ENF-07B: formulario para iniciar un seguimiento desde la decisión asistencial.</summary>
    public async Task<IActionResult> IniciarSeguimiento(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Index));
        }
        if (detail.Status != ClinicalEventStatus.EnValoracion || detail.Assessment is null)
        {
            return RedirectToAction(nameof(DetalleCambio), new { eventoId });
        }

        return View(new IniciarSeguimientoViewModel(
            detail, new IniciarSeguimientoFormModel { EventoId = detail.EventId, Revision = detail.Revision }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IniciarSeguimiento([Bind(Prefix = "Form")] IniciarSeguimientoFormModel form, CancellationToken ct)
    {
        var detail = await FindEventAsync(form.EventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Index));
        }
        if (!ModelState.IsValid)
        {
            return View(new IniciarSeguimientoViewModel(detail, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await service.StartFollowUpAsync(new StartFollowUpCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Revision,
            form.FechaPrevista, form.Criterio, form.IndicacionesContinuidad), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Seguimiento iniciado.";
            return RedirectToAction(nameof(Seguimiento), new { eventoId = form.EventoId });
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.Conflict => ConcurrencyMessage + " Lo que has escrito sigue aquí para que puedas copiarlo.",
            ApplicationFailureCode.InvalidInput =>
                "Revisa los datos: indica una fecha prevista o un criterio de revisión. Para iniciar un seguimiento hace falta una valoración guardada.",
            _ => result.Error.Message,
        });
        return View(new IniciarSeguimientoViewModel(detail, form));
    }

    /// <summary>ENF-08/ENF-09: el seguimiento abierto de un evento, con sus acciones y los formularios para
    /// registrar una actuación, reprogramar, transferir o confirmar la recepción.</summary>
    public async Task<IActionResult> Seguimiento(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Index));
        }
        if (detail.Status != ClinicalEventStatus.EnSeguimiento || detail.FollowUp is null)
        {
            return RedirectToAction(nameof(DetalleCambio), new { eventoId });
        }

        return View(new SeguimientoViewModel(detail, null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Seguimiento([Bind(Prefix = "Form")] SeguimientoAccionFormModel form, CancellationToken ct)
    {
        var detail = await FindEventAsync(form.EventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Index));
        }
        if (detail.FollowUp is null)
        {
            return RedirectToAction(nameof(DetalleCambio), new { eventoId = form.EventoId });
        }
        if (!ModelState.IsValid)
        {
            return View(new SeguimientoViewModel(detail, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await service.RecordFollowUpActionAsync(new RecordFollowUpActionCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Revision, form.Tipo,
            form.Texto, form.FechaPrevista, form.Criterio, form.EquipoEntrante, form.TransferenciaId), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = form.Tipo switch
            {
                FollowUpActionType.Reprogramacion => "Seguimiento reprogramado.",
                FollowUpActionType.Transferencia => "Transferencia registrada. Queda pendiente de recepción.",
                FollowUpActionType.Recepcion => "Recepción confirmada.",
                _ => "Actuación registrada.",
            };
            return RedirectToAction(nameof(Seguimiento), new { eventoId = form.EventoId });
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.Conflict => ConcurrencyMessage + " Lo que has escrito sigue aquí para que puedas copiarlo.",
            ApplicationFailureCode.InvalidInput => form.Tipo switch
            {
                FollowUpActionType.Reprogramacion => "Para reprogramar indica una nueva fecha o criterio y justifica el cambio.",
                FollowUpActionType.Transferencia => "Para transferir indica el equipo o turno entrante.",
                _ => "Escribe la actuación antes de registrarla.",
            },
            _ => result.Error.Message,
        });
        return View(new SeguimientoViewModel(detail, form));
    }

    /// <summary>ENF-07A: resumen de la valoración y decisión explícita de comunicación familiar
    /// (ENF-14/ENF-15) antes de cerrar.</summary>
    public async Task<IActionResult> Cerrar(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Index));
        }
        if (!CanDecide(detail))
        {
            return RedirectToAction(nameof(DetalleCambio), new { eventoId });
        }

        return View(new CerrarViewModel(detail, new CerrarFormModel
        {
            EventoId = detail.EventId, Revision = detail.Revision, OperacionId = Guid.NewGuid(),
        }));
    }

    /// <summary>ENF-07A "cerrar": idempotente por OperacionId (repetir el envío no cierra dos veces). Ante un
    /// conflicto se conserva lo escrito y se pide recargar, igual que al guardar la valoración.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cerrar([Bind(Prefix = "Form")] CerrarFormModel form, CancellationToken ct)
    {
        var detail = await FindEventAsync(form.EventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Index));
        }
        if (!ModelState.IsValid)
        {
            return View(new CerrarViewModel(detail, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var preparar = form.Comunicacion == FamilyCommunicationDecision.Preparar;
        var result = await service.CloseClinicalEventAsync(new CloseClinicalEventCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Revision, form.OperacionId,
            form.Comunicacion, preparar ? form.TipoComunicacion : null, preparar ? form.TextoComunicacion : null), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = preparar
                ? "Evento cerrado. La comunicación familiar queda pendiente de aprobación."
                : "Evento cerrado.";
            return RedirectToAction(nameof(DetalleCambio), new { eventoId = form.EventoId });
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.Conflict => ConcurrencyMessage + " Lo que has escrito sigue aquí para que puedas copiarlo.",
            ApplicationFailureCode.InvalidInput =>
                "Revisa los datos: decide si se comunica a la familia y, si preparas la comunicación, elige el tipo y escribe el texto. Para cerrar hace falta una valoración guardada.",
            _ => result.Error.Message,
        });
        return View(new CerrarViewModel(detail, form));
    }

    private const string ConcurrencyMessage =
        "Este evento ha cambiado desde que lo abriste (otro profesional, u otra pestaña o pulsación tuya). Recarga para ver la versión actual antes de continuar.";

    private async Task<PendingChangeDetail?> FindEventAsync(Guid eventoId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null || eventoId == Guid.Empty)
        {
            return null;
        }
        var result = await service.FindPendingChangeDetailAsync(
            new FindPendingChangeDetailCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), eventoId), ct);
        return result.Ok ? result.Value : null;
    }

    public async Task<IActionResult> Residentes(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Residentes)) });
        }

        var command = new ListScopeResidentsCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId));
        var result = await service.ListScopeResidentsAsync(command, ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(Array.Empty<ScopeResidentSummary>());
        }

        return View(result.Value);
    }

    public async Task<IActionResult> Residente(Guid residenteId, CancellationToken ct)
    {
        if (residenteId == Guid.Empty)
        {
            return RedirectToAction(nameof(Residentes));
        }
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Residente), new { residenteId }) });
        }

        var centroId = CenterId.From(activeScope.CenterId);
        var findResult = await service.FindScopeResidentAsync(
            new FindScopeResidentCommand(activeScope.ProfileScopeId, centroId, ResidentId.From(residenteId)), ct);
        if (!findResult.Ok || findResult.Value is null)
        {
            return RedirectToAction(nameof(Residentes));
        }

        var baselineResult = await service.ReadCurrentBaselineAsync(
            new ReadCurrentBaselineCommand(activeScope.ProfileScopeId, centroId, findResult.Value.ResidentId), ct);
        return View(new EnfermeriaResidentDetailViewModel(findResult.Value, baselineResult.Ok ? baselineResult.Value : null));
    }

    /// <summary>ENF-16: formulario de alta. Al guardar se continúa en el detalle del evento, desde donde
    /// se empieza su valoración.</summary>
    public async Task<IActionResult> RegistrarEvento(Guid residenteId, CancellationToken ct)
    {
        var resolved = await ResolveScopeResidentAsync(residenteId, ct);
        if (resolved is null)
        {
            return RedirectToAction(nameof(Residentes));
        }

        ViewBag.Resident = resolved.Value.Resident;
        return View(new RegistrarEventoFormModel { ResidenteId = residenteId, OperacionId = Guid.NewGuid() });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarEvento(RegistrarEventoFormModel form, CancellationToken ct)
    {
        var resolved = await ResolveScopeResidentAsync(form.ResidenteId, ct);
        if (resolved is null)
        {
            return RedirectToAction(nameof(Residentes));
        }
        if (!ModelState.IsValid)
        {
            ViewBag.Resident = resolved.Value.Resident;
            return View(form);
        }

        var command = new RegisterClinicalEventCommand(
            resolved.Value.Scope.ProfileScopeId, CenterId.From(resolved.Value.Scope.CenterId), ResidentId.From(form.ResidenteId),
            form.Observacion, form.Clasificacion, form.DatosClinicosPertinentes, form.OperacionId);
        var result = await service.RegisterClinicalEventAsync(command, ct);
        if (!result.Ok)
        {
            TempData["Error"] = result.Error!.Message;
            return RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId });
        }

        // ENF-16 "guardar y continuar directamente la valoración": al detalle del evento recién creado.
        TempData["Mensaje"] = "Evento registrado. Ya puedes empezar su valoración.";
        return RedirectToAction(nameof(DetalleCambio), new { eventoId = result.Value!.EventId });
    }

    /// <summary>Compartido por Residente y RegistrarEvento: confirma que el residente está en el ámbito
    /// (ENF-17, mismo criterio que la lista). Si no lo está, el llamador redirige a ENF-17 sin distinguir
    /// "no está en el ámbito" de "no existe".</summary>
    private async Task<(ActiveProfileScopeCookieValue Scope, ScopeResidentSummary Resident)?> ResolveScopeResidentAsync(
        Guid residenteId, CancellationToken ct)
    {
        if (residenteId == Guid.Empty)
        {
            return null;
        }
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return null;
        }
        var centroId = CenterId.From(activeScope.CenterId);
        var findResult = await service.FindScopeResidentAsync(
            new FindScopeResidentCommand(activeScope.ProfileScopeId, centroId, ResidentId.From(residenteId)), ct);
        return !findResult.Ok || findResult.Value is null ? null : (activeScope, findResult.Value);
    }
}
