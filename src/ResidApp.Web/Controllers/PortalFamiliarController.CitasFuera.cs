using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;
using ResidApp.Web.Models;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Citas fuera del centro en el Portal Familiar, solo ver (script 0104; PR 5). La familia las ve si el centro las abrió, su residente no está restringido y la cita no está oculta; cualquier
/// otro caso (vínculo ajeno o inexistente, interruptor apagado, cita oculta o ajena) da el mismo 404, sin distinguirlos. Ninguna de estas pantallas se guarda en el navegador ni lleva datos de la
/// cita en el título de la pestaña ni en la dirección. En el detalle, la familia puede ofrecerse a llevar o acompañar («Me encargo yo»), soltarlo («Ya no puedo») o decir que no puede («No puedo»; PR 6).
/// </summary>
public sealed partial class PortalFamiliarController
{
    /// <summary>La lista por días: la vista principal y la única en el móvil.</summary>
    [HttpGet("Residente/{linkId:guid}/CitasFuera")]
    public async Task<IActionResult> CitasFuera(Guid linkId, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(CitasFuera), new { linkId }));
        }

        Response.Headers.CacheControl = "no-store";
        var result = await externals.ListAsync(Query(scope), linkId, ct);
        return result.Ok ? View(new PortalFamiliarCitasFueraViewModel(result.Value!, result.Value!.Today)) : ExternalFailure(result.Error!);
    }

    /// <summary>El mes: una marca neutra por día con citas, sin tipo, especialidad ni número. Solo de hoy a los próximos 90 días; fuera de eso, el mes sale vacío.</summary>
    [HttpGet("Residente/{linkId:guid}/CitasFuera/Mes")]
    public async Task<IActionResult> CitasFueraMes(Guid linkId, int? anio, int? mes, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(CitasFueraMes), new { linkId }));
        }

        Response.Headers.CacheControl = "no-store";
        var result = await externals.ListAsync(Query(scope), linkId, ct);
        if (!result.Ok)
        {
            return ExternalFailure(result.Error!);
        }

        // El mes pedido, acotado a los meses que tocan la ventana de la familia; un valor mal formado se ignora y sale el mes de hoy.
        var today = result.Value!.Today;
        var first = new DateOnly(today.Year, today.Month, 1);
        var last = new DateOnly(today.AddDays(ExternalFamilyRules.WindowDays).Year, today.AddDays(ExternalFamilyRules.WindowDays).Month, 1);
        var reference = anio is >= 2000 and <= 2200 && mes is >= 1 and <= 12 ? new DateOnly(anio.Value, mes.Value, 1) : first;
        reference = reference < first ? first : reference > last ? last : reference;
        return View(new PortalFamiliarCitasFueraViewModel(result.Value, reference));
    }

    [HttpGet("Residente/{linkId:guid}/CitasFuera/{citaId:guid}")]
    public async Task<IActionResult> CitaFuera(Guid linkId, Guid citaId, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(CitaFuera), new { linkId, citaId }));
        }

        Response.Headers.CacheControl = "no-store";
        var result = await externals.FindAsync(Query(scope), linkId, citaId, ct);
        return result.Ok ? View(new PortalFamiliarCitaFueraViewModel(result.Value.Resident, result.Value.Appointment)) : ExternalFailure(result.Error!);
    }

    /// <summary>
    /// «Me encargo yo», «Ya no puedo», «No puedo» y volver atrás en «No puedo» (PR 6; script 0105). Todo lo que la familia no puede ver da el mismo 404 que el resto; «ya lo tiene otra persona» y «ya no se
    /// puede desde aquí» se cuentan en la propia pantalla, con un texto fijo que no dice quién ni qué cambió.
    /// </summary>
    [HttpPost("Residente/{linkId:guid}/CitasFuera/{citaId:guid}/Accion")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CitaFueraAccion(Guid linkId, Guid citaId, ExternalArrangementScope? ambito, FamilyArrangementAction? accion, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return ScopeRedirect(Url.Action(nameof(CitaFuera), new { linkId, citaId }));
        }

        if (accion is not { } action || !Enum.IsDefined(action) || (action is FamilyArrangementAction.Take or FamilyArrangementAction.Release && (ambito is not { } chosen || !Enum.IsDefined(chosen))))
        {
            return ExternalFailure(new ApplicationFailure(ApplicationFailureCode.AccessDenied, "No tienes acceso a esta cita."));
        }

        var result = await externals.ArrangeAsync(Query(scope), linkId, citaId, ambito ?? ExternalArrangementScope.Transporte, action, ct);
        if (!result.Ok)
        {
            return ExternalFailure(result.Error!);
        }

        var part = ambito == ExternalArrangementScope.Acompanante ? "del acompañamiento" : "del transporte";
        switch (result.Value)
        {
            case FamilyArrangementOutcome.Done:
                TempData["Mensaje"] = action switch
                {
                    FamilyArrangementAction.Take => $"Anotado: te encargas {part}. Es un aviso para el centro: no sustituye al teléfono, y el centro lo confirmará por su cuenta.",
                    FamilyArrangementAction.Release => $"Anotado: ya no te encargas {part}. Si el centro ya lo había comprobado contigo, avísale por teléfono.",
                    FamilyArrangementAction.Refuse => "Anotado: has dicho que no puedes. Si cambia, puedes volver atrás.",
                    _ => "Anotado: vuelves a estar disponible.",
                };
                break;
            case FamilyArrangementOutcome.TakenByAnother:
                TempData["Error"] = $"Ya se ha encargado {part} otra persona de la familia.";
                break;
            default:
                TempData["Error"] = "Esto ya no se puede cambiar desde aquí: el centro lo ha dejado resuelto o la cita ha cambiado. Si lo necesitas, llama por teléfono al centro.";
                break;
        }

        return RedirectToAction(nameof(CitaFuera), new { linkId, citaId });
    }

    /// <summary>Todo lo que no es un fallo técnico, sea un vínculo ajeno, un interruptor apagado o una cita que no existe, es el mismo 404 con el mismo texto, que no cuenta nada.</summary>
    private IActionResult ExternalFailure(ApplicationFailure error)
    {
        if (error.Code == ApplicationFailureCode.AccessDenied)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return View("Estado", new PortalFamiliarEstadoViewModel(
                "No encontramos esta página", "Esa página no existe o ya no está disponible. Vuelve a tus residentes e inténtalo desde ahí."));
        }

        return Failure(error);
    }
}
