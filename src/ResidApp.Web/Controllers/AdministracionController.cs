using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Vertical Administración, bloque 1 (historia 1): ADM-01 (inicio), ADM-02 (lista de residentes del ámbito) y ADM-03
/// (ficha administrativa con historial de ubicación y corrección de identidad). Ninguna pantalla muestra basal, Barthel
/// ni contenido clínico. Traduce a AdministracionApplicationService; la autorización no vive aquí.
/// </summary>
public sealed class AdministracionController(AdministracionApplicationService service) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Index)) });
        }

        var result = await service.ListResidentsAsync(Query(activeScope), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        return View(new AdministracionInicioViewModel(result.Value?.Count ?? 0));
    }

    public async Task<IActionResult> Residentes(AdministrativeResidentFilter filtro, CancellationToken ct)
    {
        // ADM-02: un valor del filtro mal formado en la URL se ignora (queda sin filtrar), no se muestra como error.
        ModelState.Clear();
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Request.Path + Request.QueryString });
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var result = await service.ListResidentsAsync(Query(activeScope), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(AdministrativeResidentListViewModel.From([], filtro, today));
        }

        return View(AdministrativeResidentListViewModel.From(result.Value!, filtro, today));
    }

    /// <summary>ADM-03: si el residente no existe o no está en el ámbito, se vuelve a la lista sin distinguir el motivo.</summary>
    public async Task<IActionResult> Residente(Guid residenteId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Residente), new { residenteId }) });
        }

        var result = await service.FindResidentAsync(new FindAdministrativeResidentQuery(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(residenteId)), ct);
        return result.Ok
            ? View(new AdministrativeResidentViewModel(result.Value!, DateOnly.FromDateTime(DateTime.Today)))
            : RedirectToAction(nameof(Residentes));
    }

    public async Task<IActionResult> CorregirIdentidad(Guid residenteId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(CorregirIdentidad), new { residenteId }) });
        }

        var result = await service.FindResidentAsync(new FindAdministrativeResidentQuery(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(residenteId)), ct);
        if (!result.Ok)
        {
            return RedirectToAction(nameof(Residentes));
        }

        var resident = result.Value!.Resident;
        return View(new CorrectIdentityViewModel(resident, new CorrectIdentityFormModel
        {
            ResidenteId = residenteId,
            CorreccionesEsperadas = result.Value.Corrections.Count,
            NombreVisible = resident.DisplayName,
            FechaNacimiento = resident.BirthDate,
            SexoDocumentado = resident.DocumentedSex,
        }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CorregirIdentidad([Bind(Prefix = "Form")] CorrectIdentityFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var centerId = CenterId.From(activeScope.CenterId);
        var residentId = ResidentId.From(form.ResidenteId);
        var current = await service.FindResidentAsync(new FindAdministrativeResidentQuery(activeScope.ProfileScopeId, centerId, residentId), ct);
        if (!current.Ok)
        {
            return RedirectToAction(nameof(Residentes));
        }

        if (ModelState.IsValid)
        {
            var result = await service.CorrectIdentityAsync(new CorrectResidentIdentityCommand(
                activeScope.ProfileScopeId, centerId, residentId, form.NombreVisible, form.FechaNacimiento!.Value,
                form.SexoDocumentado, form.Motivo, form.CorreccionesEsperadas), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Identidad corregida.";
                return RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId });
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict =>
                    "La identidad ha cambiado desde que abriste el formulario. Revisa los datos vigentes y, si hace falta, vuelve a enviar la corrección.",
                ApplicationFailureCode.InvalidInput =>
                    "Revisa los datos: el nombre no puede quedar vacío, la fecha de nacimiento no puede ser futura, el motivo es obligatorio y algo tiene que cambiar.",
                _ => result.Error.Message,
            });
            if (result.Error.Code == ApplicationFailureCode.Conflict)
            {
                // La ficha se muestra ya actualizada y lo escrito se conserva; el siguiente envío parte de la corrección vigente.
                form.CorreccionesEsperadas = current.Value!.Corrections.Count;
            }
        }

        return View(new CorrectIdentityViewModel(current.Value!.Resident, form));
    }

    private static AdministracionQuery Query(ActiveProfileScopeCookieValue activeScope) =>
        new(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId));
}
