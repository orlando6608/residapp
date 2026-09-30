using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Vertical Medicina: MED-01 (inicio con contadores), MED-02/MED-03 (bandeja y detalle de escalados, con las
/// fuentes de solo lectura), MED-04/MED-05 (empezar y guardar la valoración médica), MED-06 a MED-09
/// (conducta médica con la salida "registrar indicaciones" y el seguimiento de las indicaciones emitidas),
/// MED-10 a MED-12 (seguimiento médico, su bandeja y la continuidad entre turnos), MED-13 (protocolo urgente;
/// la derivación llegará en su bloque), MED-15 a MED-17 (cierre médico con la decisión de comunicación
/// familiar) y MED-18 a MED-20 (evento propio, lista y ficha de residentes). Traduce a
/// MedicinaApplicationService; la autorización y las reglas de negocio no viven aquí.
/// </summary>
public sealed class MedicinaController(MedicinaApplicationService service) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Index)) });
        }

        var centroId = CenterId.From(activeScope.CenterId);
        var escalados = await service.ListEscalationsAsync(new ListEscalationsCommand(activeScope.ProfileScopeId, centroId), ct);
        var indicaciones = await service.ListMedicalIndicationsAsync(new ListMedicalIndicationsCommand(activeScope.ProfileScopeId, centroId), ct);
        var seguimientos = await service.ListMedicalFollowUpsAsync(new ListMedicalFollowUpsCommand(activeScope.ProfileScopeId, centroId), ct);
        var protocolos = await service.ListUrgentProtocolsAsync(new ListUrgentProtocolsCommand(activeScope.ProfileScopeId, centroId), ct);
        if (!escalados.Ok)
        {
            ModelState.AddModelError(string.Empty, escalados.Error!.Message);
        }
        var lista = indicaciones.Ok ? indicaciones.Value! : [];
        return View(new MedicinaInicioViewModel(
            escalados.Ok ? escalados.Value!.Count : 0, lista.Count,
            lista.Count(i => i.Indication.Status == MedicalIndicationStatus.PendienteLectura),
            lista.Count(i => i.Indication.Status == MedicalIndicationStatus.NoRealizada),
            seguimientos.Ok ? seguimientos.Value!.Count : 0,
            seguimientos.Ok ? seguimientos.Value!.Count(s => FollowUpDisplay.IsOverdue(s.DueDate)) : 0,
            protocolos.Ok ? protocolos.Value!.Count : 0));
    }

    /// <summary>MED-02: bandeja de escalados (pendientes y en valoración médica), del más antiguo al más reciente.</summary>
    public async Task<IActionResult> Escalados(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Escalados)) });
        }

        var result = await service.ListEscalationsAsync(
            new ListEscalationsCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(Array.Empty<EscalationSummary>());
        }

        return View(result.Value);
    }

    /// <summary>MED-03: detalle de un escalado. Si no existe, no es un escalado o no está en el ámbito, se
    /// vuelve a la bandeja sin distinguir el motivo.</summary>
    public async Task<IActionResult> Escalado(Guid eventoId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Escalado), new { eventoId }) });
        }

        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Escalados));
        }

        var baseline = await service.ReadCurrentBaselineAsync(
            new ReadCurrentBaselineCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), detail.ResidentId), ct);
        return View(new MedicinaEscaladoViewModel(detail, baseline.Ok ? baseline.Value : null, service.CorrectionWindow));
    }

    /// <summary>MED-04 "iniciar valoración médica": registra profesional y hora en servidor. Si el evento
    /// cambió desde que se abrió el detalle, no se empieza y se vuelve al detalle ya recargado.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EmpezarValoracion(Guid eventoId, int revision, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null || eventoId == Guid.Empty)
        {
            return RedirectToAction(nameof(Index));
        }

        var result = await service.StartMedicalAssessmentAsync(
            new StartMedicalAssessmentCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), eventoId, revision), ct);
        if (result.Ok)
        {
            return RedirectToAction(nameof(Valoracion), new { eventoId });
        }

        TempData["Error"] = result.Error!.Code == ApplicationFailureCode.Conflict ? ConcurrencyMessage : result.Error.Message;
        return RedirectToAction(nameof(Escalado), new { eventoId });
    }

    /// <summary>MED-05: formulario de valoración médica, solo sobre un evento cuya valoración médica ya se empezó.</summary>
    public async Task<IActionResult> Valoracion(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Escalados));
        }
        if (detail.Status != ClinicalEventStatus.EnValoracionMedica)
        {
            return RedirectToAction(nameof(Escalado), new { eventoId });
        }

        return View(new ValoracionMedicaViewModel(detail, ValoracionMedicaFormModel.From(detail)));
    }

    /// <summary>MED-05 "guardar borrador". Ante un conflicto no se sobrescribe el trabajo ajeno: se vuelve a
    /// mostrar lo escrito, con la revisión antigua, y se pide recargar.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Valoracion([Bind(Prefix = "Form")] ValoracionMedicaFormModel form, CancellationToken ct)
    {
        var detail = await FindEventAsync(form.EventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Escalados));
        }
        if (!ModelState.IsValid)
        {
            return View(new ValoracionMedicaViewModel(detail, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await service.SaveMedicalAssessmentAsync(new SaveMedicalAssessmentCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Revision,
            form.HallazgosExploracion, form.Valoracion, form.Actuaciones,
            form.TemperaturaCelsius, form.TensionSistolica, form.TensionDiastolica, form.FrecuenciaCardiaca,
            form.FrecuenciaRespiratoria, form.SaturacionO2, form.SoporteRespiratorio, form.FlujoO2, form.Glucemia,
            form.OtraConstanteNombre, form.OtraConstanteValor, form.OtraConstanteUnidad), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Valoración médica guardada.";
            return RedirectToAction(nameof(Escalado), new { eventoId = form.EventoId });
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code == ApplicationFailureCode.Conflict
            ? ConcurrencyMessage + " Lo que has escrito sigue aquí para que puedas copiarlo."
            : "Revisa los datos: la valoración necesita al menos un dato, la PA con ambas cifras, el flujo de O₂ solo con oxigenoterapia y la otra constante con nombre y valor.");
        return View(new ValoracionMedicaViewModel(detail, form));
    }

    /// <summary>COR-01: corrección de la valoración médica por su autor, dentro de la ventana y cuando ya no se
    /// puede guardar de forma normal. El servidor vuelve a comprobarlo todo al guardar.</summary>
    public async Task<IActionResult> CorregirValoracion(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Escalados));
        }
        if (detail.Medical.Assessment is not { } assessment || AmendmentAction(detail) != AssessmentAmendmentAction.Corregir)
        {
            TempData["Error"] = CorrectionUnavailableMessage;
            return RedirectToAction(nameof(Escalado), new { eventoId });
        }

        return View(new CorreccionValoracionMedicaViewModel(detail, CorreccionValoracionMedicaFormModel.From(detail, assessment)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CorregirValoracion([Bind(Prefix = "Form")] CorreccionValoracionMedicaFormModel form, CancellationToken ct)
    {
        var detail = await FindEventAsync(form.EventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Escalados));
        }
        if (AmendmentAction(detail) != AssessmentAmendmentAction.Corregir)
        {
            TempData["Error"] = CorrectionUnavailableMessage;
            return RedirectToAction(nameof(Escalado), new { eventoId = form.EventoId });
        }
        if (!ModelState.IsValid)
        {
            return View(new CorreccionValoracionMedicaViewModel(detail, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await service.CorrectMedicalAssessmentAsync(new CorrectMedicalAssessmentCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Correcciones, form.Motivo,
            form.HallazgosExploracion, form.Valoracion, form.Actuaciones,
            form.TemperaturaCelsius, form.TensionSistolica, form.TensionDiastolica, form.FrecuenciaCardiaca,
            form.FrecuenciaRespiratoria, form.SaturacionO2, form.SoporteRespiratorio, form.FlujoO2, form.Glucemia,
            form.OtraConstanteNombre, form.OtraConstanteValor, form.OtraConstanteUnidad), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Valoración médica corregida.";
            return RedirectToAction(nameof(Escalado), new { eventoId = form.EventoId });
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.Conflict => CorrectionConflictMessage,
            ApplicationFailureCode.AccessDenied => CorrectionUnavailableMessage,
            _ => "Revisa los datos: el motivo es obligatorio, la valoración necesita al menos un dato, la PA con ambas cifras, el flujo de O₂ solo con oxigenoterapia y la otra constante con nombre y valor.",
        });
        return View(new CorreccionValoracionMedicaViewModel(detail, form));
    }

    /// <summary>COR-02: rectificación añadida por el autor de la valoración médica, una vez pasada la ventana.</summary>
    public async Task<IActionResult> RectificarValoracion(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Escalados));
        }
        if (AmendmentAction(detail) != AssessmentAmendmentAction.Rectificar)
        {
            TempData["Error"] = CorrectionUnavailableMessage;
            return RedirectToAction(nameof(Escalado), new { eventoId });
        }

        var form = new RectificacionFormModel
        {
            EventoId = eventoId, Rectificaciones = detail.Medical.Assessment!.Amendments!.Rectifications.Count,
        };
        return View("~/Views/Shared/RectificarValoracion.cshtml",
            new RectificacionViewModel(detail, "valoración médica", nameof(Escalado), form));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RectificarValoracion([Bind(Prefix = "Form")] RectificacionFormModel form, CancellationToken ct)
    {
        var detail = await FindEventAsync(form.EventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Escalados));
        }
        var model = new RectificacionViewModel(detail, "valoración médica", nameof(Escalado), form);
        if (!ModelState.IsValid)
        {
            return View("~/Views/Shared/RectificarValoracion.cshtml", model);
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await service.RectifyAssessmentAsync(new RectifyAssessmentCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Rectificaciones, form.Texto, form.Motivo,
            SystemProfile.Medicina), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Rectificación añadida.";
            return RedirectToAction(nameof(Escalado), new { eventoId = form.EventoId });
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
            detail.Medical.Assessment?.Amendments, detail.Status == ClinicalEventStatus.EnValoracionMedica, service.CorrectionWindow);

    /// <summary>MED-06 "conducta médica": las cuatro salidas, desde una valoración médica guardada, con
    /// indicaciones ya emitidas o al resolver un seguimiento médico. Están disponibles "registrar
    /// indicaciones", "cerrar" e "iniciar seguimiento médico" (este, solo si el evento aún no tuvo uno).</summary>
    public async Task<IActionResult> Conducta(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Escalados));
        }
        if (!CanDecide(detail))
        {
            return RedirectToAction(nameof(Escalado), new { eventoId });
        }

        return View(detail);
    }

    private static bool CanDecide(PendingChangeDetail detail) =>
        detail.Medical.Assessment is not null
        && detail.Status is ClinicalEventStatus.EnValoracionMedica or ClinicalEventStatus.ConIndicacionPendiente
            or ClinicalEventStatus.EnSeguimientoMedico;

    /// <summary>Además de desde la conducta, se cierra desde el protocolo urgente activo de Medicina.</summary>
    private static bool CanClose(PendingChangeDetail detail) =>
        CanDecide(detail) || (detail.Medical.Assessment is not null && detail.Status == ClinicalEventStatus.ProtocoloUrgenteMedico);

    /// <summary>MED-13: confirmar la activación del protocolo urgente, con una nota opcional.</summary>
    public async Task<IActionResult> ActivarProtocolo(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Escalados));
        }
        if (!CanDecide(detail))
        {
            return RedirectToAction(nameof(Escalado), new { eventoId });
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
            return RedirectToAction(nameof(Escalados));
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
            ApplicationFailureCode.InvalidInput => "Revisa los datos: la nota es demasiado larga. Para activar el protocolo hace falta una valoración médica guardada.",
            _ => result.Error.Message,
        });
        return View(new ActivarProtocoloViewModel(detail, form));
    }

    /// <summary>MED-13: bandeja de protocolos urgentes activos de Medicina.</summary>
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

    /// <summary>MED-13: el protocolo urgente activo, con sus registros y los formularios para documentar
    /// actuaciones, evolución y contactos con servicios.</summary>
    public async Task<IActionResult> Protocolo(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Protocolos));
        }
        if (detail.Status != ClinicalEventStatus.ProtocoloUrgenteMedico || detail.UrgentProtocol is null)
        {
            return RedirectToAction(nameof(Escalado), new { eventoId });
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
            return RedirectToAction(nameof(Escalado), new { eventoId = form.EventoId });
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
        detail.Status == ClinicalEventStatus.ProtocoloUrgenteMedico && detail.UrgentProtocol is not null && detail.Referral is null;

    /// <summary>MED-14: derivar a Urgencias desde el protocolo urgente activo de Medicina. Misma pantalla que
    /// la de Enfermería (DER-01).</summary>
    public async Task<IActionResult> Derivar(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Protocolos));
        }
        if (!CanRefer(detail))
        {
            return RedirectToAction(nameof(Escalado), new { eventoId });
        }

        return View(new DerivarViewModel(
            detail, new DerivarFormModel { EventoId = detail.EventId, Revision = detail.Revision, OperacionId = Guid.NewGuid() }, null));
    }

    /// <summary>MED-14: vista previa obligatoria y firma, como en Enfermería.</summary>
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
        var preview = ReferralReportContent.Compose(sections, new ReferralReportInput(form.Motivo, form.InformacionAdicional));
        if (form.Accion != DerivarFormModel.Firmar)
        {
            return View(new DerivarViewModel(detail, form, preview));
        }

        var result = await service.SignReferralReportAsync(new SignReferralReportCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Revision, form.OperacionId,
            sections, form.Motivo, form.InformacionAdicional, form.Huella), ct);
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
        if (!identification.Ok)
        {
            return null;
        }
        var baseline = await service.ReadCurrentBaselineAsync(
            new ReadCurrentBaselineCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), detail.ResidentId), ct);
        return ReferralReportBuilder.Build(detail, baseline.Ok ? baseline.Value : null, identification.Value!);
    }

    /// <summary>MED-16/DER-06: registrar un intento de llamada al contacto familiar tras firmar el informe.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IntentoLlamada([Bind(Prefix = "Call")] IntentoLlamadaFormModel form, CancellationToken ct)
    {
        var detail = await FindEventAsync(form.EventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Protocolos));
        }
        if (detail.Status != ClinicalEventStatus.ProtocoloUrgenteMedico || detail.Referral is null)
        {
            return RedirectToAction(nameof(Escalado), new { eventoId = form.EventoId });
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
            : RedirectToAction(nameof(Escalado), new { eventoId });
    }


    /// <summary>Un solo seguimiento médico por evento: se inicia desde la conducta si todavía no lo tuvo.</summary>
    private static bool CanStartFollowUp(PendingChangeDetail detail) =>
        CanDecide(detail) && detail.Medical.FollowUp is null;

    /// <summary>MED-10: formulario para iniciar el seguimiento médico desde la conducta.</summary>
    public async Task<IActionResult> IniciarSeguimiento(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Escalados));
        }
        if (!CanStartFollowUp(detail))
        {
            return RedirectToAction(nameof(Escalado), new { eventoId });
        }

        return View(new IniciarSeguimientoMedicoViewModel(
            detail, new IniciarSeguimientoMedicoFormModel { EventoId = detail.EventId, Revision = detail.Revision }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IniciarSeguimiento([Bind(Prefix = "Form")] IniciarSeguimientoMedicoFormModel form, CancellationToken ct)
    {
        var detail = await FindEventAsync(form.EventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Escalados));
        }
        if (!ModelState.IsValid)
        {
            return View(new IniciarSeguimientoMedicoViewModel(detail, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await service.StartMedicalFollowUpAsync(new StartMedicalFollowUpCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Revision,
            form.FechaPrevista, form.Criterio, form.Objetivo), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Seguimiento médico iniciado.";
            return RedirectToAction(nameof(Seguimiento), new { eventoId = form.EventoId });
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.Conflict => ConcurrencyMessage + " Lo que has escrito sigue aquí para que puedas copiarlo.",
            ApplicationFailureCode.InvalidInput =>
                "Revisa los datos: escribe el objetivo e indica una fecha prevista o un criterio de revisión. Hace falta una valoración médica guardada, y cada evento admite un solo seguimiento médico.",
            _ => result.Error.Message,
        });
        return View(new IniciarSeguimientoMedicoViewModel(detail, form));
    }

    /// <summary>MED-11: bandeja compartida de seguimientos médicos abiertos, vencidos incluidos.</summary>
    public async Task<IActionResult> Seguimientos(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Seguimientos)) });
        }

        var result = await service.ListMedicalFollowUpsAsync(
            new ListMedicalFollowUpsCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(Array.Empty<MedicalFollowUpSummary>());
        }

        return View(result.Value);
    }

    /// <summary>MED-11/MED-12: el seguimiento médico abierto de un evento, con sus acciones y los formularios
    /// para registrar una revisión, reprogramar y, al terminar el turno, transferir o conservar.</summary>
    public async Task<IActionResult> Seguimiento(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Seguimientos));
        }
        if (detail.Status != ClinicalEventStatus.EnSeguimientoMedico || detail.Medical.FollowUp is null)
        {
            return RedirectToAction(nameof(Escalado), new { eventoId });
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
            return RedirectToAction(nameof(Seguimientos));
        }
        if (detail.Medical.FollowUp is null)
        {
            return RedirectToAction(nameof(Escalado), new { eventoId = form.EventoId });
        }
        if (!ModelState.IsValid)
        {
            return View(new SeguimientoViewModel(detail, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await service.RecordMedicalFollowUpActionAsync(new RecordMedicalFollowUpActionCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Revision, form.Tipo,
            form.Texto, form.FechaPrevista, form.Criterio, form.EquipoEntrante, form.TransferenciaId), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = form.Tipo switch
            {
                FollowUpActionType.Reprogramacion => "Seguimiento reprogramado.",
                FollowUpActionType.Transferencia => "Transferencia registrada. El equipo entrante puede confirmar la recepción.",
                FollowUpActionType.Conservacion => "Queda registrado que conservas el seguimiento para tu próxima revisión.",
                FollowUpActionType.Recepcion => "Recepción confirmada.",
                _ => "Revisión registrada.",
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
                FollowUpActionType.Conservacion => "La nota es demasiado larga.",
                _ => "Escribe la revisión antes de registrarla.",
            },
            _ => result.Error.Message,
        });
        return View(new SeguimientoViewModel(detail, form));
    }

    /// <summary>MED-07: formulario de una indicación a Enfermería.</summary>
    public async Task<IActionResult> Indicacion(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Escalados));
        }
        if (!CanDecide(detail))
        {
            return RedirectToAction(nameof(Escalado), new { eventoId });
        }

        return View(new IndicacionViewModel(detail, new IndicacionFormModel { EventoId = detail.EventId, Revision = detail.Revision }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Indicacion([Bind(Prefix = "Form")] IndicacionFormModel form, CancellationToken ct)
    {
        var detail = await FindEventAsync(form.EventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Escalados));
        }
        if (!ModelState.IsValid)
        {
            return View(new IndicacionViewModel(detail, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await service.RegisterMedicalIndicationAsync(new RegisterMedicalIndicationCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Revision,
            form.Texto, form.FechaPrevista, form.Criterio, form.InformacionAdicional), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = detail.Status == ClinicalEventStatus.EnSeguimientoMedico
                ? "Indicación registrada y seguimiento médico terminado. Enfermería de la unidad la verá en su bandeja de indicaciones."
                : "Indicación registrada. Enfermería de la unidad la verá en su bandeja de indicaciones.";
            return RedirectToAction(nameof(Escalado), new { eventoId = form.EventoId });
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.Conflict => ConcurrencyMessage + " Lo que has escrito sigue aquí para que puedas copiarlo.",
            ApplicationFailureCode.InvalidInput =>
                "Revisa los datos: escribe la indicación e indica una fecha prevista o un criterio. Hace falta una valoración médica guardada.",
            _ => result.Error.Message,
        });
        return View(new IndicacionViewModel(detail, form));
    }

    /// <summary>MED-15 a MED-17: resumen de la valoración médica, indicaciones aún pendientes y decisión de
    /// comunicación familiar antes de cerrar. Mismos modelos que el cierre de Enfermería.</summary>
    public async Task<IActionResult> Cerrar(Guid eventoId, CancellationToken ct)
    {
        var detail = await FindEventAsync(eventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Escalados));
        }
        if (!CanClose(detail))
        {
            return RedirectToAction(nameof(Escalado), new { eventoId });
        }

        return View(new CerrarViewModel(detail, CerrarFormModel.For(detail)));
    }

    /// <summary>Cierre médico idempotente por OperacionId, sin segundo cierre de Enfermería. Las indicaciones
    /// pendientes siguen en la bandeja de Enfermería.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cerrar([Bind(Prefix = "Form")] CerrarFormModel form, CancellationToken ct)
    {
        var detail = await FindEventAsync(form.EventoId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Escalados));
        }
        if (!ModelState.IsValid)
        {
            return View(new CerrarViewModel(detail, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var preparar = form.Comunicacion == FamilyCommunicationDecision.Preparar;
        var result = await service.CloseMedicalEventAsync(new CloseMedicalEventCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.EventoId, form.Revision, form.OperacionId,
            form.Comunicacion, preparar ? form.TipoComunicacion : null, preparar ? form.TextoComunicacion : null), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = preparar
                ? "Evento cerrado. Enfermería no tiene que cerrarlo. La comunicación familiar queda pendiente de aprobación."
                : "Evento cerrado. Enfermería no tiene que cerrarlo.";
            return RedirectToAction(nameof(Escalado), new { eventoId = form.EventoId });
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.Conflict => ConcurrencyMessage + " Lo que has escrito sigue aquí para que puedas copiarlo.",
            ApplicationFailureCode.InvalidInput when detail.Referral is not null => ReferralDisplay.CloseMessage,
            ApplicationFailureCode.InvalidInput =>
                "Revisa los datos: decide si se comunica a la familia y, si preparas la comunicación, elige el tipo y escribe el texto. Para cerrar hace falta una valoración médica guardada.",
            _ => result.Error.Message,
        });
        return View(new CerrarViewModel(detail, form));
    }

    /// <summary>MED-08/MED-09: indicaciones emitidas con su lectura, realización e incidencias.</summary>
    public async Task<IActionResult> Indicaciones(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Indicaciones)) });
        }

        var result = await service.ListMedicalIndicationsAsync(
            new ListMedicalIndicationsCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(Array.Empty<MedicalIndicationListItem>());
        }

        return View(result.Value);
    }

    /// <summary>MED-19: residentes del ámbito de Medicina, con el mismo criterio que la lista de Enfermería.</summary>
    public async Task<IActionResult> Residentes(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Residentes)) });
        }

        var result = await service.ListScopeResidentsAsync(
            new ListScopeResidentsCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), SystemProfile.Medicina), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(Array.Empty<ScopeResidentSummary>());
        }

        return View(result.Value);
    }

    /// <summary>MED-20: ficha del residente con su basal vigente (solo lectura) y "registrar evento".</summary>
    public async Task<IActionResult> Residente(Guid residenteId, CancellationToken ct)
    {
        var resolved = await ResolveScopeResidentAsync(residenteId, ct);
        if (resolved is null)
        {
            return RedirectToAction(nameof(Residentes));
        }

        var baselineResult = await service.ReadCurrentBaselineAsync(new ReadCurrentBaselineCommand(
            resolved.Value.Scope.ProfileScopeId, CenterId.From(resolved.Value.Scope.CenterId), resolved.Value.Resident.ResidentId), ct);
        return View(new EnfermeriaResidentDetailViewModel(resolved.Value.Resident, baselineResult.Ok ? baselineResult.Value : null));
    }

    /// <summary>MED-22 (historia 9): eventos cerrados del residente que ve Medicina (escalados y propios), con el
    /// basal y la ubicación de su fecha (HIS-03), y las versiones firmadas del basal. Vista común con Enfermería.</summary>
    public async Task<IActionResult> Historial(Guid residenteId, CancellationToken ct)
    {
        var resolved = await ResolveScopeResidentAsync(residenteId, ct);
        if (resolved is null)
        {
            return RedirectToAction(nameof(Residentes));
        }

        var (scope, resident) = resolved.Value;
        var centroId = CenterId.From(scope.CenterId);
        var events = await service.ListClosedEventsAsync(
            new ListClosedEventsCommand(scope.ProfileScopeId, centroId, resident.ResidentId, SystemProfile.Medicina), ct);
        var baselines = await service.ReadBaselineHistoryAsync(
            new ReadBaselineHistoryCommand(scope.ProfileScopeId, centroId, resident.ResidentId), ct);
        return View("~/Views/Shared/Historial.cshtml", ResidentHistoryViewModel.From(resident, events, baselines, nameof(Escalado)));
    }

    /// <summary>MED-03/MED-24 (HIS-02): línea temporal completa del residente, bajo demanda y en solo lectura,
    /// dentro del ámbito; de los eventos, solo los que ve Medicina. Vista común con Enfermería.</summary>
    public async Task<IActionResult> LineaTemporal(Guid residenteId, CancellationToken ct)
    {
        var resolved = await ResolveScopeResidentAsync(residenteId, ct);
        if (resolved is null)
        {
            return RedirectToAction(nameof(Residentes));
        }

        var (scope, resident) = resolved.Value;
        var timeline = await service.ReadResidentTimelineAsync(new ReadResidentTimelineCommand(
            scope.ProfileScopeId, CenterId.From(scope.CenterId), resident.ResidentId, SystemProfile.Medicina), ct);
        return View("~/Views/Shared/LineaTemporal.cshtml",
            new ResidentTimelineViewModel(resident, timeline.Ok ? timeline.Value : null, nameof(Escalado)));
    }

    /// <summary>MED-18: evento propio de Medicina. Al guardar nace ya en valoración médica, iniciada por quien
    /// lo registra, y se continúa en el formulario de valoración.</summary>
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
            form.Observacion, form.Clasificacion, form.DatosClinicosPertinentes, form.OperacionId, SystemProfile.Medicina);
        var result = await service.RegisterClinicalEventAsync(command, ct);
        if (!result.Ok)
        {
            TempData["Error"] = result.Error!.Message;
            return RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId });
        }

        TempData["Mensaje"] = "Evento registrado. Completa tu valoración médica.";
        return RedirectToAction(nameof(Valoracion), new { eventoId = result.Value!.EventId });
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
        var result = await service.FindEscalationDetailAsync(
            new FindEscalationDetailCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), eventoId), ct);
        return result.Ok ? result.Value : null;
    }

    /// <summary>Compartido por Residente y RegistrarEvento: confirma que el residente está en el ámbito de
    /// Medicina (MED-19, mismo criterio que la lista), sin distinguir "no está en el ámbito" de "no existe".</summary>
    private async Task<(ActiveProfileScopeCookieValue Scope, ScopeResidentSummary Resident)?> ResolveScopeResidentAsync(
        Guid residenteId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null || residenteId == Guid.Empty)
        {
            return null;
        }
        var findResult = await service.FindScopeResidentAsync(new FindScopeResidentCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(residenteId), SystemProfile.Medicina), ct);
        return !findResult.Ok || findResult.Value is null ? null : (activeScope, findResult.Value);
    }
}
