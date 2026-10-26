using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Organization;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// ADM-07 (script 0073, CJ 1.8): el organigrama del centro en solo lectura para todos sus profesionales (Administración, Enfermería, Auxiliar,
/// Medicina y Dirección Clínica), nunca la familia. Administración lo gestiona en AdministracionController.Cargos. La autorización no vive aquí:
/// OrganigramaApplicationService revalida el ámbito activo contra SQL y rechaza a la familia y a Plataforma.
/// </summary>
public sealed class OrganigramaController(OrganigramaApplicationService organigrama) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Index)) });
        }

        var result = await organigrama.ReadAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ct);
        if (!result.Ok && result.Error!.Code == ApplicationFailureCode.AccessDenied)
        {
            // La familia y Plataforma no ven el organigrama: a su inicio, sin enseñar una pantalla de personal.
            return RedirectToAction("Index", "Home");
        }

        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        var positions = result.Value ?? [];
        ViewData["PuedeGestionar"] = activeScope.Profile == SystemProfile.Administracion;
        return View(new OrganigramaViewModel(
            PositionTree.Nest(positions, p => p.PositionId, p => p.SuperiorId, p => p.Name), positions.Count, Manage: false, CanLoadTemplate: false));
    }
}
