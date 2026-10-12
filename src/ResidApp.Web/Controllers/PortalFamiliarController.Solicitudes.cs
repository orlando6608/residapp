using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;

namespace ResidApp.Web.Controllers;

/// <summary>Solicitudes de cita del Portal Familiar (script 0057): la familia pide una cita, y cuando el centro propone un horario lo acepta, pide otro o
/// cancela. Solo con el perfil activo FAMILIAR; cada operación la comprueba el caso de uso contra SQL. Sin chat y sin ningún profesional.</summary>
public sealed partial class PortalFamiliarController
{
    private const string CitasUnavailableRequest =
        "No se ha podido enviar la solicitud: revisa los datos, puede que ya tengas una solicitud abierta o una cita próxima con ese equipo, o que el centro ya no admita solicitudes.";

    [HttpGet("Residente/{linkId:guid}/Solicitudes/Nueva")]
    public async Task<IActionResult> NuevaSolicitud(Guid linkId, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(NuevaSolicitud), new { linkId }));
        }

        var home = await appointments.ReadHomeAsync(Query(scope), linkId, ct);
        if (!home.Ok)
        {
            return Failure(home.Error!);
        }

        if (!home.Value!.Settings.RequestsEnabled)
        {
            TempData["Error"] = "Ahora mismo el centro no admite solicitudes de cita desde el portal.";
            return RedirectToAction(nameof(Citas), new { linkId });
        }

        return View(new PortalFamiliarNuevaSolicitudViewModel(home.Value.Resident, home.Value, new SolicitudCitaFormModel()));
    }

    [HttpPost("Residente/{linkId:guid}/Solicitudes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EnviarSolicitud(Guid linkId, [Bind(Prefix = "Form")] SolicitudCitaFormModel form, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(Citas), new { linkId }));
        }

        var home = await appointments.ReadHomeAsync(Query(scope), linkId, ct);
        if (!home.Ok)
        {
            return Failure(home.Error!);
        }

        if (!ModelState.IsValid)
        {
            return View(nameof(NuevaSolicitud), new PortalFamiliarNuevaSolicitudViewModel(home.Value!.Resident, home.Value, form));
        }

        var result = await requests.CreateAsync(new CreateFamilyAppointmentRequestCommand(
            scope.ProfileScopeId, CenterId.From(scope.CenterId), linkId, form.Equipo, form.Modalidad, form.Motivo, form.Comentario, form.Dias, form.ParteDelDia,
            form.OperacionId), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Solicitud enviada. El centro te propondrá un día y una hora; hasta entonces no hay ninguna cita confirmada.";
            return RedirectToAction(nameof(Solicitud), new { linkId, solicitudId = result.Value!.Id });
        }

        if (result.Error!.Code is ApplicationFailureCode.Conflict or ApplicationFailureCode.InvalidInput)
        {
            return View(nameof(NuevaSolicitud), new PortalFamiliarNuevaSolicitudViewModel(home.Value!.Resident, home.Value, form, CitasUnavailableRequest));
        }

        return Failure(result.Error!);
    }

    [HttpGet("Residente/{linkId:guid}/Solicitudes/{solicitudId:guid}")]
    public async Task<IActionResult> Solicitud(Guid linkId, Guid solicitudId, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(Solicitud), new { linkId, solicitudId }));
        }

        var request = await requests.ReadAsync(Query(scope), linkId, solicitudId, ct);
        var home = request.Ok ? await appointments.ReadHomeAsync(Query(scope), linkId, ct) : null;
        return request.Ok && home is { Ok: true }
            ? View(new PortalFamiliarSolicitudViewModel(home.Value!.Resident, request.Value!))
            : Failure((home is { Ok: false } ? home.Error : request.Error)!);
    }

    /// <summary>Aceptar la propuesta crea la cita confirmada; repetirlo (doble pulsación) lleva a la misma cita.</summary>
    [HttpPost("Residente/{linkId:guid}/Solicitudes/{solicitudId:guid}/Aceptar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AceptarSolicitud(Guid linkId, Guid solicitudId, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(Solicitud), new { linkId, solicitudId }));
        }

        var result = await requests.AcceptAsync(Query(scope), linkId, solicitudId, ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Cita confirmada.";
            return result.Value!.AppointmentId is { } appointmentId
                ? RedirectToAction(nameof(Cita), new { linkId, citaId = appointmentId })
                : RedirectToAction(nameof(Solicitud), new { linkId, solicitudId });
        }

        if (result.Error!.Code is ApplicationFailureCode.Conflict or ApplicationFailureCode.InvalidInput)
        {
            TempData["Error"] = "No se ha podido aceptar: la propuesta ya no está vigente, el horario ya no está libre o ya tienes una cita próxima con ese equipo.";
            return RedirectToAction(nameof(Solicitud), new { linkId, solicitudId });
        }

        return Failure(result.Error!);
    }

    [HttpPost("Residente/{linkId:guid}/Solicitudes/{solicitudId:guid}/OtroHorario")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PedirOtroHorario(Guid linkId, Guid solicitudId, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(Solicitud), new { linkId, solicitudId }));
        }

        var result = await requests.RequestAnotherTimeAsync(Query(scope), linkId, solicitudId, ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Hemos avisado al centro de que necesitas otro horario. Te propondrá uno nuevo.";
            return RedirectToAction(nameof(Solicitud), new { linkId, solicitudId });
        }

        if (result.Error!.Code is ApplicationFailureCode.Conflict or ApplicationFailureCode.InvalidInput)
        {
            TempData["Error"] = "No se ha podido pedir otro horario: la solicitud ya no tiene una propuesta.";
            return RedirectToAction(nameof(Solicitud), new { linkId, solicitudId });
        }

        return Failure(result.Error!);
    }

    [HttpGet("Residente/{linkId:guid}/Solicitudes/{solicitudId:guid}/Cancelar")]
    public async Task<IActionResult> ConfirmarCancelarSolicitud(Guid linkId, Guid solicitudId, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(Solicitud), new { linkId, solicitudId }));
        }

        var request = await requests.ReadAsync(Query(scope), linkId, solicitudId, ct);
        var home = request.Ok ? await appointments.ReadHomeAsync(Query(scope), linkId, ct) : null;
        if (!request.Ok || home is not { Ok: true })
        {
            return Failure((home is { Ok: false } ? home.Error : request.Error)!);
        }

        if (!request.Value!.CanCancel)
        {
            TempData["Error"] = "Esta solicitud ya no se puede cancelar.";
            return RedirectToAction(nameof(Solicitud), new { linkId, solicitudId });
        }

        return View(new PortalFamiliarSolicitudViewModel(home.Value!.Resident, request.Value));
    }

    [HttpPost("Residente/{linkId:guid}/Solicitudes/{solicitudId:guid}/Cancelar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelarSolicitud(Guid linkId, Guid solicitudId, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(Solicitud), new { linkId, solicitudId }));
        }

        var result = await requests.CancelAsync(Query(scope), linkId, solicitudId, ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Solicitud cancelada.";
            return RedirectToAction(nameof(Solicitud), new { linkId, solicitudId });
        }

        if (result.Error!.Code is ApplicationFailureCode.Conflict or ApplicationFailureCode.InvalidInput)
        {
            TempData["Error"] = "Esta solicitud ya no se puede cancelar.";
            return RedirectToAction(nameof(Solicitud), new { linkId, solicitudId });
        }

        return Failure(result.Error!);
    }
}
