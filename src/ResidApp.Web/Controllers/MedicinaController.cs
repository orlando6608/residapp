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
/// MED-10 a MED-12 (seguimiento médico, su bandeja y la continuidad entre turnos) y MED-15 a MED-17 (cierre
/// médico con la decisión de comunicación familiar).
/// El protocolo urgente llegará con su historia. Traduce a MedicinaApplicationService; la autorización y las
/// reglas de negocio no viven aquí.
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
            seguimientos.Ok ? seguimientos.Value!.Count(s => FollowUpDisplay.IsOverdue(s.DueDate)) : 0));
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
        return View(new MedicinaEscaladoViewModel(detail, baseline.Ok ? baseline.Value : null));
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
        if (!CanDecide(detail))
        {
            return RedirectToAction(nameof(Escalado), new { eventoId });
        }

        return View(new CerrarViewModel(detail, new CerrarFormModel
        {
            EventoId = detail.EventId, Revision = detail.Revision, OperacionId = Guid.NewGuid(),
        }));
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

    private const string ConcurrencyMessage =
        "Este evento ha cambiado desde que lo abriste (otro profesional, u otra pestaña o pulsación tuya). Recarga para ver la versión actual antes de continuar.";

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
}
