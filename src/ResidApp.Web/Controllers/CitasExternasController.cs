using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Citas externas de los residentes, pantallas del personal (script 0101; docs/producto/citas-externas-diseno.md, PR 2). Una sola ruta para Administración, Enfermería y
/// Medicina: Administración registra y cambia siempre; Enfermería solo con el permiso EXTERNAL_APPOINTMENT_MANAGE; Medicina solo ve. Traduce a
/// ExternalAppointmentsApplicationService; la autorización (ámbito, perfil, permiso y unidades) no vive aquí: la cookie solo orienta y cada caso de uso vuelve a comprobar. Los mensajes no
/// repiten nunca lo escrito en la cita (lugar, qué preparar, motivos).
/// </summary>
public sealed class CitasExternasController(
    ExternalAppointmentsApplicationService appointments, AdministracionApplicationService administration,
    EnfermeriaApplicationService nursing, MedicinaApplicationService medicine) : Controller
{
    private const int DefaultDays = 60;
    private const int MaxDays = 366;

    private const string InvalidMessage =
        "Revisa la cita: el día no puede ser anterior a hoy, elige el tipo, el transporte, el acompañante y qué ve la familia, el lugar y «qué preparar» son una sola línea corta, " +
        "y si cambias el día, o una hora que ya estaba puesta, tienes que elegir un motivo.";

    private const string NursePermissionMessage = "Para registrar o cambiar citas externas hace falta el permiso «Registrar y cambiar las citas externas de los residentes»: pídeselo a Administración.";

    private const string DoctorMessage = "Aquí solo puedes ver las citas. Las registra Administración.";

    /// <summary>Por qué no puede cambiar: Medicina nunca podrá (solo ve); Enfermería, hasta que Administración le dé el permiso.</summary>
    private static string NoPermission(ActiveProfileScopeCookieValue scope) => scope.Profile == SystemProfile.Medicina ? DoctorMessage : NursePermissionMessage;

    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? desde, DateOnly? hasta, Guid? residenteId, CancellationToken ct)
    {
        // Un valor mal formado en la URL se ignora (queda el filtro por defecto), no se muestra como error.
        ModelState.Clear();
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        var today = appointments.TodayAtCenter();
        // Un día muy lejano en la URL (válido pero extremo) se acerca al límite: no debe desbordar el calendario.
        DateOnly Limit(DateOnly day) => day < today.AddYears(-5) ? today.AddYears(-5) : day > today.AddYears(5) ? today.AddYears(5) : day;
        var from = Limit(desde ?? today);
        var to = Limit(hasta ?? from.AddDays(DefaultDays));
        if (to < from)
        {
            to = from;
        }
        if (to > from.AddDays(MaxDays))
        {
            to = from.AddDays(MaxDays);
        }

        var result = await appointments.ListAsync(scope.ProfileScopeId, CenterId.From(scope.CenterId), from, to, residenteId is { } id && id != Guid.Empty ? ResidentId.From(id) : null, ct);
        if (!result.Ok)
        {
            TempData["Error"] = result.Error!.Message;
            return RedirectToAction("Index", "Home");
        }

        string? residentName = null;
        if (residenteId is { } filtered && filtered != Guid.Empty)
        {
            residentName = (await ResidentsAsync(scope, ct)).FirstOrDefault(r => r.Id == filtered)?.Name;
        }
        return View(new ExternalAppointmentListViewModel(result.Value!.Appointments, result.Value.CanManage, from, to, today, scope.Profile, residenteId, residentName));
    }

    [HttpGet]
    public async Task<IActionResult> Ver(Guid id, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        var loaded = await LoadAsync(scope, id, ct);
        if (loaded is null)
        {
            return RedirectToAction(nameof(Index));
        }

        return View(new ExternalAppointmentDetailViewModel(loaded.Value.Record, loaded.Value.CanManage, appointments.TodayAtCenter()));
    }

    [HttpGet]
    public async Task<IActionResult> Nueva(Guid? residenteId, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        if (!await CanManageAsync(scope, ct))
        {
            TempData["Error"] = NoPermission(scope);
            return RedirectToAction(nameof(Index));
        }

        var residents = await ResidentsAsync(scope, ct);
        var chosen = residenteId is { } id ? residents.FirstOrDefault(r => r.Id == id) : null;
        var form = ExternalAppointmentFormModel.ForNew(chosen?.Id ?? Guid.Empty, appointments.TodayAtCenter());
        return View("Formulario", new ExternalAppointmentFormViewModel(true, form, chosen?.Name, chosen is null ? residents : null, null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Nueva([Bind(Prefix = "Form")] ExternalAppointmentFormModel form, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        if (!await CanManageAsync(scope, ct))
        {
            TempData["Error"] = NoPermission(scope);
            return RedirectToAction(nameof(Index));
        }

        ExternalAppointment appointment;
        try
        {
            appointment = form.ToAppointment();
            if (form.ResidenteId == Guid.Empty)
            {
                throw new DomainValidationException(ExternalAppointment.InvalidCode);
            }
        }
        catch (DomainValidationException)
        {
            return await NewFormAsync(scope, form, InvalidMessage, ct);
        }

        var result = await appointments.RegisterAsync(
            new RegisterExternalAppointmentCommand(scope.ProfileScopeId, CenterId.From(scope.CenterId), ResidentId.From(form.ResidenteId), appointment), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Cita externa registrada.";
            return RedirectToAction(nameof(Ver), new { id = result.Value });
        }

        return result.Error!.Code == ApplicationFailureCode.InvalidInput
            ? await NewFormAsync(scope, form, InvalidMessage, ct)
            : await NewFormAsync(scope, form, result.Error.Message, ct);
    }

    [HttpGet]
    public async Task<IActionResult> Editar(Guid id, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        var loaded = await LoadAsync(scope, id, ct);
        if (loaded is null)
        {
            return RedirectToAction(nameof(Index));
        }

        if (!loaded.Value.CanManage)
        {
            TempData["Error"] = NoPermission(scope);
            return RedirectToAction(nameof(Ver), new { id });
        }

        if (!ExternalAppointmentRules.IsOpen(loaded.Value.Record.Status))
        {
            TempData["Error"] = ClosedMessage;
            return RedirectToAction(nameof(Ver), new { id });
        }

        return View("Formulario", new ExternalAppointmentFormViewModel(
            false, ExternalAppointmentFormModel.From(loaded.Value.Record), loaded.Value.Record.ResidentName, null, null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar([Bind(Prefix = "Form")] ExternalAppointmentFormModel form, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        var loaded = await LoadAsync(scope, form.CitaId, ct);
        if (loaded is null)
        {
            return RedirectToAction(nameof(Index));
        }

        if (!loaded.Value.CanManage)
        {
            TempData["Error"] = NoPermission(scope);
            return RedirectToAction(nameof(Ver), new { id = form.CitaId });
        }

        ExternalAppointment appointment;
        try
        {
            appointment = form.ToAppointment();
        }
        catch (DomainValidationException)
        {
            return EditForm(loaded.Value.Record, form, InvalidMessage);
        }

        var result = await appointments.UpdateAsync(new UpdateExternalAppointmentCommand(
            scope.ProfileScopeId, CenterId.From(scope.CenterId), form.CitaId, form.VersionVista, appointment, form.ReasonOrNull(), form.MotivoLinea), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Cita externa guardada.";
            return RedirectToAction(nameof(Ver), new { id = form.CitaId });
        }

        switch (result.Error!.Code)
        {
            case ApplicationFailureCode.InvalidInput:
                return EditForm(loaded.Value.Record, form, InvalidMessage);
            default:
                // Otra persona guardó antes, la cita ya está cerrada o se perdió el acceso: se vuelve a la cita para ver cómo está ahora.
                TempData["Error"] = result.Error.Message;
                return RedirectToAction(nameof(Ver), new { id = form.CitaId });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Estado(Guid id, int version, string? estado, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        var parsed = EnumCode.TryParseCode<ExternalAppointmentStatus>(estado ?? "", out var status) ? status : (ExternalAppointmentStatus?)null;
        var result = await appointments.ChangeStatusAsync(new ChangeExternalAppointmentStatusCommand(scope.ProfileScopeId, CenterId.From(scope.CenterId), id, version, parsed), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Estado de la cita guardado.";
        }
        else
        {
            TempData["Error"] = result.Error!.Code == ApplicationFailureCode.AccessDenied && !await CanManageAsync(scope, ct) ? NoPermission(scope) : result.Error.Message;
        }

        return RedirectToAction(nameof(Ver), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        var loaded = await LoadAsync(scope, id, ct);
        if (loaded is null)
        {
            return RedirectToAction(nameof(Index));
        }

        if (!loaded.Value.CanManage || !ExternalAppointmentRules.IsOpen(loaded.Value.Record.Status))
        {
            TempData["Error"] = loaded.Value.CanManage ? ClosedMessage : NoPermission(scope);
            return RedirectToAction(nameof(Ver), new { id });
        }

        return View(new ExternalAppointmentCancelViewModel(loaded.Value.Record, new ExternalAppointmentFormModel { CitaId = id, VersionVista = loaded.Value.Record.Version }, null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancelar([Bind(Prefix = "Form")] ExternalAppointmentFormModel form, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        if (!await CanManageAsync(scope, ct))
        {
            // Quien solo ve no recibe el formulario de cancelar, ni siquiera con un envío sin motivo.
            TempData["Error"] = NoPermission(scope);
            return RedirectToAction(nameof(Ver), new { id = form.CitaId });
        }

        var result = await appointments.CancelAsync(new CancelExternalAppointmentCommand(
            scope.ProfileScopeId, CenterId.From(scope.CenterId), form.CitaId, form.VersionVista, form.ReasonOrNull(), form.MotivoLinea), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Cita externa cancelada.";
            return RedirectToAction(nameof(Ver), new { id = form.CitaId });
        }

        if (result.Error!.Code == ApplicationFailureCode.InvalidInput)
        {
            var loaded = await LoadAsync(scope, form.CitaId, ct);
            return loaded is null
                ? RedirectToAction(nameof(Index))
                : View(new ExternalAppointmentCancelViewModel(loaded.Value.Record, form, "Elige el motivo de la cancelación. La línea de al lado es opcional, de una sola línea y sin diagnósticos ni nombres."));
        }

        TempData["Error"] = result.Error.Code == ApplicationFailureCode.AccessDenied && !await CanManageAsync(scope, ct) ? NoPermission(scope) : result.Error.Message;
        return RedirectToAction(nameof(Ver), new { id = form.CitaId });
    }

    private const string ClosedMessage = "La cita ya está cerrada (realizada, no se acudió o cancelada) y no se puede cambiar.";

    /// <summary>El ámbito activo, si es de uno de los tres perfiles con acceso. Solo orienta: la cookie no está firmada y cada caso de uso vuelve a autorizar.</summary>
    private ActiveProfileScopeCookieValue? Scope() =>
        ActiveProfileScopeCookie.Read(Request) is { } scope && scope.Profile is SystemProfile.Administracion or SystemProfile.Enfermeria or SystemProfile.Medicina ? scope : null;

    private async Task<bool> CanManageAsync(ActiveProfileScopeCookieValue scope, CancellationToken ct)
    {
        var result = await appointments.CanManageAsync(scope.ProfileScopeId, CenterId.From(scope.CenterId), ct);
        return result.Ok && result.Value;
    }

    private async Task<(Application.Ports.ExternalAppointmentRecord Record, bool CanManage)?> LoadAsync(ActiveProfileScopeCookieValue scope, Guid id, CancellationToken ct)
    {
        var found = await appointments.FindAsync(scope.ProfileScopeId, CenterId.From(scope.CenterId), id, ct);
        if (!found.Ok)
        {
            TempData["Error"] = found.Error!.Message;
            return null;
        }

        return (found.Value!, await CanManageAsync(scope, ct));
    }

    private IActionResult EditForm(Application.Ports.ExternalAppointmentRecord record, ExternalAppointmentFormModel form, string error) =>
        View("Formulario", new ExternalAppointmentFormViewModel(false, form, record.ResidentName, null, error));

    private async Task<IActionResult> NewFormAsync(ActiveProfileScopeCookieValue scope, ExternalAppointmentFormModel form, string error, CancellationToken ct)
    {
        var residents = await ResidentsAsync(scope, ct);
        var chosen = residents.FirstOrDefault(r => r.Id == form.ResidenteId);
        return View("Formulario", new ExternalAppointmentFormViewModel(true, form, chosen?.Name, chosen is null ? residents : null, error));
    }

    /// <summary>Los residentes del ámbito, para elegirlos al registrar y para poner nombre a una lista filtrada. Cada perfil los lee con su propio caso de uso.</summary>
    private async Task<IReadOnlyList<ExternalAppointmentResidentOption>> ResidentsAsync(ActiveProfileScopeCookieValue scope, CancellationToken ct)
    {
        var centerId = CenterId.From(scope.CenterId);
        if (scope.Profile == SystemProfile.Administracion)
        {
            var list = await administration.ListResidentsAsync(new AdministracionQuery(scope.ProfileScopeId, centerId), ct);
            return list.Ok ? list.Value!.Select(r => new ExternalAppointmentResidentOption(r.ResidentId.Value, r.DisplayName, r.LocationLabel)).OrderBy(r => r.Name).ToList() : [];
        }

        var command = new ListScopeResidentsCommand(scope.ProfileScopeId, centerId, scope.Profile);
        var residents = scope.Profile == SystemProfile.Enfermeria ? await nursing.ListScopeResidentsAsync(command, ct) : await medicine.ListScopeResidentsAsync(command, ct);
        return residents.Ok ? residents.Value!.Select(r => new ExternalAppointmentResidentOption(r.ResidentId.Value, r.DisplayName, r.LocationLabel)).OrderBy(r => r.Name).ToList() : [];
    }
}
