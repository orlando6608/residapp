using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Grupo A1 del vertical Auxiliar: AUX-01 (mis residentes), AUX-02 (registro cotidiano del residente,
/// solo la ficha de consulta en este grupo — Sin cambios/No valorable/Registrar cambio llegan en A2/A3) y
/// AUX-03 (consulta del basal vigente). Traduce a AuxiliarApplicationService; la autorización y las
/// reglas de negocio no viven aquí.
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
        return detail is null ? RedirectToAction(nameof(Index)) : View(detail);
    }

    public async Task<IActionResult> Basal(Guid residenteId, CancellationToken ct)
    {
        var detail = await LoadResidentDetailAsync(residenteId, ct);
        return detail is null ? RedirectToAction(nameof(Index)) : View(detail);
    }

    /// <summary>Compartido por AUX-02 y AUX-03: primero confirma que el residente está asignado (AUX-01,
    /// mismo criterio que la lista) y solo entonces lee el basal vigente. Si no está asignado, redirige a
    /// AUX-01 sin distinguir "no asignado" de "no existe".</summary>
    private async Task<AuxiliarResidentDetailViewModel?> LoadResidentDetailAsync(Guid residenteId, CancellationToken ct)
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
        var residentId = ResidentId.From(residenteId);

        var findResult = await service.FindAssignedResidentAsync(
            new FindAssignedResidentCommand(activeScope.ProfileScopeId, centroId, residentId), ct);
        if (!findResult.Ok || findResult.Value is null)
        {
            return null;
        }

        var baselineResult = await service.ReadCurrentBaselineAsync(
            new ReadCurrentBaselineCommand(activeScope.ProfileScopeId, centroId, residentId), ct);
        return new AuxiliarResidentDetailViewModel(findResult.Value, baselineResult.Ok ? baselineResult.Value : null);
    }
}
