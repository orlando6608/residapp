using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Portal Familiar, bloque 1 (script 0052): el familiar elige un residente al que tiene acceso (autorización Activa) y lee sus comunicados
/// publicados. Solo con el perfil activo FAMILIAR; cada lectura la comprueba el caso de uso contra SQL (vínculo y autorización), nunca la cookie.
/// Los textos que no cuentan nada del residente: un vínculo ajeno, suspendido o inexistente da el mismo «sin acceso».
/// </summary>
[Route("Familia")]
public sealed class PortalFamiliarController(FamilyPortalApplicationService service) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(bool elegir, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Index)) });
        }

        var result = await service.ListResidentsAsync(Query(scope), ct);
        if (!result.Ok)
        {
            return Failure(result.Error!);
        }

        return result.Value!.Count switch
        {
            0 => View("Estado", new PortalFamiliarEstadoViewModel(
                "No tienes ninguna autorización activa",
                "Ahora mismo no tienes acceso a la información de ningún residente. Si crees que es un error, contacta con la Administración del centro.")),
            1 when !elegir => RedirectToAction(nameof(Residente), new { linkId = result.Value[0].LinkId }),
            _ => View(result.Value),
        };
    }

    [HttpGet("Residente/{linkId:guid}")]
    public async Task<IActionResult> Residente(Guid linkId, string? tipo, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Residente), new { linkId }) });
        }

        var result = await service.ReadResidentAsync(Query(scope), linkId, ct);
        if (!result.Ok)
        {
            return Failure(result.Error!);
        }

        FamilyCommunicationType? type = tipo switch
        {
            "ordinaria" => FamilyCommunicationType.Ordinaria,
            "relevante" => FamilyCommunicationType.Relevante,
            _ => null,
        };
        var home = result.Value!;
        var communications = type is null ? home.Communications : home.Communications.Where(c => c.Type == type).ToList();
        return View(new PortalFamiliarResidenteViewModel(home, type, communications));
    }

    [HttpGet("Residente/{linkId:guid}/Comunicado/{comunicadoId:guid}")]
    public async Task<IActionResult> Comunicado(Guid linkId, Guid comunicadoId, CancellationToken ct)
    {
        if (Scope() is not { } scope)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Comunicado), new { linkId, comunicadoId }) });
        }

        var result = await service.ReadCommunicationAsync(Query(scope), linkId, comunicadoId, ct);
        return result.Ok
            ? View(new PortalFamiliarComunicadoViewModel(result.Value!.Resident, result.Value.Communication))
            : Failure(result.Error!);
    }

    private ActiveProfileScopeCookieValue? Scope() =>
        ActiveProfileScopeCookie.Read(Request) is { Profile: SystemProfile.Familiar } scope ? scope : null;

    private static FamilyPortalQuery Query(ActiveProfileScopeCookieValue scope) => new(scope.ProfileScopeId, CenterId.From(scope.CenterId));

    private IActionResult Failure(ApplicationFailure error)
    {
        if (error.Code == ApplicationFailureCode.AccessDenied)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return View("Estado", new PortalFamiliarEstadoViewModel(
                "No tienes acceso a esto",
                "No puedes ver esta información. Vuelve a tus residentes e inténtalo desde ahí."));
        }

        Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return View("Estado", new PortalFamiliarEstadoViewModel(
            "No hemos podido cargar la información",
            "Ha habido un fallo técnico. Esto no significa que no haya novedades: inténtalo de nuevo en unos minutos.", PuedeReintentar: true));
    }
}
