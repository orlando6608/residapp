using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>Solicitudes de cita en modo solicitud previa (script 0057; historia 8 de Administración): Administración ve la bandeja, propone un hueco libre
/// y cierra con un motivo interno las que no puede atender. La cita nace cuando la familia acepta la propuesta.</summary>
public sealed partial class AdministracionController
{
    public async Task<IActionResult> SolicitudesCitas(bool todas, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(SolicitudesCitas)) });
        }

        var result = await appointmentRequests.ListAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), !todas, ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        return View(new SolicitudesCitasViewModel(result.Value ?? [], !todas, DateTimeOffset.UtcNow));
    }

    [HttpGet]
    public async Task<IActionResult> ProponerCita(Guid solicitudId, CancellationToken ct)
    {
        var request = await FindRequestAsync(solicitudId, ct);
        if (request is null || request.Status != AppointmentRequestStatus.Pendiente)
        {
            TempData["Error"] = "A esa solicitud no se le puede proponer un horario: ya no está pendiente o no es de este centro.";
            return RedirectToAction(nameof(SolicitudesCitas));
        }

        return View(new ProponerCitaViewModel(request, await ProposalSlotsAsync(solicitudId, ct), new ProponerCitaFormModel { SolicitudId = solicitudId }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProponerCita([Bind(Prefix = "Form")] ProponerCitaFormModel form, CancellationToken ct)
    {
        var request = await FindRequestAsync(form.SolicitudId, ct);
        if (request is null)
        {
            return RedirectToAction(nameof(SolicitudesCitas));
        }

        if (!ModelState.IsValid)
        {
            return View(new ProponerCitaViewModel(request, await ProposalSlotsAsync(form.SolicitudId, ct), form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await appointmentRequests.ProposeAsync(new ProposeAppointmentCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.SolicitudId, form.Inicio, form.Profesional), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Propuesta enviada. El hueco queda reservado hasta que la familia responda; la cita solo se confirma si la acepta.";
            return RedirectToAction(nameof(SolicitudesCitas));
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.Conflict => "Ese hueco ya no está libre o la solicitud ya no está pendiente. Elige otro hueco; lo que has escrito sigue aquí.",
            ApplicationFailureCode.InvalidInput => "Elige el hueco que propones; el profesional, si lo apuntas, puede tener hasta 100 caracteres.",
            _ => result.Error.Message,
        });
        return View(new ProponerCitaViewModel(request, await ProposalSlotsAsync(form.SolicitudId, ct), form));
    }

    [HttpGet]
    public async Task<IActionResult> CerrarSolicitudCita(Guid solicitudId, CancellationToken ct)
    {
        var request = await FindRequestAsync(solicitudId, ct);
        if (request is null || request.Status is not (AppointmentRequestStatus.Pendiente or AppointmentRequestStatus.Propuesta))
        {
            TempData["Error"] = "Esa solicitud no se puede cerrar: ya no está abierta o no es de este centro.";
            return RedirectToAction(nameof(SolicitudesCitas));
        }

        return View(new CerrarSolicitudViewModel(request, new CerrarSolicitudFormModel { SolicitudId = solicitudId }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CerrarSolicitudCita([Bind(Prefix = "Form")] CerrarSolicitudFormModel form, CancellationToken ct)
    {
        var request = await FindRequestAsync(form.SolicitudId, ct);
        if (request is null)
        {
            return RedirectToAction(nameof(SolicitudesCitas));
        }

        if (!ModelState.IsValid)
        {
            return View(new CerrarSolicitudViewModel(request, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await appointmentRequests.CloseAsync(new CloseAppointmentRequestCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.SolicitudId, form.Motivo), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Solicitud cerrada. La familia verá que el centro la ha cerrado, sin el motivo. Queda en la auditoría.";
            return RedirectToAction(nameof(SolicitudesCitas));
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.Conflict => "La solicitud ya no está abierta. Vuelve a la bandeja para ver su estado.",
            ApplicationFailureCode.InvalidInput => "Escribe el motivo del cierre, de hasta 500 caracteres.",
            _ => result.Error.Message,
        });
        return View(new CerrarSolicitudViewModel(request, form));
    }

    private async Task<AdministrationAppointmentRequest?> FindRequestAsync(Guid requestId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return null;
        }

        var result = await appointmentRequests.ReadAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), requestId, ct);
        return result.Ok ? result.Value : null;
    }

    private async Task<IReadOnlyList<AppointmentSlot>> ProposalSlotsAsync(Guid requestId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await appointmentRequests.ListProposalSlotsAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), requestId, ct);
        return result.Value ?? [];
    }
}
