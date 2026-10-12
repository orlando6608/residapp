using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>Citas con reserva directa (script 0054; ADM-21 a ADM-26): Administración configura el servicio, las franjas y los bloqueos, y ve y gestiona
/// la agenda del centro. Las horas se escriben y se muestran en la hora del servidor.</summary>
public sealed partial class AdministracionController
{
    private static TimeZoneInfo CitasZone => TimeZoneInfo.Local;

    public async Task<IActionResult> Citas(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Citas)) });
        }

        var result = await appointmentService.ReadAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(new CitasViewModel(null, new CitasServicioFormModel(), new CitasFranjaFormModel(), new CitasBloqueoFormModel()));
        }

        return View(new CitasViewModel(result.Value, CitasServicioFormModel.From(result.Value!), new CitasFranjaFormModel { DuracionMinutos = 30 }, new CitasBloqueoFormModel()));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Citas([Bind(Prefix = "Servicio")] CitasServicioFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Citas)) });
        }

        var centerId = CenterId.From(activeScope.CenterId);
        if (ModelState.IsValid)
        {
            var result = await appointmentService.SaveSettingsAsync(new SaveAppointmentServiceCommand(
                activeScope.ProfileScopeId, centerId, form.Version, form.Modo, form.AntelacionHoras, form.HorizonteDias, form.ParsedModalities()), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Configuración del servicio de citas guardada. Se aplica a las reservas nuevas; las citas ya confirmadas se conservan.";
                return RedirectToAction(nameof(Citas));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => "Otra persona ha cambiado la configuración desde que abriste la pantalla. Recarga para ver la vigente; lo que has escrito sigue aquí.",
                ApplicationFailureCode.InvalidInput => "Revisa los datos: la antelación va de 0 a 720 horas, el horizonte de 1 a 180 días, y la solicitud previa todavía no está disponible.",
                ApplicationFailureCode.AccessDenied => "No tienes acceso a esta pantalla.",
                _ => result.Error.Message,
            });
        }

        var view = await appointmentService.ReadAsync(activeScope.ProfileScopeId, centerId, ct);
        return View(new CitasViewModel(view.Ok ? view.Value : null, form, new CitasFranjaFormModel { DuracionMinutos = 30 }, new CitasBloqueoFormModel()));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CitasFranja([Bind(Prefix = "Franja")] CitasFranjaFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Revisa la franja: " + string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Citas));
        }

        var result = await appointmentService.AddWindowAsync(new AddAppointmentWindowCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.Equipo, form.DiaSemana, form.Inicio, form.Fin, form.DuracionMinutos), ct);
        return Outcome(result, "Franja añadida. Los huecos salen de las franjas cada vez que la familia los consulta.", result.Error?.Code switch
        {
            ApplicationFailureCode.Conflict => "La franja se solapa con otra del mismo equipo y día.",
            ApplicationFailureCode.InvalidInput => "Revisa la franja: la hora de fin debe ser posterior a la de inicio, la duración de 5 a 240 minutos y debe caber al menos una cita.",
            _ => null,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CitasFranjaRetirar(Guid id, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await appointmentService.RetireWindowAsync(new AppointmentTargetCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), id), ct);
        return Outcome(result, "Franja retirada. Las citas ya confirmadas se conservan.", result.Error?.Code == ApplicationFailureCode.AccessDenied ? "Esa franja ya no existe." : null);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CitasBloqueo([Bind(Prefix = "Bloqueo")] CitasBloqueoFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Revisa el bloqueo: " + string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Citas));
        }

        var result = await appointmentService.AddBlockAsync(new AddAppointmentBlockCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.Equipo, form.Modalidad, LocalToOffset(form.Desde), LocalToOffset(form.Hasta), form.Motivo), ct);
        return Outcome(result, "Bloqueo añadido. Los huecos de ese rato dejan de ofrecerse; las citas ya confirmadas no se tocan.",
            result.Error?.Code == ApplicationFailureCode.InvalidInput ? "Revisa el bloqueo: el fin debe ser posterior al inicio y el motivo no puede estar vacío." : null);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CitasBloqueoRetirar(Guid id, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await appointmentService.RetireBlockAsync(new AppointmentTargetCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), id), ct);
        return Outcome(result, "Bloqueo retirado. Los huecos vuelven a ofrecerse.", result.Error?.Code == ApplicationFailureCode.AccessDenied ? "Ese bloqueo ya no existe." : null);
    }

    public async Task<IActionResult> AgendaCitas(DateOnly? semana, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(AgendaCitas)) });
        }

        var monday = MondayOf(semana ?? DateOnly.FromDateTime(DateTime.Today));
        var from = LocalToOffset(monday.ToDateTime(TimeOnly.MinValue))!.Value;
        var result = await appointmentService.ListAgendaAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), from, from.AddDays(7), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        return View(new AgendaCitasViewModel(monday, result.Value ?? [], DateTimeOffset.UtcNow));
    }

    [HttpGet]
    public async Task<IActionResult> CancelarCita(Guid citaId, CancellationToken ct)
    {
        var appointment = await FindAppointmentAsync(citaId, ct);
        if (appointment is null || appointment.Status != AppointmentStatus.Confirmada)
        {
            TempData["Error"] = "Esa cita no se puede cancelar: ya no está confirmada o no es de este centro.";
            return RedirectToAction(nameof(AgendaCitas));
        }

        return View(new CancelarCitaViewModel(appointment, new CancelarCitaFormModel { CitaId = citaId }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelarCita([Bind(Prefix = "Form")] CancelarCitaFormModel form, CancellationToken ct)
    {
        var appointment = await FindAppointmentAsync(form.CitaId, ct);
        if (appointment is null)
        {
            return RedirectToAction(nameof(AgendaCitas));
        }
        if (!ModelState.IsValid)
        {
            return View(new CancelarCitaViewModel(appointment, form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await appointmentService.CancelAsync(new CancelAppointmentCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.CitaId, form.Motivo), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Cita cancelada. La familia verá que el centro la ha cancelado, sin el motivo. Queda en la auditoría.";
            return RedirectToAction(nameof(AgendaCitas), new { semana = WeekOf(appointment) });
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.Conflict => "La cita ya no está confirmada. Vuelve a la agenda para ver su estado.",
            ApplicationFailureCode.InvalidInput => "Escribe el motivo de la cancelación, de hasta 500 caracteres.",
            _ => result.Error.Message,
        });
        return View(new CancelarCitaViewModel(appointment, form));
    }

    [HttpGet]
    public async Task<IActionResult> ReprogramarCita(Guid citaId, CancellationToken ct)
    {
        var appointment = await FindAppointmentAsync(citaId, ct);
        if (appointment is null || appointment.Status != AppointmentStatus.Confirmada)
        {
            TempData["Error"] = "Esa cita no se puede reprogramar: ya no está confirmada o no es de este centro.";
            return RedirectToAction(nameof(AgendaCitas));
        }

        return View(new ReprogramarCitaViewModel(appointment, await RescheduleSlotsAsync(citaId, ct), new ReprogramarCitaFormModel { CitaId = citaId }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReprogramarCita([Bind(Prefix = "Form")] ReprogramarCitaFormModel form, CancellationToken ct)
    {
        var appointment = await FindAppointmentAsync(form.CitaId, ct);
        if (appointment is null)
        {
            return RedirectToAction(nameof(AgendaCitas));
        }
        if (!ModelState.IsValid)
        {
            return View(new ReprogramarCitaViewModel(appointment, await RescheduleSlotsAsync(form.CitaId, ct), form));
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await appointmentService.RescheduleAsync(new RescheduleAppointmentCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.CitaId, form.NuevoInicio, form.Motivo), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Cita reprogramada. La familia verá la hora nueva y que la cambió el centro, sin el motivo. Queda en la auditoría.";
            return RedirectToAction(nameof(AgendaCitas), new { semana = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(form.NuevoInicio!.Value, CitasZone).DateTime) });
        }

        ModelState.AddModelError(string.Empty, result.Error!.Code switch
        {
            ApplicationFailureCode.Conflict => "Ese hueco ya no está libre o la cita ya no está confirmada. Elige otro hueco; lo que has escrito sigue aquí.",
            ApplicationFailureCode.InvalidInput => "Elige el nuevo hueco y escribe el motivo, de hasta 500 caracteres.",
            _ => result.Error.Message,
        });
        return View(new ReprogramarCitaViewModel(appointment, await RescheduleSlotsAsync(form.CitaId, ct), form));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FinalizarCita(Guid citaId, DateOnly? semana, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await appointmentService.FinishAsync(new AppointmentTargetCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), citaId), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Cita finalizada.";
        }
        else
        {
            TempData["Error"] = result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => "Solo se puede finalizar una cita confirmada cuya hora de inicio ya ha pasado.",
                ApplicationFailureCode.AccessDenied => "No se puede acceder a esa cita.",
                _ => result.Error.Message,
            };
        }

        return RedirectToAction(nameof(AgendaCitas), new { semana });
    }

    private IActionResult Outcome(ApplicationResult<bool> result, string success, string? failure)
    {
        if (result.Ok)
        {
            TempData["Mensaje"] = success;
        }
        else
        {
            TempData["Error"] = failure ?? result.Error!.Message;
        }

        return RedirectToAction(nameof(Citas));
    }

    /// <summary>Una cita del centro por su identificador, buscada en el año anterior y el siguiente; null si no es de este centro.</summary>
    private async Task<AdministrationAppointment?> FindAppointmentAsync(Guid appointmentId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return null;
        }

        var result = await appointmentService.ListAgendaAsync(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow.AddYears(1), ct);
        return result.Ok ? result.Value!.FirstOrDefault(a => a.Id == appointmentId) : null;
    }

    private async Task<IReadOnlyList<AppointmentSlot>> RescheduleSlotsAsync(Guid appointmentId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request)!;
        var result = await appointmentService.ListRescheduleSlotsAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), appointmentId, ct);
        return result.Ok ? result.Value! : [];
    }

    private static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    private static DateOnly WeekOf(AdministrationAppointment appointment) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(appointment.StartsAt, CitasZone).DateTime);

    /// <summary>Una fecha y hora escritas en la hora local del servidor, como instante. Las horas que no existen (cambio de hora) se desplazan una hora.</summary>
    private static DateTimeOffset? LocalToOffset(DateTime? local)
    {
        if (local is not { } value)
        {
            return null;
        }

        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        if (CitasZone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddHours(1);
        }

        return new DateTimeOffset(unspecified, CitasZone.GetUtcOffset(unspecified));
    }
}
