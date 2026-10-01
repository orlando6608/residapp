using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Families;
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

    /// <summary>ADM-09 (0022): añadir un familiar. No abre su autorización (FAM-01).</summary>
    public async Task<IActionResult> AnadirFamiliar(Guid residenteId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(AnadirFamiliar), new { residenteId }) });
        }

        var detail = await FindAsync(activeScope, residenteId, ct);
        return detail is null
            ? RedirectToAction(nameof(Residentes))
            : View("Familiar", new FamilyMemberViewModel(detail.Resident, new FamilyMemberFormModel
            {
                ResidenteId = residenteId,
                OperacionId = Guid.NewGuid(),
            }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnadirFamiliar([Bind(Prefix = "Form")] FamilyMemberFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var detail = await FindAsync(activeScope, form.ResidenteId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Residentes));
        }

        if (ModelState.IsValid)
        {
            var result = await service.AddFamilyMemberAsync(new AddFamilyMemberCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(form.ResidenteId), form.OperacionId,
                form.NombreVisible, form.Relacion, form.Telefono, form.Correo), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Familiar añadido. No tiene autorización de acceso hasta que la abras y la actives.";
                return RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId });
            }

            ModelState.AddModelError(string.Empty, FamilyMemberError(result.Error!, editing: false));
        }

        return View("Familiar", new FamilyMemberViewModel(detail.Resident, form));
    }

    /// <summary>ADM-08 (0022): editar nombre, relación, teléfono y correo de un familiar del residente.</summary>
    public async Task<IActionResult> EditarFamiliar(Guid residenteId, Guid vinculoId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(EditarFamiliar), new { residenteId, vinculoId }) });
        }

        var detail = await FindAsync(activeScope, residenteId, ct);
        var member = detail?.Family.FirstOrDefault(f => f.LinkId == vinculoId);
        if (member is null)
        {
            return detail is null ? RedirectToAction(nameof(Residentes)) : RedirectToAction(nameof(Residente), new { residenteId });
        }

        return View("Familiar", new FamilyMemberViewModel(detail!.Resident, new FamilyMemberFormModel
        {
            ResidenteId = residenteId,
            VinculoId = vinculoId,
            NombreVisible = member.DisplayName,
            Relacion = member.Relationship,
            Telefono = member.Phone,
            Correo = member.Email,
        }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditarFamiliar([Bind(Prefix = "Form")] FamilyMemberFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var detail = await FindAsync(activeScope, form.ResidenteId, ct);
        if (detail is null || form.VinculoId is not { } linkId)
        {
            return RedirectToAction(nameof(Residentes));
        }

        if (ModelState.IsValid)
        {
            var result = await service.UpdateFamilyMemberAsync(new UpdateFamilyMemberCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(form.ResidenteId), linkId,
                form.NombreVisible, form.Relacion, form.Telefono, form.Correo), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Datos del familiar guardados.";
                return RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId });
            }

            if (result.Error!.Code == ApplicationFailureCode.AccessDenied)
            {
                return RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId });
            }

            ModelState.AddModelError(string.Empty, FamilyMemberError(result.Error, editing: true));
        }

        return View("Familiar", new FamilyMemberViewModel(detail.Resident, form));
    }

    /// <summary>ADM-10/ADM-11 (0022): la autorización de acceso de un familiar, con su historial y los cambios posibles.</summary>
    public async Task<IActionResult> AutorizacionFamiliar(Guid residenteId, Guid vinculoId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(AutorizacionFamiliar), new { residenteId, vinculoId }) });
        }

        var detail = await FindAsync(activeScope, residenteId, ct);
        var member = detail?.Family.FirstOrDefault(f => f.LinkId == vinculoId);
        if (member is null)
        {
            return detail is null ? RedirectToAction(nameof(Residentes)) : RedirectToAction(nameof(Residente), new { residenteId });
        }

        return View(new FamilyAuthorizationViewModel(detail!.Resident, member, DateOnly.FromDateTime(DateTime.Today),
            new FamilyAuthorizationFormModel
            {
                ResidenteId = residenteId,
                VinculoId = vinculoId,
                CambiosEsperados = member.AuthorizationChanges.Count,
            }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AutorizacionFamiliar([Bind(Prefix = "Form")] FamilyAuthorizationFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var centerId = CenterId.From(activeScope.CenterId);
        var residentId = ResidentId.From(form.ResidenteId);
        if (ModelState.IsValid)
        {
            var result = await service.ChangeFamilyAuthorizationAsync(new ChangeFamilyAuthorizationCommand(
                activeScope.ProfileScopeId, centerId, residentId, form.VinculoId, form.Cambio, form.ValidaHasta, form.Motivo,
                form.CambiosEsperados), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = form.Cambio switch
                {
                    FamilyAuthorizationChange.Abrir => "Autorización abierta: queda pendiente hasta que la actives.",
                    FamilyAuthorizationChange.Activar => "Autorización activada.",
                    FamilyAuthorizationChange.Suspender => "Autorización suspendida.",
                    _ => "Autorización revocada.",
                };
                return RedirectToAction(nameof(AutorizacionFamiliar), new { residenteId = form.ResidenteId, vinculoId = form.VinculoId });
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict =>
                    "La autorización ha cambiado desde que abriste la pantalla. Revisa su estado y, si hace falta, vuelve a hacer el cambio.",
                ApplicationFailureCode.InvalidInput =>
                    "Revisa los datos: suspender y revocar piden motivo, la fecha «válida hasta» no puede ser anterior a hoy y el cambio tiene que ser posible desde el estado actual.",
                _ => result.Error.Message,
            });
        }

        // Se vuelve a leer: tras un conflicto, la pantalla muestra el estado vigente y el siguiente envío parte de él.
        var detail = await FindAsync(activeScope, form.ResidenteId, ct);
        var member = detail?.Family.FirstOrDefault(f => f.LinkId == form.VinculoId);
        if (member is null)
        {
            return RedirectToAction(nameof(Residentes));
        }

        form.CambiosEsperados = member.AuthorizationChanges.Count;
        return View(new FamilyAuthorizationViewModel(detail!.Resident, member, DateOnly.FromDateTime(DateTime.Today), form));
    }

    /// <summary>ADM-08 (0022): designar, cambiar o quitar el contacto urgente entre los familiares del residente.</summary>
    public async Task<IActionResult> ContactoUrgente(Guid residenteId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(ContactoUrgente), new { residenteId }) });
        }

        var detail = await FindAsync(activeScope, residenteId, ct);
        return detail is null
            ? RedirectToAction(nameof(Residentes))
            : View(new EmergencyContactViewModel(detail, new EmergencyContactFormModel
            {
                ResidenteId = residenteId,
                DesignacionesEsperadas = detail.EmergencyContacts.Count,
                VinculoId = detail.CurrentEmergencyContact,
            }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ContactoUrgente([Bind(Prefix = "Form")] EmergencyContactFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (ModelState.IsValid)
        {
            var result = await service.DesignateEmergencyContactAsync(new DesignateEmergencyContactCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(form.ResidenteId), form.VinculoId,
                form.DesignacionesEsperadas), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = form.VinculoId is null ? "Contacto urgente quitado." : "Contacto urgente designado.";
                return RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId });
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict =>
                    "El contacto urgente ha cambiado desde que abriste la pantalla. Revisa el vigente y, si hace falta, vuelve a elegir.",
                ApplicationFailureCode.InvalidInput => "Elige un contacto distinto del vigente.",
                _ => result.Error.Message,
            });
        }

        var detail = await FindAsync(activeScope, form.ResidenteId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Residentes));
        }

        form.DesignacionesEsperadas = detail.EmergencyContacts.Count;
        return View(new EmergencyContactViewModel(detail, form));
    }

    /// <summary>La ficha del residente si está en el ámbito activo de Administración; null si no, sin distinguir el motivo.</summary>
    private async Task<AdministrativeResidentDetail?> FindAsync(ActiveProfileScopeCookieValue activeScope, Guid residenteId, CancellationToken ct)
    {
        var result = await service.FindResidentAsync(new FindAdministrativeResidentQuery(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(residenteId)), ct);
        return result.Ok ? result.Value : null;
    }

    private static string FamilyMemberError(ApplicationFailure error, bool editing) => error.Code == ApplicationFailureCode.InvalidInput
        ? "Revisa los datos: nombre, relación y teléfono son obligatorios; el teléfono lleva entre 6 y 15 dígitos y el correo, si lo escribes, una sola «@»."
          + (editing ? " Además, algo tiene que cambiar." : "")
        : error.Message;

    private static AdministracionQuery Query(ActiveProfileScopeCookieValue activeScope) =>
        new(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId));
}
