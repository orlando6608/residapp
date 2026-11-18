using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// La nota «al terminar» y «Registrar la siguiente cita» (script 0103; docs/producto/citas-externas-diseno.md, PR 4). La nota la escribe cualquier Enfermería con acceso al residente y solo la leen
/// Enfermería y Medicina; Administración no la lee y solo ve la siguiente cita prevista. Los mensajes no repiten nunca lo escrito en la nota. La autorización no vive aquí: cada caso de uso la comprueba.
/// </summary>
public sealed partial class CitasExternasController
{
    private const string NoteWriterMessage = "La nota «al terminar» la escribe Enfermería. Aquí no puedes anotarla.";

    private const string NoteNotRealizedMessage = "La nota «al terminar» se anota cuando la cita está realizada.";

    private const string NoteInvalidMessage =
        "Revisa la nota: elige qué cambia para el centro, escribe como mucho una línea corta sin saltos de línea, y si indicas la siguiente cita, que sea una fecha posterior a la cita " +
        "o un plazo de 1 a 36 meses.";

    private const string NoteConflictMessage = "Otra persona ha cambiado la nota mientras la escribías. Mira cómo está ahora y, si hace falta, vuelve a anotar.";

    private const string NextAppointmentClosedMessage = "La siguiente cita solo se registra desde una cita realizada o a la que no se acudió.";

    [HttpGet]
    public async Task<IActionResult> Anotar(Guid id, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        if (await LoadNoteContextAsync(scope, id, ct) is not { } context)
        {
            return RedirectToAction(nameof(Ver), new { id });
        }

        return View(new ExternalAppointmentNoteFormViewModel(context.Record, ExternalAppointmentNoteFormModel.For(id, context.Current), context.Current, null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Anotar([Bind(Prefix = "Form")] ExternalAppointmentNoteFormModel form, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        if (await LoadNoteContextAsync(scope, form.CitaId, ct) is not { } context)
        {
            return RedirectToAction(nameof(Ver), new { id = form.CitaId });
        }

        ExternalAppointmentNote note;
        try
        {
            note = form.ToNote();
        }
        catch (DomainValidationException)
        {
            return View(new ExternalAppointmentNoteFormViewModel(context.Record, form, context.Current, NoteInvalidMessage));
        }

        var result = await appointments.SaveNoteAsync(new SaveExternalAppointmentNoteCommand(
            scope.ProfileScopeId, CenterId.From(scope.CenterId), form.CitaId, form.VersionVista, note, form.OperacionId == Guid.Empty ? null : form.OperacionId), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Nota guardada.";
            return RedirectToAction(nameof(Ver), new { id = form.CitaId });
        }

        if (result.Error!.Code == ApplicationFailureCode.InvalidInput)
        {
            return View(new ExternalAppointmentNoteFormViewModel(context.Record, form, context.Current, NoteInvalidMessage));
        }

        TempData["Error"] = result.Error.Code == ApplicationFailureCode.Conflict ? NoteConflictMessage : result.Error.Message;
        return RedirectToAction(nameof(Ver), new { id = form.CitaId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetirarNota([Bind(Prefix = "Form")] ExternalAppointmentNoteFormModel form, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        if (await LoadNoteContextAsync(scope, form.CitaId, ct) is not { } context)
        {
            return RedirectToAction(nameof(Ver), new { id = form.CitaId });
        }

        var result = await appointments.RetractNoteAsync(new RetractExternalAppointmentNoteCommand(
            scope.ProfileScopeId, CenterId.From(scope.CenterId), form.CitaId, form.VersionVista, form.RetractionReasonOrNull()), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Nota retirada. Las versiones anteriores se conservan.";
            return RedirectToAction(nameof(Ver), new { id = form.CitaId });
        }

        if (result.Error!.Code == ApplicationFailureCode.InvalidInput)
        {
            return View("Anotar", new ExternalAppointmentNoteFormViewModel(context.Record, form, context.Current, "Elige el motivo para retirar la nota. Si ya no hay nota que retirar, vuelve a la cita."));
        }

        TempData["Error"] = result.Error.Code == ApplicationFailureCode.Conflict ? NoteConflictMessage : result.Error.Message;
        return RedirectToAction(nameof(Ver), new { id = form.CitaId });
    }

    [HttpGet]
    public async Task<IActionResult> Versiones(Guid id, CancellationToken ct)
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

        // Cada apertura de las versiones anteriores deja un evento de auditoría (sin contenido): solo Enfermería y Medicina, y solo las pide quien pulsa el enlace.
        var versions = await appointments.ListNoteVersionsAsync(scope.ProfileScopeId, CenterId.From(scope.CenterId), id, ct);
        if (!versions.Ok)
        {
            TempData["Error"] = versions.Error!.Message;
            return RedirectToAction(nameof(Ver), new { id });
        }

        return View(new ExternalAppointmentNoteVersionsViewModel(loaded.Value.Record, versions.Value!));
    }

    /// <summary>Un envío por POST (no una dirección con valores): el servidor lee la cita de origen y rellena el formulario de registrar con su tipo, especialidad, lugar y qué ve la familia, y la
    /// fecha prevista si la hay. No copia qué preparar, transporte ni acompañante (son de otra salida). No guarda nada hasta que se confirma el formulario.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SiguienteCita(Guid id, CancellationToken ct)
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

        var record = loaded.Value.Record;
        if (!loaded.Value.CanManage)
        {
            TempData["Error"] = NoPermission(scope);
            return RedirectToAction(nameof(Ver), new { id });
        }

        if (record.Status is not (ExternalAppointmentStatus.Realizada or ExternalAppointmentStatus.NoAcudida))
        {
            TempData["Error"] = NextAppointmentClosedMessage;
            return RedirectToAction(nameof(Ver), new { id });
        }

        var followUp = await appointments.FindFollowUpAsync(scope.ProfileScopeId, CenterId.From(scope.CenterId), id, ct);
        if (followUp.Ok && followUp.Value!.RegisteredId is not null)
        {
            TempData["Error"] = "Ya hay una siguiente cita registrada para esta.";
            return RedirectToAction(nameof(Ver), new { id });
        }

        var today = appointments.TodayAtCenter();
        var source = record.Appointment;
        var suggested = followUp.Ok ? followUp.Value!.Planned.Suggested(source.Date) : null;
        var form = ExternalAppointmentFormModel.ForNew(record.ResidentId.Value, suggested is { } day && day >= today ? day : today);
        form.CitaOrigenId = id;
        form.Tipo = source.Type.ToCode();
        form.Especialidad = source.Specialty?.ToCode();
        form.Lugar = source.Place;
        form.Visibilidad = source.FamilyVisibility.ToCode();
        return View("Formulario", new ExternalAppointmentFormViewModel(true, form, record.ResidentName, null, null, source.Date));
    }

    /// <summary>La cita y su nota vigente para escribir: solo Enfermería, y solo con la cita realizada. Si no, deja el motivo en TempData y devuelve null.</summary>
    private async Task<(Application.Ports.ExternalAppointmentRecord Record, Application.Ports.ExternalNoteVersion? Current)?> LoadNoteContextAsync(
        ActiveProfileScopeCookieValue scope, Guid id, CancellationToken ct)
    {
        if (scope.Profile != SystemProfile.Enfermeria)
        {
            TempData["Error"] = NoteWriterMessage;
            return null;
        }

        var loaded = await LoadAsync(scope, id, ct);
        if (loaded is null)
        {
            return null;
        }

        if (loaded.Value.Record.Status != ExternalAppointmentStatus.Realizada)
        {
            TempData["Error"] = NoteNotRealizedMessage;
            return null;
        }

        var current = await appointments.FindNoteAsync(scope.ProfileScopeId, CenterId.From(scope.CenterId), id, ct);
        if (!current.Ok)
        {
            TempData["Error"] = current.Error!.Message;
            return null;
        }

        return (loaded.Value.Record, current.Value);
    }
}
