using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Auxiliar;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Vertical Auxiliar: AUX-01 (mis residentes), AUX-02 (registro cotidiano del residente) y AUX-03
/// (consulta del basal vigente) del grupo A1; AUX-04 (Sin cambios) y AUX-05 (No valorable) del grupo A2;
/// AUX-06 a AUX-12 (Registrar cambio) de los grupos A3+A4. Traduce a AuxiliarApplicationService; la
/// autorización y las reglas de negocio no viven aquí.
/// </summary>
public sealed class AuxiliarController(AuxiliarApplicationService service) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Index)) });
        }

        var command = new ListAssignedResidentsCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId));
        var result = await service.ListAssignedResidentsAsync(command, ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(Array.Empty<AssignedResidentSummary>());
        }

        return View(result.Value);
    }

    public async Task<IActionResult> Registro(Guid residenteId, CancellationToken ct)
    {
        var detail = await LoadResidentDetailAsync(residenteId, ct);
        return detail is null
            ? RedirectToAction(nameof(Index))
            : View(new AuxiliarRegistroViewModel(detail.Resident, detail.Baseline, Guid.NewGuid(), Guid.NewGuid()));
    }

    public async Task<IActionResult> Basal(Guid residenteId, CancellationToken ct)
    {
        var detail = await LoadResidentDetailAsync(residenteId, ct);
        return detail is null ? RedirectToAction(nameof(Index)) : View(detail);
    }

    /// <summary>AUX-04: no lleva campos propios más allá del residente y el OperacionId de idempotencia,
    /// así que no hace falta un FormModel dedicado.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SinCambios(Guid residenteId, Guid operacionId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Registro), new { residenteId }) });
        }

        var command = new RegisterDailyClosureCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(residenteId),
            DailyClosureType.SinCambios, Motivo: null, operacionId);
        var result = await service.RegisterDailyClosureAsync(command, ct);

        // Redirige siempre (patrón post-redirect-get): a diferencia de Residents/Create o Baseline/Sign,
        // este formulario no tiene campos propios que conservar tras un fallo, así que no hace falta
        // volver a mostrar la vista con el ModelState — un mensaje flash basta.
        TempData[result.Ok ? "Mensaje" : "Error"] = result.Ok
            ? "Cierre registrado: sin cambios."
            : result.Error!.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NoValorable(NoValorableFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Registro), new { residenteId = form.ResidenteId }) });
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Indica un motivo para marcar el día como no valorable.";
            return RedirectToAction(nameof(Registro), new { residenteId = form.ResidenteId });
        }

        var command = new RegisterDailyClosureCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(form.ResidenteId),
            DailyClosureType.NoValorable, form.Motivo, form.OperacionId);
        var result = await service.RegisterDailyClosureAsync(command, ct);

        TempData[result.Ok ? "Mensaje" : "Error"] = result.Ok
            ? "Cierre registrado: día marcado como no valorable."
            : result.Error!.Message;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>AUX-06/AUX-07/AUX-08/AUX-09/AUX-10 (grupo A3): una sola pantalla con las diez áreas
    /// (checklist de opciones rápidas en siete de ellas, texto libre en todas), temperatura opcional y
    /// clasificación. No persiste nada — eso solo ocurre al confirmar (ConfirmarCambio, grupo A4).</summary>
    public async Task<IActionResult> RegistrarCambio(Guid residenteId, CancellationToken ct)
    {
        var resolved = await ResolveAssignedResidentAsync(residenteId, ct);
        if (resolved is null)
        {
            return RedirectToAction(nameof(Index));
        }

        ViewBag.Resident = resolved.Value.Resident;
        return View(new RegistrarCambioFormModel { ResidenteId = residenteId, OperacionId = Guid.NewGuid() });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarCambio(RegistrarCambioFormModel form, CancellationToken ct)
    {
        var resolved = await ResolveAssignedResidentAsync(form.ResidenteId, ct);
        if (resolved is null)
        {
            return RedirectToAction(nameof(Index));
        }

        // AUX-06: exige al menos un área con contenido (texto u opción marcada). AUX-10: un prioritario
        // exige motivo de catálogo cerrado. Comprobación temprana para no llegar a AUX-11A/11B con datos
        // incompletos; el caso de uso (ConfirmarCambio) vuelve a exigir esto igual, por si se salta este
        // paso.
        if (BuildAreas(form).Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Selecciona al menos un área con contenido.");
        }
        if (form.Clasificacion == DailyChangeClassification.Prioritario && form.MotivoPrioritario is null)
        {
            ModelState.AddModelError(nameof(form.MotivoPrioritario), "Elige el motivo prioritario.");
        }

        ViewBag.Resident = resolved.Value.Resident;
        return ModelState.IsValid ? View("ConfirmarCambio", form) : View(form);
    }

    /// <summary>AUX-11A (ordinario) / AUX-11B+AUX-12 (prioritario, con el aviso directo documentado aquí
    /// mismo): confirmación final, la única que realmente escribe en dbo.cierres_cotidianos_residente.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmarCambio(RegistrarCambioFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(RegistrarCambio), new { residenteId = form.ResidenteId }) });
        }
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Revisa los datos del cambio antes de confirmar.";
            return RedirectToAction(nameof(RegistrarCambio), new { residenteId = form.ResidenteId });
        }
        if (form.Clasificacion == DailyChangeClassification.Prioritario && string.IsNullOrWhiteSpace(form.AvisoDirecto))
        {
            TempData["Error"] = "Documenta el aviso directo antes de confirmar un evento prioritario.";
            return RedirectToAction(nameof(RegistrarCambio), new { residenteId = form.ResidenteId });
        }

        var areas = BuildAreas(form);
        var command = new RegisterDailyChangeCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(form.ResidenteId),
            areas, form.Temperatura, form.Clasificacion!.Value, form.MotivoPrioritario, form.AvisoDirecto, form.OperacionId);
        var result = await service.RegisterDailyChangeAsync(command, ct);

        TempData[result.Ok ? "Mensaje" : "Error"] = result.Ok
            ? $"Cambio registrado: enviado a la bandeja {(form.Clasificacion == DailyChangeClassification.Prioritario ? "prioritaria" : "ordinaria")} de Enfermería."
            : result.Error!.Message;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Traduce AreaTexto + AreaOpciones (AUX-07) a la lista de áreas con contenido: cada opción
    /// marcada llega como "AREA_CODE:OPCION_CODE" (ver RegistrarCambioFormModel.AreaOpciones); una entrada
    /// con código de área o de opción desconocido se descarta en vez de fallar, ya que el propio caso de
    /// uso vuelve a validar contra el catálogo cerrado.</summary>
    private static List<RegisterDailyChangeAreaCommand> BuildAreas(RegistrarCambioFormModel form)
    {
        var opcionesPorArea = new Dictionary<DailyChangeAreaCode, List<DailyChangeAreaOptionCode>>();
        foreach (var entrada in form.AreaOpciones)
        {
            var partes = entrada.Split(':', 2);
            if (partes.Length != 2 ||
                !EnumCode.TryParseCode<DailyChangeAreaCode>(partes[0], out var area) ||
                !EnumCode.TryParseCode<DailyChangeAreaOptionCode>(partes[1], out var opcion))
            {
                continue;
            }
            if (!opcionesPorArea.TryGetValue(area, out var lista))
            {
                opcionesPorArea[area] = lista = [];
            }
            lista.Add(opcion);
        }

        var areas = new List<RegisterDailyChangeAreaCommand>();
        foreach (var area in Enum.GetValues<DailyChangeAreaCode>())
        {
            var texto = form.AreaTexto.GetValueOrDefault(area.ToCode());
            var opciones = (IReadOnlyList<DailyChangeAreaOptionCode>)opcionesPorArea.GetValueOrDefault(area, []);
            if (opciones.Count == 0 && string.IsNullOrWhiteSpace(texto))
            {
                continue;
            }
            areas.Add(new RegisterDailyChangeAreaCommand(area, opciones, string.IsNullOrWhiteSpace(texto) ? null : texto));
        }
        return areas;
    }

    /// <summary>Compartido por AUX-02, AUX-03 y RegistrarCambio: confirma que el residente está asignado
    /// (AUX-01, mismo criterio que la lista). Si no lo está, el llamador redirige a AUX-01 sin distinguir
    /// "no asignado" de "no existe".</summary>
    private async Task<(ActiveProfileScopeCookieValue Scope, AssignedResidentSummary Resident)?> ResolveAssignedResidentAsync(
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
        var findResult = await service.FindAssignedResidentAsync(
            new FindAssignedResidentCommand(activeScope.ProfileScopeId, centroId, ResidentId.From(residenteId)), ct);
        return !findResult.Ok || findResult.Value is null ? null : (activeScope, findResult.Value);
    }

    private async Task<AuxiliarResidentDetailViewModel?> LoadResidentDetailAsync(Guid residenteId, CancellationToken ct)
    {
        var resolved = await ResolveAssignedResidentAsync(residenteId, ct);
        if (resolved is null)
        {
            return null;
        }
        var (activeScope, resident) = resolved.Value;
        var centroId = CenterId.From(activeScope.CenterId);
        var baselineResult = await service.ReadCurrentBaselineAsync(
            new ReadCurrentBaselineCommand(activeScope.ProfileScopeId, centroId, resident.ResidentId), ct);
        return new AuxiliarResidentDetailViewModel(resident, baselineResult.Ok ? baselineResult.Value : null);
    }
}
