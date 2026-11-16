using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Residents;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Ficha de antecedentes de salud, fase 1: alergias e intolerancias a medicamentos (script 0094; docs/producto/ficha-antecedentes-salud-diseno.md). Un formulario
/// común a Enfermería y Medicina, que se abre desde el bloque «Antecedentes de salud» de la ficha del residente. Traduce a HealthBackgroundApplicationService; la
/// autorización (ámbito, perfil y permiso de edición) no vive aquí. Los mensajes no repiten nunca lo escrito en la ficha.
/// </summary>
public sealed class FichaAntecedentesController(
    HealthBackgroundApplicationService health, EnfermeriaApplicationService nursing, MedicinaApplicationService medicine) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Alergias(Guid residenteId, CancellationToken ct)
    {
        var loaded = await LoadAsync(residenteId, ct);
        if (loaded is null)
        {
            return RedirectToAction("Index", "Home");
        }

        var (activeScope, resident, view) = loaded.Value;
        if (!view.CanEdit)
        {
            TempData["Error"] = "Para rellenar o cambiar la ficha hace falta el permiso «Rellenar y cambiar la ficha de antecedentes de salud»: pídeselo a Administración.";
            return BackToRecord(activeScope, residenteId);
        }

        var form = AllergyFormModel.From(residenteId, view.Allergies);
        if (view.UnreliableVersion is { } unreliable)
        {
            // La última versión guardada está incompleta: se parte de un formulario vacío y lo que se guarde será la versión siguiente, como corrección.
            form.VersionVista = unreliable;
            return View(new AllergyEditViewModel(resident, form,
                "La última versión guardada de esta ficha está incompleta y no es fiable. Vuelve a rellenar las alergias con lo que conste y guarda: se guardará como una versión nueva que la corrige.",
                RecordController(activeScope)));
        }
        if (TempData["FichaConflicto"] is string)
        {
            return View(new AllergyEditViewModel(resident, form,
                "Otra persona ha guardado esta ficha mientras la editabas. Aquí tienes la versión actual: revísala y, si hace falta, vuelve a hacer tu cambio.", RecordController(activeScope)));
        }
        return View(new AllergyEditViewModel(resident, form, null, RecordController(activeScope)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Alergias([Bind(Prefix = "Form")] AllergyFormModel form, CancellationToken ct)
    {
        var loaded = await LoadAsync(form.ResidenteId, ct);
        if (loaded is null)
        {
            return RedirectToAction("Index", "Home");
        }

        var (activeScope, resident, view) = loaded.Value;
        if (!view.CanEdit)
        {
            TempData["Error"] = "Para rellenar o cambiar la ficha hace falta el permiso «Rellenar y cambiar la ficha de antecedentes de salud»: pídeselo a Administración.";
            return BackToRecord(activeScope, form.ResidenteId);
        }

        AllergyRecordContent content;
        try
        {
            content = form.ToContent();
        }
        catch (DomainValidationException)
        {
            return View(new AllergyEditViewModel(resident, form, InvalidMessage, RecordController(activeScope)));
        }

        var result = await health.SaveAllergiesAsync(new SaveAllergiesCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(form.ResidenteId), form.VersionVista, form.ReasonOrNull(), content), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Ficha de alergias guardada.";
            return BackToRecord(activeScope, form.ResidenteId);
        }

        switch (result.Error!.Code)
        {
            case ApplicationFailureCode.Conflict:
                TempData["FichaConflicto"] = "1";
                return RedirectToAction(nameof(Alergias), new { residenteId = form.ResidenteId });
            case ApplicationFailureCode.InvalidInput:
                return View(new AllergyEditViewModel(resident, form, InvalidMessage, RecordController(activeScope)));
            default:
                TempData["Error"] = result.Error.Message;
                return BackToRecord(activeScope, form.ResidenteId);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Enfermedades(Guid residenteId, CancellationToken ct)
    {
        var loaded = await LoadAsync(residenteId, ct);
        if (loaded is null)
        {
            return RedirectToAction("Index", "Home");
        }

        var (activeScope, resident, view) = loaded.Value;
        if (!view.CanEdit)
        {
            TempData["Error"] = NoPermissionMessage;
            return BackToRecord(activeScope, residenteId);
        }

        var form = ConditionFormModel.From(residenteId, view.Conditions);
        if (view.UnreliableConditionsVersion is { } unreliable)
        {
            form.VersionVista = unreliable;
            return View(new ConditionEditViewModel(resident, form,
                "La última versión guardada de esta ficha está incompleta y no es fiable. Vuelve a rellenar las enfermedades e intervenciones con lo que conste y guarda: se guardará como una versión nueva que la corrige.",
                RecordController(activeScope)));
        }
        if (TempData["FichaConflicto"] is string)
        {
            return View(new ConditionEditViewModel(resident, form,
                "Otra persona ha guardado esta ficha mientras la editabas. Aquí tienes la versión actual: revísala y, si hace falta, vuelve a hacer tu cambio.", RecordController(activeScope)));
        }
        return View(new ConditionEditViewModel(resident, form, null, RecordController(activeScope)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enfermedades([Bind(Prefix = "Form")] ConditionFormModel form, CancellationToken ct)
    {
        var loaded = await LoadAsync(form.ResidenteId, ct);
        if (loaded is null)
        {
            return RedirectToAction("Index", "Home");
        }

        var (activeScope, resident, view) = loaded.Value;
        if (!view.CanEdit)
        {
            TempData["Error"] = NoPermissionMessage;
            return BackToRecord(activeScope, form.ResidenteId);
        }

        ConditionRecordContent content;
        try
        {
            content = form.ToContent();
        }
        catch (DomainValidationException)
        {
            return View(new ConditionEditViewModel(resident, form, ConditionsInvalidMessage, RecordController(activeScope)));
        }

        var result = await health.SaveConditionsAsync(new SaveConditionsCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(form.ResidenteId), form.VersionVista, form.ReasonOrNull(), content), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Ficha de enfermedades e intervenciones guardada.";
            return BackToRecord(activeScope, form.ResidenteId);
        }

        switch (result.Error!.Code)
        {
            case ApplicationFailureCode.Conflict:
                TempData["FichaConflicto"] = "1";
                return RedirectToAction(nameof(Enfermedades), new { residenteId = form.ResidenteId });
            case ApplicationFailureCode.InvalidInput:
                return View(new ConditionEditViewModel(resident, form, ConditionsInvalidMessage, RecordController(activeScope)));
            default:
                TempData["Error"] = result.Error.Message;
                return BackToRecord(activeScope, form.ResidenteId);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Medicacion(Guid residenteId, CancellationToken ct)
    {
        var loaded = await LoadAsync(residenteId, ct);
        if (loaded is null)
        {
            return RedirectToAction("Index", "Home");
        }

        var (activeScope, resident, view) = loaded.Value;
        if (!view.CanEdit)
        {
            TempData["Error"] = NoPermissionMessage;
            return BackToRecord(activeScope, residenteId);
        }

        var form = MedicationFormModel.From(residenteId, view.Medication);
        if (view.UnreliableMedicationVersion is { } unreliable)
        {
            form.VersionVista = unreliable;
            return View(new MedicationEditViewModel(resident, form,
                "La última versión guardada de esta ficha está incompleta y no es fiable. Vuelve a rellenar la medicación habitual con lo que conste en la receta o en la hoja de tratamiento y guarda: se guardará como una versión nueva que la corrige.",
                RecordController(activeScope), MedicationLastModified(view)));
        }
        if (TempData["FichaConflicto"] is string)
        {
            return View(new MedicationEditViewModel(resident, form,
                "Otra persona ha guardado esta ficha mientras la editabas. Aquí tienes la versión actual: revísala y, si hace falta, vuelve a hacer tu cambio.", RecordController(activeScope), MedicationLastModified(view)));
        }
        return View(new MedicationEditViewModel(resident, form, null, RecordController(activeScope), MedicationLastModified(view)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Medicacion([Bind(Prefix = "Form")] MedicationFormModel form, CancellationToken ct)
    {
        var loaded = await LoadAsync(form.ResidenteId, ct);
        if (loaded is null)
        {
            return RedirectToAction("Index", "Home");
        }

        var (activeScope, resident, view) = loaded.Value;
        if (!view.CanEdit)
        {
            TempData["Error"] = NoPermissionMessage;
            return BackToRecord(activeScope, form.ResidenteId);
        }

        MedicationRecordContent content;
        try
        {
            content = form.ToContent();
        }
        catch (DomainValidationException)
        {
            return View(new MedicationEditViewModel(resident, form, MedicationInvalidMessage, RecordController(activeScope), MedicationLastModified(view)));
        }

        var result = await health.SaveMedicationAsync(new SaveMedicationCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(form.ResidenteId), form.VersionVista, form.ReasonOrNull(), content), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = "Ficha de medicación habitual guardada.";
            return BackToRecord(activeScope, form.ResidenteId);
        }

        switch (result.Error!.Code)
        {
            case ApplicationFailureCode.Conflict:
                TempData["FichaConflicto"] = "1";
                return RedirectToAction(nameof(Medicacion), new { residenteId = form.ResidenteId });
            case ApplicationFailureCode.InvalidInput:
                return View(new MedicationEditViewModel(resident, form, MedicationInvalidMessage, RecordController(activeScope), MedicationLastModified(view)));
            default:
                TempData["Error"] = result.Error.Message;
                return BackToRecord(activeScope, form.ResidenteId);
        }
    }

    private static string? MedicationLastModified(HealthBackgroundView view) =>
        view.Medication is { } record ? HealthBackgroundDisplay.MedicationLastModified(record) : null;

    private const string MedicationInvalidMessage =
        "Revisa la ficha: elige una opción; si toma medicación, escribe al menos un medicamento con su dosis y su pauta (o «no consta») y no marques «No refiere medicación habitual»; " +
        "si no es el primer registro, indica si ha cambiado algo o corriges un error; y cada texto tiene que ser corto, de una sola línea y sin repetirse.";

    private const string NoPermissionMessage =
        "Para rellenar o cambiar la ficha hace falta el permiso «Rellenar y cambiar la ficha de antecedentes de salud»: pídeselo a Administración.";

    private const string ConditionsInvalidMessage =
        "Revisa la ficha: elige una opción; si tiene enfermedades o intervenciones, escribe al menos una y no marques «sin enfermedades»; el año, si lo pones, es un número de cuatro cifras que no sea futuro; " +
        "si no es el primer registro, indica si ha cambiado algo o corriges un error; y cada texto tiene que ser corto, de una sola línea y sin repetirse.";

    private const string InvalidMessage =
        "Revisa la ficha: elige una opción; si tiene alergias, escribe al menos una; la intolerancia necesita su motivo; si no es el primer registro, indica si ha cambiado algo o corriges un error; " +
        "y cada texto tiene que ser corto, de una sola línea y sin repetirse.";

    private async Task<(ActiveProfileScopeCookieValue Scope, ScopeResidentSummary Resident, HealthBackgroundView View)?> LoadAsync(Guid residenteId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null || residenteId == Guid.Empty || activeScope.Profile is not (SystemProfile.Enfermeria or SystemProfile.Medicina))
        {
            return null;
        }

        var centerId = CenterId.From(activeScope.CenterId);
        var residentId = ResidentId.From(residenteId);
        var find = new FindScopeResidentCommand(activeScope.ProfileScopeId, centerId, residentId, activeScope.Profile);
        var resident = activeScope.Profile == SystemProfile.Enfermeria
            ? await nursing.FindScopeResidentAsync(find, ct)
            : await medicine.FindScopeResidentAsync(find, ct);
        var view = await health.ReadAsync(activeScope.ProfileScopeId, centerId, residentId, ct);
        return resident.Ok && resident.Value is not null && view.Ok ? (activeScope, resident.Value, view.Value!) : null;
    }

    private static string RecordController(ActiveProfileScopeCookieValue activeScope) => activeScope.Profile == SystemProfile.Medicina ? "Medicina" : "Enfermeria";

    /// <summary>Vuelve a la ficha del residente del perfil activo.</summary>
    private IActionResult BackToRecord(ActiveProfileScopeCookieValue activeScope, Guid residenteId) => activeScope.Profile switch
    {
        SystemProfile.Enfermeria => RedirectToAction("Residente", "Enfermeria", new { residenteId }),
        SystemProfile.Medicina => RedirectToAction("Residente", "Medicina", new { residenteId }),
        _ => RedirectToAction("Index", "Home"),
    };
}
