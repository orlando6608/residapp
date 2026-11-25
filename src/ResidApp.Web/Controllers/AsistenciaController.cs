using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Attendance;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Asistencia diaria del Centro de Día (ADR 0019, script 0115), pantallas del personal. Una sola ruta para Administración (lee, enciende la función, fija la pauta y registra ausencias), Auxiliar, Enfermería y
/// Medicina (anotan; Enfermería también fija la pauta y registra ausencias). Traduce a AttendanceApplicationService; la autorización (ámbito, perfil, tipo de centro, interruptor y alcance sobre el residente) no vive
/// aquí: la cookie solo orienta y cada caso de uso y el almacén vuelven a comprobar. Los mensajes no repiten nunca lo escrito (la nota). [Authorize] repite a nivel de controlador lo que ya exige la política global.
/// </summary>
[Authorize]
[SoloCentroDeDia]
public sealed class AsistenciaController(AttendanceApplicationService attendance) : Controller
{
    private const string NoAccess = "No tienes acceso a esta pantalla.";

    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? fecha, CancellationToken ct)
    {
        // Un valor mal formado en la URL se ignora (queda hoy), no se muestra como error.
        ModelState.Clear();
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        var today = CenterTime.Today;
        var day = fecha is { } asked && asked <= today && asked >= today.AddYears(-5) ? asked : today;
        // Nunca encendida: Administración, que es quien la enciende, va directa a los ajustes. Apagada después: se sigue pudiendo consultar lo anotado, pero no anotar días nuevos.
        var state = await attendance.ReadCenterAsync(scope.ProfileScopeId, CenterId.From(scope.CenterId), ct);
        if (!state.Ok)
        {
            return View(new AsistenciaDiaViewModel(day, today, [], scope.Profile, state.Error!.Message));
        }

        if (scope.Profile == SystemProfile.Administracion && state.Value!.Number == 0 && !state.Value.Active)
        {
            return RedirectToAction(nameof(Ajustes));
        }

        var result = await attendance.ListDayAsync(scope.ProfileScopeId, CenterId.From(scope.CenterId), day, ct);
        return result.Ok
            ? View(new AsistenciaDiaViewModel(day, today, result.Value!, scope.Profile, null, state.Value!.Active))
            : View(new AsistenciaDiaViewModel(day, today, [], scope.Profile, result.Error!.Message));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Anotar(Guid residenteId, DateOnly fecha, AttendanceState estado, AttendanceCause? causa, string? nota, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        // «Vino» no lleva causa ni nota: lo que haya quedado escrito en la fila al pulsar ese botón se descarta.
        var result = await attendance.RecordAsync(new RecordAttendanceCommand(
            scope.ProfileScopeId, CenterId.From(scope.CenterId), ResidentId.From(residenteId), fecha,
            estado, estado == AttendanceState.Asistio ? null : causa, estado == AttendanceState.Asistio ? null : nota), ct);
        TempData[result.Ok ? "Mensaje" : "Error"] = result.Ok ? "Anotado." : result.Error!.Message;
        // El aviso sale junto a la persona anotada y se lleva allí el foco: en una lista larga no hay que volver a buscar la fila.
        TempData["Fila"] = residenteId.ToString("N");
        return RedirectToAction(nameof(Index), new { fecha });
    }

    [HttpGet]
    public async Task<IActionResult> Pauta(Guid residenteId, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        var result = await attendance.ReadPatternAsync(scope.ProfileScopeId, CenterId.From(scope.CenterId), ResidentId.From(residenteId), ct);
        if (!result.Ok)
        {
            TempData["Error"] = result.Error!.Message;
            return RedirectToAction(nameof(Index));
        }

        return View(new AsistenciaPautaViewModel(result.Value!, AttendanceRules.CanManageAbsences(scope.Profile), CenterTime.Today, scope.Profile));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pauta(Guid residenteId, int version, DayOfWeek[]? dias, DateOnly? valeDesde, bool sustituirProgramado, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        var mask = (dias ?? []).Distinct().Aggregate(0, (all, day) => all | AttendanceRules.Mask(day));
        var result = await attendance.SetPatternAsync(new SetAttendancePatternCommand(scope.ProfileScopeId, CenterId.From(scope.CenterId), ResidentId.From(residenteId), version, mask, valeDesde, sustituirProgramado), ct);
        if (result.Ok && result.Value == version)
        {
            // Mismos días que la pauta actual: no se ha creado ninguna versión, y no se dice que se guardó.
            TempData["Aviso"] = "No hay cambios: los días son los mismos que los de la última pauta anotada. Para cambiar la fecha desde la que cuenta, cambia también algún día.";
            return RedirectToAction(nameof(Pauta), new { residenteId });
        }

        if (result.Ok)
        {
            TempData["Mensaje"] = "Pauta guardada.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Error"] = result.Error!.Message;
        return RedirectToAction(nameof(Pauta), new { residenteId });
    }

    [HttpGet]
    public async Task<IActionResult> Ausencias(Guid? residenteId, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        var centerId = CenterId.From(scope.CenterId);
        var filter = residenteId is { } id && id != Guid.Empty ? ResidentId.From(id) : (ResidentId?)null;
        var result = await attendance.ListAbsencesAsync(scope.ProfileScopeId, centerId, filter, ct);
        if (!result.Ok)
        {
            TempData["Error"] = result.Error!.Message;
            return RedirectToAction(nameof(Index));
        }

        var canManage = AttendanceRules.CanManageAbsences(scope.Profile);
        var residents = canManage ? await attendance.ListDayAsync(scope.ProfileScopeId, centerId, null, ct) : null;
        return View(new AsistenciaAusenciasViewModel(
            result.Value!, residents is { Ok: true } ? residents.Value! : [], canManage, AttendanceRules.CanReadAbsenceReason(scope.Profile), CenterTime.Today, residenteId));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarAusencia(Guid residenteId, DateOnly? desde, DateOnly? hasta, AbsenceReason? motivo, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        if (desde is null || hasta is null || motivo is null)
        {
            TempData["Error"] = "Elige la persona, el primer y el último día y el motivo.";
            return RedirectToAction(nameof(Ausencias));
        }

        var result = await attendance.RegisterAbsenceAsync(new RegisterAbsenceCommand(
            scope.ProfileScopeId, CenterId.From(scope.CenterId), ResidentId.From(residenteId), desde.Value, hasta.Value, motivo.Value), ct);
        TempData[result.Ok ? "Mensaje" : "Error"] = result.Ok ? "Ausencia registrada. Esos días no saldrán como «sin anotar»." : result.Error!.Message;
        return RedirectToAction(nameof(Ausencias));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnularAusencia(Guid ausenciaId, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        var result = await attendance.AnnulAbsenceAsync(new AbsenceChangeCommand(scope.ProfileScopeId, CenterId.From(scope.CenterId), ausenciaId), ct);
        TempData[result.Ok ? "Mensaje" : "Error"] = result.Ok ? "Ausencia anulada." : result.Error!.Message;
        return RedirectToAction(nameof(Ausencias));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcortarAusencia(Guid ausenciaId, DateOnly? nuevoHasta, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Index", "Home");
        }

        if (nuevoHasta is null)
        {
            TempData["Error"] = "Elige el nuevo último día.";
            return RedirectToAction(nameof(Ausencias));
        }

        var result = await attendance.ShortenAbsenceAsync(new ShortenAbsenceCommand(scope.ProfileScopeId, CenterId.From(scope.CenterId), ausenciaId, nuevoHasta.Value), ct);
        TempData[result.Ok ? "Mensaje" : "Error"] = result.Ok ? "Ausencia acortada." : result.Error!.Message;
        return RedirectToAction(nameof(Ausencias));
    }

    /// <summary>Solo Administración. Una consulta: no guarda nada, no avisa a nadie y no se imprime. Sin datos en la dirección ni en el título; la caché del navegador no la guarda.</summary>
    [HttpGet]
    public async Task<IActionResult> Seguimiento(CancellationToken ct)
    {
        if (Scope() is not { Profile: SystemProfile.Administracion } scope)
        {
            TempData["Error"] = "La lista de seguimiento es solo de Administración.";
            return RedirectToAction(nameof(Index));
        }

        Response.Headers.CacheControl = "no-store";
        var result = await attendance.ListFollowUpAsync(scope.ProfileScopeId, CenterId.From(scope.CenterId), ct);
        if (!result.Ok)
        {
            TempData["Error"] = result.Error!.Message;
            return RedirectToAction(nameof(Index));
        }

        return View(new AsistenciaSeguimientoViewModel(result.Value!, CenterTime.Today));
    }

    [HttpGet]
    public async Task<IActionResult> Ajustes(CancellationToken ct)
    {
        if (Scope() is not { Profile: SystemProfile.Administracion } scope)
        {
            TempData["Error"] = "Solo Administración activa o desactiva la asistencia.";
            return RedirectToAction(nameof(Index));
        }

        var state = await attendance.ReadCenterAsync(scope.ProfileScopeId, CenterId.From(scope.CenterId), ct);
        if (!state.Ok)
        {
            TempData["Error"] = state.Error!.Message;
            return RedirectToAction("Index", "Home");
        }

        return View(new AsistenciaAjustesViewModel(state.Value!, null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ajustes(int version, bool encender, bool informada, bool eipd, CancellationToken ct)
    {
        if (Scope() is not { Profile: SystemProfile.Administracion } scope)
        {
            TempData["Error"] = "Solo Administración activa o desactiva la asistencia.";
            return RedirectToAction(nameof(Index));
        }

        var result = await attendance.SetCenterAsync(new SetAttendanceCenterCommand(scope.ProfileScopeId, CenterId.From(scope.CenterId), version, encender, informada, eipd), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = encender ? "La asistencia está activada. Ya se puede anotar quién viene cada día." : "La asistencia está desactivada. Lo ya anotado se conserva.";
            return RedirectToAction(nameof(Ajustes));
        }

        if (result.Error!.Code == ApplicationFailureCode.InvalidInput)
        {
            var state = await attendance.ReadCenterAsync(scope.ProfileScopeId, CenterId.From(scope.CenterId), ct);
            return state.Ok
                ? View(new AsistenciaAjustesViewModel(state.Value!, "Para activar la asistencia tienes que marcar las dos confirmaciones."))
                : RedirectToAction("Index", "Home");
        }

        TempData["Error"] = result.Error.Message;
        return RedirectToAction(nameof(Ajustes));
    }

    private ActiveProfileScopeCookieValue? Scope() =>
        ActiveProfileScopeCookie.Read(Request) is { } scope && scope.Profile is SystemProfile.Administracion or SystemProfile.Auxiliar or SystemProfile.Enfermeria or SystemProfile.Medicina ? scope : null;
}
