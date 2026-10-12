using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Portal Familiar, bloque 1 (script 0052): el familiar elige un residente al que tiene acceso (autorización Activa) y lee sus comunicados
/// publicados. Solo con el perfil activo FAMILIAR; cada lectura la comprueba el caso de uso contra SQL (vínculo y autorización), nunca la cookie.
/// Los textos que no cuentan nada del residente: un vínculo ajeno, suspendido o inexistente da el mismo «sin acceso».
/// </summary>
[Route("Familia")]
public sealed partial class PortalFamiliarController(
    FamilyPortalApplicationService service, FamilyAppointmentsApplicationService appointments, FamilyAppointmentRequestsApplicationService requests) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(bool elegir, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(Index)));
        }

        var result = await service.ListResidentsAsync(Query(scope), ct);
        if (!result.Ok)
        {
            return Failure(result.Error!);
        }

        return result.Value!.Count switch
        {
            0 => View("Estado", new PortalFamiliarEstadoViewModel(
                "No tienes ninguna autorización activa",
                "Ahora mismo no tienes acceso a la información de ningún residente. Si crees que es un error, contacta con la Administración del centro.")),
            1 when !elegir => RedirectToAction(nameof(Residente), new { linkId = result.Value[0].LinkId }),
            _ => View(result.Value),
        };
    }

    [HttpGet("Residente/{linkId:guid}")]
    public async Task<IActionResult> Residente(Guid linkId, string? tipo, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(Residente), new { linkId }));
        }

        var result = await service.ReadResidentAsync(Query(scope), linkId, ct);
        if (!result.Ok)
        {
            return Failure(result.Error!);
        }

        FamilyCommunicationType? type = tipo switch
        {
            "ordinaria" => FamilyCommunicationType.Ordinaria,
            "relevante" => FamilyCommunicationType.Relevante,
            _ => null,
        };
        var home = result.Value!;
        var communications = type is null ? home.Communications : home.Communications.Where(c => c.Type == type && c.Kind != FamilyPortalCommunicationKind.Withdrawn).ToList();
        return View(new PortalFamiliarResidenteViewModel(home, type, communications));
    }

    [HttpGet("Residente/{linkId:guid}/Comunicado/{comunicadoId:guid}")]
    public async Task<IActionResult> Comunicado(Guid linkId, Guid comunicadoId, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(Comunicado), new { linkId, comunicadoId }));
        }

        var result = await service.ReadCommunicationAsync(Query(scope), linkId, comunicadoId, ct);
        return result.Ok
            ? View(new PortalFamiliarComunicadoViewModel(result.Value!.Resident, result.Value.Communication, result.Value.Corrections))
            : Failure(result.Error!);
    }

    // ---- Citas (script 0054): reserva directa. ----

    [HttpGet("Residente/{linkId:guid}/Citas")]
    public async Task<IActionResult> Citas(Guid linkId, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(Citas), new { linkId }));
        }

        var result = await appointments.ReadHomeAsync(Query(scope), linkId, ct);
        if (!result.Ok)
        {
            return Failure(result.Error!);
        }

        var solicitudes = await requests.ListAsync(Query(scope), linkId, ct);
        return solicitudes.Ok ? View(new PortalFamiliarCitasViewModel(result.Value!, solicitudes.Value)) : Failure(solicitudes.Error!);
    }

    /// <summary>Elegir equipo y modalidad y ver los huecos libres; con <paramref name="reprogramar"/>, mover una cita propia (mismo equipo y modalidad).</summary>
    [HttpGet("Residente/{linkId:guid}/Citas/Nueva")]
    public async Task<IActionResult> NuevaCita(
        Guid linkId, AppointmentTeam? equipo, AppointmentModality? modalidad, Guid? reprogramar, string? aviso, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(NuevaCita), new { linkId }));
        }

        var home = await appointments.ReadHomeAsync(Query(scope), linkId, ct);
        if (!home.Ok)
        {
            return Failure(home.Error!);
        }

        var moving = reprogramar is { } id ? home.Value!.Appointments.FirstOrDefault(a => a.Id == id && a.CanReschedule) : null;
        if (reprogramar is not null && moving is null)
        {
            TempData["Error"] = "Esa cita ya no se puede reprogramar.";
            return RedirectToAction(nameof(Citas), new { linkId });
        }

        if (moving is not null)
        {
            equipo = moving.Team;
            modalidad = moving.Modality;
        }

        IReadOnlyList<AppointmentSlot>? slots = null;
        if (equipo is not null && modalidad is not null)
        {
            var listed = await appointments.ListSlotsAsync(Query(scope), linkId, equipo, modalidad, moving?.Id, ct);
            if (!listed.Ok)
            {
                return Failure(listed.Error!);
            }

            slots = listed.Value;
        }

        return View(new PortalFamiliarNuevaCitaViewModel(home.Value!.Resident, home.Value, equipo, modalidad, slots, moving, aviso));
    }

    /// <summary>Revisar el hueco elegido antes de confirmar (CIT-D02). Cada visita trae una operación nueva: la doble pulsación del botón de confirmar es
    /// la que se repite, y no duplica la cita.</summary>
    [HttpGet("Residente/{linkId:guid}/Citas/Revisar")]
    public async Task<IActionResult> RevisarCita(
        Guid linkId, AppointmentTeam? equipo, AppointmentModality? modalidad, DateTimeOffset? inicio, Guid? reprogramar, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(Citas), new { linkId }));
        }

        var listed = equipo is null || modalidad is null || inicio is null
            ? null
            : await appointments.ListSlotsAsync(Query(scope), linkId, equipo, modalidad, reprogramar, ct);
        if (listed is { Ok: false })
        {
            return Failure(listed.Error!);
        }

        var slot = listed?.Value!.FirstOrDefault(s => s.StartsAt.UtcDateTime == inicio!.Value.UtcDateTime);
        var home = await appointments.ReadHomeAsync(Query(scope), linkId, ct);
        if (!home.Ok)
        {
            return Failure(home.Error!);
        }

        if (slot is null)
        {
            return RedirectToAction(nameof(NuevaCita), new { linkId, equipo, modalidad, reprogramar, aviso = "Ese hueco ya no está libre. Elige otro." });
        }

        var moving = reprogramar is { } id ? home.Value!.Appointments.FirstOrDefault(a => a.Id == id) : null;
        return View(new PortalFamiliarConfirmarCitaViewModel(home.Value!.Resident, equipo!.Value, modalidad!.Value, slot, Guid.NewGuid(), moving));
    }

    [HttpPost("Residente/{linkId:guid}/Citas/Reservar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReservarCita(
        Guid linkId, AppointmentTeam? equipo, AppointmentModality? modalidad, DateTimeOffset? inicio, Guid operacionId, Guid? reprogramar, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(Citas), new { linkId }));
        }

        ApplicationResult<FamilyAppointment> result = reprogramar is { } appointmentId
            ? await appointments.RescheduleAsync(new RescheduleFamilyAppointmentCommand(scope.ProfileScopeId, CenterId.From(scope.CenterId), linkId, appointmentId, inicio), ct)
            : await appointments.BookAsync(new BookFamilyAppointmentCommand(scope.ProfileScopeId, CenterId.From(scope.CenterId), linkId, equipo, modalidad, inicio, operacionId), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = reprogramar is null ? "Cita confirmada." : "Cita reprogramada.";
            return RedirectToAction(nameof(Cita), new { linkId, citaId = result.Value!.Id });
        }

        if (result.Error!.Code is ApplicationFailureCode.Conflict or ApplicationFailureCode.InvalidInput)
        {
            // Si el hueco se ocupó justo antes de confirmar, no se crea la cita y se ofrecen los que quedan libres (CIT-D04).
            return RedirectToAction(nameof(NuevaCita), new
            {
                linkId, equipo, modalidad, reprogramar,
                aviso = "No se ha podido confirmar: el hueco ya no está libre, ya tienes una cita próxima con ese equipo o ya no se puede cambiar. Elige otro hueco o revisa tus citas.",
            });
        }

        return Failure(result.Error!);
    }

    [HttpGet("Residente/{linkId:guid}/Citas/{citaId:guid}")]
    public async Task<IActionResult> Cita(Guid linkId, Guid citaId, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(Cita), new { linkId, citaId }));
        }

        var appointment = await appointments.ReadAppointmentAsync(Query(scope), linkId, citaId, ct);
        var home = appointment.Ok ? await appointments.ReadHomeAsync(Query(scope), linkId, ct) : null;
        return appointment.Ok && home is { Ok: true }
            ? View(new PortalFamiliarCitaViewModel(home.Value!.Resident, appointment.Value!))
            : Failure((home is { Ok: false } ? home.Error : appointment.Error)!);
    }

    [HttpGet("Residente/{linkId:guid}/Citas/{citaId:guid}/Cancelar")]
    public async Task<IActionResult> ConfirmarCancelarCita(Guid linkId, Guid citaId, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(Cita), new { linkId, citaId }));
        }

        var appointment = await appointments.ReadAppointmentAsync(Query(scope), linkId, citaId, ct);
        var home = appointment.Ok ? await appointments.ReadHomeAsync(Query(scope), linkId, ct) : null;
        if (!appointment.Ok || home is not { Ok: true })
        {
            return Failure((home is { Ok: false } ? home.Error : appointment.Error)!);
        }

        if (!appointment.Value!.CanCancel)
        {
            TempData["Error"] = "Esta cita ya no se puede cancelar desde el portal. Llama por teléfono al centro.";
            return RedirectToAction(nameof(Cita), new { linkId, citaId });
        }

        return View(new PortalFamiliarCitaViewModel(home.Value!.Resident, appointment.Value));
    }

    [HttpPost("Residente/{linkId:guid}/Citas/{citaId:guid}/Cancelar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelarCita(Guid linkId, Guid citaId, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(Cita), new { linkId, citaId }));
        }

        var result = await appointments.CancelAsync(Query(scope), linkId, citaId, ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Cita cancelada. El hueco queda libre.";
            return RedirectToAction(nameof(Cita), new { linkId, citaId });
        }

        if (result.Error!.Code is ApplicationFailureCode.Conflict or ApplicationFailureCode.InvalidInput)
        {
            TempData["Error"] = "Esta cita ya no se puede cancelar desde el portal: ya no está confirmada o falta poco para su hora. Llama por teléfono al centro.";
            return RedirectToAction(nameof(Cita), new { linkId, citaId });
        }

        return Failure(result.Error);
    }

    private ActiveProfileScopeCookieValue? Scope() =>
        ActiveProfileScopeCookie.Read(Request) is { Profile: SystemProfile.Familiar } scope ? scope : null;

    /// <summary>Sin ámbito activo, a elegir uno. Con un ámbito que no es familiar no se redirige: ProfileScope/Select lo volvería a seleccionar y a devolver aquí, en bucle.</summary>
    private IActionResult ScopeRedirect(string? returnUrl)
    {
        if (ActiveProfileScopeCookie.Read(Request) is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl });
        }

        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View("Estado", new PortalFamiliarEstadoViewModel(
            "Esta sección es del Portal Familiar",
            "Has entrado con un perfil profesional y el Portal Familiar solo es para las cuentas de familiares. Si eres familiar, cambia de ámbito para entrar con tu cuenta.",
            OtroPerfil: true));
    }

    private static FamilyPortalQuery Query(ActiveProfileScopeCookieValue scope) => new(scope.ProfileScopeId, CenterId.From(scope.CenterId));

    private IActionResult Failure(ApplicationFailure error)
    {
        if (error.Code == ApplicationFailureCode.AccessDenied)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return View("Estado", new PortalFamiliarEstadoViewModel(
                "No tienes acceso a esto",
                "No puedes ver esta información. Vuelve a tus residentes e inténtalo desde ahí."));
        }

        Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return View("Estado", new PortalFamiliarEstadoViewModel(
            "No hemos podido cargar la información",
            "Ha habido un fallo técnico. Esto no significa que no haya novedades: inténtalo de nuevo en unos minutos.", PuedeReintentar: true));
    }
}
