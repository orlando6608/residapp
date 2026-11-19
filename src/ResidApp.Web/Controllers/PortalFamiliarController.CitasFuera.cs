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
/// cita en el título de la pestaña ni en la dirección; no hay botones de acción (eso es el PR 6).
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
