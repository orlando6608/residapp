using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
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
/// ENF-07B a ENF-09 (seguimiento, su bandeja y la transferencia de turno); historia 6: ENF-11 (protocolo urgente,
/// sin la derivación todavía). Traduce a EnfermeriaApplicationService; la
/// autorización y las reglas de negocio no viven aquí.
/// </summary>
public sealed class EnfermeriaController(
    EnfermeriaApplicationService service, FindEmergencyContact findEmergencyContact, ListMilestoneWarnings milestoneWarnings) : Controller
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
        var indicaciones = await service.ListPendingIndicationsAsync(new ListPendingIndicationsCommand(activeScope.ProfileScopeId, centroId), ct);
        var protocolos = await service.ListUrgentProtocolsAsync(new ListUrgentProtocolsCommand(activeScope.ProfileScopeId, centroId), ct);
        var escalados = await service.ListOpenEscalationsAsync(new ListOpenEscalationsCommand(activeScope.ProfileScopeId, centroId), ct);
        var avisos = await milestoneWarnings.ExecuteAsync(new ListMilestoneWarningsCommand(activeScope.ProfileScopeId, centroId), ct);
        return View(new EnfermeriaInicioViewModel(
            ordinarios.Ok ? ordinarios.Value!.Count : 0, prioritarios.Ok ? prioritarios.Value!.Count : 0,
            seguimientos.Ok ? seguimientos.Value!.Count : 0,
            seguimientos.Ok ? seguimientos.Value!.Count(s => FollowUpDisplay.IsOverdue(s.DueDate)) : 0,
            comunicaciones.Ok ? comunicaciones.Value!.Count : 0,
            indicaciones.Ok ? indicaciones.Value!.Count : 0,
            indicaciones.Ok ? indicaciones.Value!.Count(i => i.Indication.Status == MedicalIndicationStatus.PendienteLectura) : 0,
            protocolos.Ok ? protocolos.Value!.Count : 0,
            escalados.Ok ? escalados.Value!.Count : 0, avisos.Ok ? avisos.Value : null));
    }

    /// <summary>ENF-10: indicaciones de Medicina pendientes de leer o de registrar su resultado, compartidas
    /// por la Enfermería de la unidad.</summary>
    public async Task<IActionResult> Indicaciones(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Indicaciones)) });
        }

        var result = await service.ListPendingIndicationsAsync(
            new ListPendingIndicationsCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(Array.Empty<MedicalIndicationListItem>());
        }

        return View(result.Value);
    }

    /// <summary>ENF-10: confirmar la lectura (realizada = null) o registrar el resultado de una indicación ya
    /// leída. "No realizada" exige la incidencia. Ante un conflicto se recarga la bandeja con un aviso.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProgresoIndicacion(
        Guid eventoId, Guid indicacionId, int revision, bool? realizada, string? incidencia, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null || eventoId == Guid.Empty || indicacionId == Guid.Empty)
        {
            return RedirectToAction(nameof(Index));
        }

        var result = await service.RecordIndicationProgressAsync(new RecordIndicationProgressCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), eventoId, indicacionId, revision, realizada, incidencia), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = realizada switch
            {
                null => "Lectura confirmada. Registra el resultado cuando la hayas realizado o si no ha podido hacerse.",
                true => "Indicación registrada como realizada.",
                false => "Indicación registrada como no realizada, con su incidencia.",
            };
        }
        else
        {
            TempData["Error"] = result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict =>
                    "Esta indicación ha cambiado desde que abriste la bandeja (otro profesional, u otra pestaña o pulsación tuya). Revisa su estado actual.",
                ApplicationFailureCode.InvalidInput => "Para registrarla como no realizada, describe la incidencia.",
                _ => result.Error.Message,
            };
        }
        return RedirectToAction(nameof(Indicaciones));
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

    /// <summary>Escalados abiertos: los eventos que la Enfermería de la unidad escaló a Medicina y siguen abiertos, que
    /// ya no están en sus bandejas. Solo lectura; el detalle muestra la parte médica.</summary>
    public async Task<IActionResult> Escalados(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Escalados)) });
        }

        var result = await service.ListOpenEscalationsAsync(
            new ListOpenEscalationsCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(Array.Empty<OpenEscalationSummary>());
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
    public async Task<IActionResult> Ordinarios(PendingChangeFilter filtro, CancellationToken ct)
    {
        // ENF-02: un valor del filtro mal formado en la URL se ignora (queda sin filtrar), no se muestra como error.
        ModelState.Clear();
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Request.Path + Request.QueryString });
        }

        var result = await service.ListPendingChangesAsync(new ListPendingChangesCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), DailyChangeClassification.Ordinario), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(PendingChangeListViewModel.From([], filtro));
        }

        return View(PendingChangeListViewModel.From(result.Value!, filtro));
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
        return View(new EnfermeriaChangeDetailViewModel(
            detailResult.Value, baselineResult.Ok ? baselineResult.Value : null, service.CorrectionWindow));
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

    /// <summary>COR-01: corrección de la valoración por su autor, dentro de la ventana y cuando ya no se puede
    /// guardar de forma normal. El servidor vuelve a comprobarlo todo al guardar.</summary>
    public async Task<IActionResult> CorregirValoracion(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Index));
        }
        if (detail.Assessment is not { } assessment || AmendmentAction(detail) != AssessmentAmendmentAction.Corregir)
        {
            TempData["Error"] = CorrectionUnavailableMessage;
            return RedirectToAction(nameof(DetalleCambio), new { eventoId });
        }

        return View(new CorreccionValoracionViewModel(detail, CorreccionValoracionFormModel.From(detail, assessment)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CorregirValoracion([Bind(Prefix = "Form")] CorreccionValoracionFormModel form, CancellationToken ct)
    {
        var detail = await FindEventAsync(form.EventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Index));
        }
        if (AmendmentAction(detail) != AssessmentAmendmentAction.Corregir)
        {
            TempData["Error"] = CorrectionUnavailableMessage;
            return RedirectToAction(nameof(DetalleCambio), new { eventoId = form.EventoId });
        }
        if (!ModelState.IsValid)
        {
            return View(new CorreccionValoracionViewModel(detail, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await service.CorrectNursingAssessmentAsync(new CorrectNursingAssessmentCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Correcciones, form.Motivo,
            form.Hallazgos, form.Valoracion, form.Actuaciones, form.Comunicaciones, form.Resultado,
            form.TemperaturaCelsius, form.TensionSistolica, form.TensionDiastolica, form.FrecuenciaCardiaca,
            form.FrecuenciaRespiratoria, form.SaturacionO2, form.SoporteRespiratorio, form.FlujoO2, form.Glucemia,
            form.OtraConstanteNombre, form.OtraConstanteValor, form.OtraConstanteUnidad), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Valoración corregida.";
            return RedirectToAction(nameof(DetalleCambio), new { eventoId = form.EventoId });
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.Conflict => CorrectionConflictMessage,
            ApplicationFailureCode.AccessDenied => CorrectionUnavailableMessage,
            _ => "Revisa los datos: el motivo es obligatorio, la valoración necesita al menos un dato, la PA con ambas cifras, el flujo de O₂ solo con oxigenoterapia y la otra constante con nombre y valor.",
        });
        return View(new CorreccionValoracionViewModel(detail, form));
    }

    /// <summary>COR-02: rectificación añadida por el autor de la valoración, una vez pasada la ventana.</summary>
    public async Task<IActionResult> RectificarValoracion(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Index));
        }
        if (AmendmentAction(detail) != AssessmentAmendmentAction.Rectificar)
        {
            TempData["Error"] = CorrectionUnavailableMessage;
            return RedirectToAction(nameof(DetalleCambio), new { eventoId });
        }

        var form = new RectificacionFormModel
        {
            EventoId = eventoId, Rectificaciones = detail.Assessment!.Amendments!.Rectifications.Count,
        };
        return View("~/Views/Shared/RectificarValoracion.cshtml",
            new RectificacionViewModel(detail, "valoración de Enfermería", nameof(DetalleCambio), form));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RectificarValoracion([Bind(Prefix = "Form")] RectificacionFormModel form, CancellationToken ct)
    {
        var detail = await FindEventAsync(form.EventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Index));
        }
        var model = new RectificacionViewModel(detail, "valoración de Enfermería", nameof(DetalleCambio), form);
        if (!ModelState.IsValid)
        {
            return View("~/Views/Shared/RectificarValoracion.cshtml", model);
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await service.RectifyAssessmentAsync(new RectifyAssessmentCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Rectificaciones, form.Texto, form.Motivo,
            SystemProfile.Enfermeria), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Rectificación añadida.";
            return RedirectToAction(nameof(DetalleCambio), new { eventoId = form.EventoId });
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.Conflict => CorrectionConflictMessage,
            ApplicationFailureCode.AccessDenied => CorrectionUnavailableMessage,
            _ => "Revisa los datos: la rectificación y el motivo son obligatorios. Si todavía estás dentro del plazo, corrige la valoración en lugar de rectificarla.",
        });
        return View("~/Views/Shared/RectificarValoracion.cshtml", model);
    }

    private AssessmentAmendmentAction AmendmentAction(PendingChangeDetail detail) =>
        AssessmentAmendmentDisplay.Action(
            detail.Assessment?.Amendments, detail.Status == ClinicalEventStatus.EnValoracion, service.CorrectionWindow);

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

    /// <summary>Además de desde la decisión asistencial, se cierra desde el protocolo urgente activo.</summary>
    private static bool CanClose(PendingChangeDetail detail) =>
        CanDecide(detail) || (detail.Assessment is not null && detail.Status == ClinicalEventStatus.ProtocoloUrgente);

    /// <summary>ENF-11: confirmar la activación del protocolo urgente, con una nota opcional.</summary>
    public async Task<IActionResult> ActivarProtocolo(Guid eventoId, CancellationToken ct)
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

        return View(new ActivarProtocoloViewModel(
            detail, new ActivarProtocoloFormModel { EventoId = detail.EventId, Revision = detail.Revision }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ActivarProtocolo([Bind(Prefix = "Form")] ActivarProtocoloFormModel form, CancellationToken ct)
    {
        var detail = await FindEventAsync(form.EventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Index));
        }
        if (!ModelState.IsValid)
        {
            return View(new ActivarProtocoloViewModel(detail, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await service.ActivateUrgentProtocolAsync(new ActivateUrgentProtocolCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Revision, form.Nota), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Protocolo urgente activado. Documenta las actuaciones cuando puedas: la atención va primero.";
            return RedirectToAction(nameof(Protocolo), new { eventoId = form.EventoId });
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.Conflict => ConcurrencyMessage + " Lo que has escrito sigue aquí para que puedas copiarlo.",
            ApplicationFailureCode.InvalidInput => "Revisa los datos: la nota es demasiado larga. Para activar el protocolo hace falta una valoración guardada.",
            _ => result.Error.Message,
        });
        return View(new ActivarProtocoloViewModel(detail, form));
    }

    /// <summary>ENF-11: bandeja de protocolos urgentes activos de Enfermería.</summary>
    public async Task<IActionResult> Protocolos(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Protocolos)) });
        }

        var result = await service.ListUrgentProtocolsAsync(
            new ListUrgentProtocolsCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(Array.Empty<UrgentProtocolSummary>());
        }

        return View(result.Value);
    }

    /// <summary>ENF-11: el protocolo urgente activo, con sus registros y los formularios para documentar
    /// actuaciones, evolución y contactos con servicios.</summary>
    public async Task<IActionResult> Protocolo(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Protocolos));
        }
        if (detail.Status != ClinicalEventStatus.ProtocoloUrgente || detail.UrgentProtocol is null)
        {
            return RedirectToAction(nameof(DetalleCambio), new { eventoId });
        }

        return View(new ProtocoloViewModel(detail, null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Protocolo([Bind(Prefix = "Form")] ProtocoloRegistroFormModel form, CancellationToken ct)
    {
        var detail = await FindEventAsync(form.EventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Protocolos));
        }
        if (detail.UrgentProtocol is null)
        {
            return RedirectToAction(nameof(DetalleCambio), new { eventoId = form.EventoId });
        }
        if (!ModelState.IsValid)
        {
            return View(new ProtocoloViewModel(detail, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await service.RecordUrgentProtocolEntryAsync(new RecordUrgentProtocolEntryCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Revision, form.Tipo,
            form.Texto, form.Servicio, form.ContactadoEnOffset), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = UrgentProtocolDisplay.SavedMessage(form.Tipo);
            return RedirectToAction(nameof(Protocolo), new { eventoId = form.EventoId });
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.Conflict => ConcurrencyMessage + " Lo que has escrito sigue aquí para que puedas copiarlo.",
            ApplicationFailureCode.InvalidInput => UrgentProtocolDisplay.InvalidMessage(form.Tipo),
            _ => result.Error.Message,
        });
        return View(new ProtocoloViewModel(detail, form));
    }

    private static bool CanRefer(PendingChangeDetail detail) =>
        detail.Status == ClinicalEventStatus.ProtocoloUrgente && detail.UrgentProtocol is not null && detail.Referral is null;

    /// <summary>ENF-12: derivar a Urgencias desde el protocolo urgente activo: el motivo y la información
    /// adicional que escribe el profesional; el resto del informe son datos automáticos.</summary>
    public async Task<IActionResult> Derivar(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Protocolos));
        }
        if (!CanRefer(detail))
        {
            return RedirectToAction(nameof(DetalleCambio), new { eventoId });
        }

        return View(new DerivarViewModel(
            detail, new DerivarFormModel { EventoId = detail.EventId, Revision = detail.Revision, OperacionId = Guid.NewGuid() }, null));
    }

    /// <summary>ENF-12: "ver la vista previa" (obligatoria, DER-02) y "firmar y generar PDF", que exige la huella
    /// de esa vista previa e idempotente por OperacionId. Si el informe cambió entretanto se muestra la vista
    /// previa nueva para revisarla antes de firmar.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Derivar([Bind(Prefix = "Form")] DerivarFormModel form, CancellationToken ct)
    {
        var detail = await FindEventAsync(form.EventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Protocolos));
        }
        if (detail.Referral is not null)
        {
            return RedirectToAction(nameof(Protocolo), new { eventoId = form.EventoId });
        }
        if (!ModelState.IsValid || form.Accion == DerivarFormModel.Editar)
        {
            return View(new DerivarViewModel(detail, form, null));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var sections = await BuildReferralSectionsAsync(detail, ct);
        if (sections is null)
        {
            ModelState.AddModelError(string.Empty, "No se ha podido reunir la información del informe. Recarga e inténtalo de nuevo.");
            return View(new DerivarViewModel(detail, form, null));
        }
        var preview = ReferralReportContent.Compose(sections, new ReferralReportInput(form.Motivo, form.InformacionAdicional, form.Comunicaciones));
        if (form.Accion != DerivarFormModel.Firmar)
        {
            return View(new DerivarViewModel(detail, form, preview));
        }

        var result = await service.SignReferralReportAsync(new SignReferralReportCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Revision, form.OperacionId,
            sections, form.Motivo, form.InformacionAdicional, form.Huella, form.Comunicaciones), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Informe de derivación firmado. Registra el intento de llamada a la familia cuando puedas: la atención va primero.";
            return RedirectToAction(nameof(Protocolo), new { eventoId = form.EventoId });
        }

        if (result.Error!.Code == ApplicationFailureCode.Conflict)
        {
            ModelState.AddModelError(string.Empty, ReferralDisplay.ChangedMessage);
            form.Revision = detail.Revision;
            return View(new DerivarViewModel(detail, form, preview));
        }
        ModelState.AddModelError(string.Empty, result.Error.Code == ApplicationFailureCode.InvalidInput
            ? ReferralDisplay.InvalidMessage
            : result.Error.Message);
        return View(new DerivarViewModel(detail, form, null));
    }

    private async Task<IReadOnlyList<ReferralReportSection>?> BuildReferralSectionsAsync(PendingChangeDetail detail, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var identification = await service.FindResidentIdentificationAsync(
            new FindResidentIdentificationCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), detail.EventId), ct);
        return identification.Ok
            ? ReferralReportBuilder.Build(detail, await ReadBaselineAsync(detail, ct), identification.Value!)
            : null;
    }

    /// <summary>ENF-14/DER-06: registrar un intento de llamada al contacto familiar tras firmar el informe.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IntentoLlamada([Bind(Prefix = "Call")] IntentoLlamadaFormModel form, CancellationToken ct)
    {
        var detail = await FindEventAsync(form.EventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Protocolos));
        }
        if (detail.Status != ClinicalEventStatus.ProtocoloUrgente || detail.Referral is null)
        {
            return RedirectToAction(nameof(DetalleCambio), new { eventoId = form.EventoId });
        }
        if (!ModelState.IsValid)
        {
            return View(nameof(Protocolo), new ProtocoloViewModel(detail, null, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await service.RecordFamilyCallAttemptAsync(new RecordFamilyCallAttemptCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Revision, form.Contacto,
            form.LlamadoEnOffset, form.Resultado, form.Nota), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Intento de llamada registrado.";
            return RedirectToAction(nameof(Protocolo), new { eventoId = form.EventoId });
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.Conflict => ConcurrencyMessage + " Lo que has escrito sigue aquí para que puedas copiarlo.",
            ApplicationFailureCode.InvalidInput => ReferralDisplay.CallInvalidMessage,
            _ => result.Error.Message,
        });
        return View(nameof(Protocolo), new ProtocoloViewModel(detail, null, form));
    }

    /// <summary>DER-05: el PDF firmado del informe de derivación. Cada descarga queda auditada.</summary>
    public async Task<IActionResult> InformeDerivacion(Guid eventoId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction(nameof(Index));
        }

        var result = await service.DownloadReferralReportAsync(
            new DownloadReferralReportCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), eventoId), ct);
        return result.Ok
            ? File(result.Value!.Content, "application/pdf", ReferralDisplay.FileName(result.Value.SignedAt))
            : RedirectToAction(nameof(DetalleCambio), new { eventoId });
    }


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

    /// <summary>ENF-10: muestra la información que se enviará a Medicina y pide el motivo del escalado.</summary>
    public async Task<IActionResult> Escalar(Guid eventoId, CancellationToken ct)
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

        return View(new EscalarViewModel(detail, await ReadBaselineAsync(detail, ct),
            new EscalarFormModel { EventoId = detail.EventId, Revision = detail.Revision }));
    }

    /// <summary>ENF-09 "escalar a Medicina": la valoración se cierra y el evento pasa a la bandeja de
    /// escalados de Medicina. Ante un conflicto se conserva lo escrito y se pide recargar.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Escalar([Bind(Prefix = "Form")] EscalarFormModel form, CancellationToken ct)
    {
        var detail = await FindEventAsync(form.EventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Index));
        }
        if (!ModelState.IsValid)
        {
            return View(new EscalarViewModel(detail, await ReadBaselineAsync(detail, ct), form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await service.EscalateClinicalEventAsync(new EscalateClinicalEventCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Revision, form.Motivo), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Evento escalado a Medicina.";
            return RedirectToAction(nameof(DetalleCambio), new { eventoId = form.EventoId });
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.Conflict => ConcurrencyMessage + " Lo que has escrito sigue aquí para que puedas copiarlo.",
            ApplicationFailureCode.InvalidInput =>
                "Revisa los datos: el motivo del escalado es obligatorio. Para escalar hace falta una valoración guardada.",
            _ => result.Error.Message,
        });
        return View(new EscalarViewModel(detail, await ReadBaselineAsync(detail, ct), form));
    }

    private async Task<CurrentBaselineSummary?> ReadBaselineAsync(PendingChangeDetail detail, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await service.ReadCurrentBaselineAsync(
            new ReadCurrentBaselineCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), detail.ResidentId), ct);
        return result.Ok ? result.Value : null;
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

        return View(new SeguimientoViewModel(detail, null, await TransferTeamsAsync(detail.EventId, ct)));
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
            return View(new SeguimientoViewModel(detail, form, await TransferTeamsAsync(detail.EventId, ct)));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await service.RecordFollowUpActionAsync(new RecordFollowUpActionCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Revision, form.Tipo,
            form.Texto, form.FechaPrevista, form.Criterio, form.EquipoEntranteId, form.TransferenciaId), ct);
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
                FollowUpActionType.Transferencia => "Para transferir elige un equipo de la unidad del residente.",
                _ => "Escribe la actuación antes de registrarla.",
            },
            _ => result.Error.Message,
        });
        return View(new SeguimientoViewModel(detail, form, await TransferTeamsAsync(detail.EventId, ct)));
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
        if (!CanClose(detail))
        {
            return RedirectToAction(nameof(DetalleCambio), new { eventoId });
        }

        return View(new CerrarViewModel(detail, CerrarFormModel.For(detail)));
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
            ApplicationFailureCode.InvalidInput when detail.Referral is not null => ReferralDisplay.CloseMessage,
            ApplicationFailureCode.InvalidInput =>
                "Revisa los datos: decide si se comunica a la familia y, si preparas la comunicación, elige el tipo y escribe el texto. Para cerrar hace falta una valoración guardada.",
            _ => result.Error.Message,
        });
        return View(new CerrarViewModel(detail, form));
    }

    private const string ConcurrencyMessage =
        "Este evento ha cambiado desde que lo abriste (otro profesional, u otra pestaña o pulsación tuya). Recarga para ver la versión actual antes de continuar.";

    private const string CorrectionUnavailableMessage =
        "No puedes corregir ni rectificar esta valoración: solo lo hace quien la guardó por última vez, cuando ya no se puede seguir editando. Dentro del plazo se corrige; después, se añade una rectificación.";

    private const string CorrectionConflictMessage =
        "La valoración ha cambiado desde que abriste el formulario (otra pestaña o pulsación tuya). Lo que has escrito sigue aquí: vuelve al detalle para ver la versión actual.";

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

    public async Task<IActionResult> Residentes(ResidentListFilter filtro, CancellationToken ct)
    {
        // ENF-17/MED-19: un valor del filtro mal formado en la URL se ignora (queda sin filtrar), no se muestra como error.
        ModelState.Clear();
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Request.Path + Request.QueryString });
        }

        var command = new ListScopeResidentsCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId));
        var result = await service.ListScopeResidentsAsync(command, ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(ResidentListViewModel.From([], filtro));
        }

        return View(ResidentListViewModel.From(result.Value!, filtro));
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
        var openEvents = await service.ListOpenEventsAsync(
            new ListOpenEventsCommand(activeScope.ProfileScopeId, centroId, findResult.Value.ResidentId, SystemProfile.Enfermeria), ct);
        var contact = await findEmergencyContact.ExecuteAsync(
            new FindScopeResidentCommand(activeScope.ProfileScopeId, centroId, findResult.Value.ResidentId), ct);
        return View(new EnfermeriaResidentDetailViewModel(findResult.Value, baselineResult.Ok ? baselineResult.Value : null,
            OpenEvents: openEvents.Ok ? openEvents.Value : null, EmergencyContacts: contact.Ok ? contact.Value : null));
    }

    /// <summary>ENF-23/ENF-24 (historia 11): eventos cerrados del residente, con el basal y la ubicación de su
    /// fecha (HIS-03), y las versiones firmadas del basal. Parcial común con Medicina.</summary>
    public async Task<IActionResult> Historial(Guid residenteId, CancellationToken ct)
    {
        if (residenteId == Guid.Empty)
        {
            return RedirectToAction(nameof(Residentes));
        }
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Historial), new { residenteId }) });
        }

        var centroId = CenterId.From(activeScope.CenterId);
        var findResult = await service.FindScopeResidentAsync(
            new FindScopeResidentCommand(activeScope.ProfileScopeId, centroId, ResidentId.From(residenteId)), ct);
        if (!findResult.Ok || findResult.Value is null)
        {
            return RedirectToAction(nameof(Residentes));
        }

        var residentId = findResult.Value.ResidentId;
        var events = await service.ListClosedEventsAsync(
            new ListClosedEventsCommand(activeScope.ProfileScopeId, centroId, residentId, SystemProfile.Enfermeria), ct);
        var baselines = await service.ReadBaselineHistoryAsync(
            new ReadBaselineHistoryCommand(activeScope.ProfileScopeId, centroId, residentId), ct);
        return View("~/Views/Shared/Historial.cshtml",
            ResidentHistoryViewModel.From(findResult.Value, events, baselines, nameof(DetalleCambio)));
    }

    /// <summary>ENF-24 (historia 11): una versión firmada del basal, vigente o histórica, con sus nueve áreas y el
    /// Barthel por ítems. Vista común con Medicina.</summary>
    public async Task<IActionResult> VersionBasal(Guid residenteId, int version, CancellationToken ct)
    {
        if (residenteId == Guid.Empty)
        {
            return RedirectToAction(nameof(Residentes));
        }
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope",
                new { returnUrl = Url.Action(nameof(VersionBasal), new { residenteId, version }) });
        }

        var centroId = CenterId.From(activeScope.CenterId);
        var findResult = await service.FindScopeResidentAsync(
            new FindScopeResidentCommand(activeScope.ProfileScopeId, centroId, ResidentId.From(residenteId)), ct);
        if (!findResult.Ok || findResult.Value is null)
        {
            return RedirectToAction(nameof(Residentes));
        }

        var result = await service.ReadBaselineVersionAsync(
            new ReadBaselineVersionCommand(activeScope.ProfileScopeId, centroId, findResult.Value.ResidentId, version), ct);
        if (!result.Ok || result.Value is null)
        {
            return RedirectToAction(nameof(Historial), new { residenteId });
        }
        return View("~/Views/Shared/VersionBasal.cshtml", new BaselineVersionViewModel(findResult.Value, result.Value));
    }

    /// <summary>ENF-04/HIS-02: línea temporal completa del residente, bajo demanda y en solo lectura, dentro del
    /// ámbito (decisión del usuario, 2026-09-30). Vista común con Medicina.</summary>
    public async Task<IActionResult> LineaTemporal(Guid residenteId, CancellationToken ct)
    {
        if (residenteId == Guid.Empty)
        {
            return RedirectToAction(nameof(Residentes));
        }
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(LineaTemporal), new { residenteId }) });
        }

        var centroId = CenterId.From(activeScope.CenterId);
        var findResult = await service.FindScopeResidentAsync(
            new FindScopeResidentCommand(activeScope.ProfileScopeId, centroId, ResidentId.From(residenteId)), ct);
        if (!findResult.Ok || findResult.Value is null)
        {
            return RedirectToAction(nameof(Residentes));
        }

        var timeline = await service.ReadResidentTimelineAsync(
            new ReadResidentTimelineCommand(activeScope.ProfileScopeId, centroId, findResult.Value.ResidentId, SystemProfile.Enfermeria), ct);
        return View("~/Views/Shared/LineaTemporal.cshtml",
            new ResidentTimelineViewModel(findResult.Value, timeline.Ok ? timeline.Value : null, nameof(DetalleCambio)));
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

    /// <summary>Los equipos activos de la unidad del evento, para elegir el entrante de una transferencia; sin ellos, no se puede transferir.</summary>
    private async Task<IReadOnlyList<TransferTeam>> TransferTeamsAsync(Guid eventId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await service.ListTransferTeamsAsync(
            new ListTransferTeamsQuery(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), eventId), ct);
        return result.Value ?? [];
    }
}
