using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Families;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Vertical Administración, bloque 1 (historia 1): ADM-01 (inicio), ADM-02 (lista de residentes del ámbito) y ADM-03
/// (ficha administrativa con historial de ubicación y corrección de identidad). Bloque 2: familiares, autorizaciones y
/// contacto urgente. Bloque 3 (historia 4, 0023): ADM-12 y ADM-13, cuentas profesionales con sus perfiles, unidades y
/// residentes de Auxiliar. Ninguna pantalla muestra basal, Barthel ni contenido clínico. Traduce a
/// AdministracionApplicationService; la autorización no vive aquí.
/// </summary>
public sealed partial class AdministracionController(
    AdministracionApplicationService service, ResidentTransferApplicationService transfers, ResidentStatusApplicationService statuses,
    ListActiveScopeUnits listUnits,
    ListActiveScopeLocations listLocations, AdministracionEstructuraApplicationService estructura,
    AdministracionTurnosApplicationService turnos) : Controller
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

        var accounts = await service.ListAccountsAsync(Query(activeScope), ct);
        return View(new AdministracionInicioViewModel(result.Value?.Count ?? 0, accounts.Value?.Count ?? 0));
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
        if (!result.Ok)
        {
            return RedirectToAction(nameof(Residentes));
        }

        var suspension = await statuses.FindSuspensionAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(residenteId), ct);
        return View(new AdministrativeResidentViewModel(result.Value!, DateOnly.FromDateTime(DateTime.Today), suspension.Value));
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

    /// <summary>Vincular a este residente un familiar que ya está vinculado a otro residente del ámbito. No abre su autorización (FAM-01).</summary>
    public async Task<IActionResult> VincularFamiliar(Guid residenteId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(VincularFamiliar), new { residenteId }) });
        }

        var detail = await FindAsync(activeScope, residenteId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Residentes));
        }

        return View(new LinkFamilyViewModel(
            detail.Resident, await LinkableFamilyAsync(activeScope, residenteId, ct), new LinkFamilyFormModel { ResidenteId = residenteId, OperacionId = Guid.NewGuid() }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VincularFamiliar([Bind(Prefix = "Form")] LinkFamilyFormModel form, CancellationToken ct)
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
            var result = await service.LinkFamilyMemberAsync(new LinkFamilyMemberCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(form.ResidenteId), form.OperacionId,
                form.FamiliarId!.Value, form.Relacion), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Familiar vinculado. No tiene autorización de acceso hasta que la abras y la actives.";
                return RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId });
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.InvalidInput => "Escribe la relación con el residente (hasta 100 caracteres).",
                ApplicationFailureCode.Conflict => "Ese familiar ya está vinculado a este residente.",
                ApplicationFailureCode.AccessDenied => "Ese familiar ya no se puede vincular. Elige otro de la lista.",
                _ => result.Error.Message,
            });
        }

        return View(new LinkFamilyViewModel(detail.Resident, await LinkableFamilyAsync(activeScope, form.ResidenteId, ct), form));
    }

    private async Task<IReadOnlyList<LinkableFamilyMember>> LinkableFamilyAsync(
        ActiveProfileScopeCookieValue activeScope, Guid residenteId, CancellationToken ct)
    {
        var result = await service.ListLinkableFamilyAsync(new FindAdministrativeResidentQuery(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(residenteId)), ct);
        return result.Ok ? result.Value! : [];
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

        return View("Familiar", new FamilyMemberViewModel(detail!.Resident, EditForm(residenteId, member), member.OtherResidentLinks));
    }

    /// <summary>El formulario de edición con los datos actuales del familiar y su versión (ver FamilyMemberData.Version).</summary>
    private static FamilyMemberFormModel EditForm(Guid residenteId, ResidentFamilyMember member) => new()
    {
        ResidenteId = residenteId,
        VinculoId = member.LinkId,
        Version = new FamilyMemberData(member.DisplayName, member.Relationship, member.Phone, member.Email).Version,
        NombreVisible = member.DisplayName,
        Relacion = member.Relationship,
        Telefono = member.Phone,
        Correo = member.Email,
    };

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
                form.NombreVisible, form.Relacion, form.Telefono, form.Correo, form.Version ?? string.Empty), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Datos del familiar guardados.";
                return RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId });
            }

            if (result.Error!.Code == ApplicationFailureCode.AccessDenied)
            {
                return RedirectToAction(nameof(Residente), new { residenteId = form.ResidenteId });
            }

            if (result.Error.Code == ApplicationFailureCode.Conflict)
            {
                // Otra persona cambió al familiar mientras se editaba: se enseñan los datos actuales y se pide revisarlos.
                var fresh = await FindAsync(activeScope, form.ResidenteId, ct);
                var current = fresh?.Family.FirstOrDefault(f => f.LinkId == linkId);
                if (fresh is null || current is null)
                {
                    return RedirectToAction(nameof(Residentes));
                }

                ModelState.Clear();
                ModelState.AddModelError(string.Empty,
                    "Otra persona ha cambiado los datos de este familiar mientras los editabas. Aquí tienes los datos actuales: revísalos y vuelve a guardar si aún hace falta.");
                return View("Familiar", new FamilyMemberViewModel(fresh.Resident, EditForm(form.ResidenteId, current), current.OtherResidentLinks));
            }

            ModelState.AddModelError(string.Empty, FamilyMemberError(result.Error, editing: true));
        }

        return View("Familiar", new FamilyMemberViewModel(detail.Resident, form, detail.Family.FirstOrDefault(f => f.LinkId == linkId)?.OtherResidentLinks ?? 0));
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

    /// <summary>ADM-12 (0023): las cuentas con algún perfil en el centro.</summary>
    public async Task<IActionResult> Usuarios(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Usuarios)) });
        }

        var result = await service.ListAccountsAsync(Query(activeScope), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        return View(result.Value ?? []);
    }

    /// <summary>ADM-13: si la cuenta no tiene perfiles en el centro, se vuelve a la lista sin distinguir el motivo.</summary>
    public async Task<IActionResult> Usuario(Guid cuentaId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Usuario), new { cuentaId }) });
        }

        var detail = await FindAccountAsync(activeScope, cuentaId, ct);
        return detail is null ? RedirectToAction(nameof(Usuarios)) : View(detail);
    }

    public async Task<IActionResult> NuevoUsuario(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(NuevoUsuario)) });
        }

        var units = await AdministratorUnitsAsync(activeScope, ct);
        return View(new NewProfessionalAccountViewModel(new NewProfessionalAccountFormModel
        {
            OperacionId = Guid.NewGuid(),
            Unidades = units.Count == 1 ? [units[0].UnitId.Value] : [],
        }, units));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuevoUsuario([Bind(Prefix = "Form")] NewProfessionalAccountFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (form.Unidades.Count == 0)
        {
            ModelState.AddModelError("Form.Unidades", "Elige al menos una unidad.");
        }

        if (ModelState.IsValid)
        {
            var result = await service.CreateAccountAsync(new CreateProfessionalAccountCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.OperacionId, form.Identificador, form.NombreVisible,
                form.Perfil!.Value, form.Unidades), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Usuario dado de alta. Ya puede entrar con su identificador de acceso.";
                return RedirectToAction(nameof(Usuario), new { cuentaId = result.Value.Value });
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => "Ya existe una cuenta con ese identificador de acceso.",
                ApplicationFailureCode.InvalidInput =>
                    "Revisa los datos: el identificador lleva de 3 a 200 caracteres sin espacios (letras, dígitos, «.», «_», «-» o «@»), el nombre es obligatorio y hay que elegir un perfil y al menos una unidad de tu ámbito.",
                _ => result.Error.Message,
            });
        }

        return View(new NewProfessionalAccountViewModel(form, await AdministratorUnitsAsync(activeScope, ct)));
    }

    public async Task<IActionResult> NombreUsuario(Guid cuentaId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(NombreUsuario), new { cuentaId }) });
        }

        var detail = await FindAccountAsync(activeScope, cuentaId, ct);
        if (detail is null || detail.IsOwnAccount)
        {
            return detail is null ? RedirectToAction(nameof(Usuarios)) : RedirectToAction(nameof(Usuario), new { cuentaId });
        }

        return View(new RenameProfessionalAccountViewModel(detail.Account, new RenameProfessionalAccountFormModel
        {
            CuentaId = cuentaId,
            NombreVisible = detail.Account.DisplayName,
        }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NombreUsuario([Bind(Prefix = "Form")] RenameProfessionalAccountFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var detail = await FindAccountAsync(activeScope, form.CuentaId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Usuarios));
        }

        if (ModelState.IsValid)
        {
            var result = await service.RenameAccountAsync(new RenameProfessionalAccountCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), AccountId.From(form.CuentaId), form.NombreVisible), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Nombre guardado.";
                return RedirectToAction(nameof(Usuario), new { cuentaId = form.CuentaId });
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code == ApplicationFailureCode.InvalidInput
                ? "Escribe un nombre distinto del actual. Tu propia cuenta no se cambia desde aquí."
                : result.Error.Message);
        }

        return View(new RenameProfessionalAccountViewModel(detail.Account, form));
    }

    /// <summary>ADM-12: estado es el que se quiere (Suspended para suspender, Active para reactivar).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EstadoUsuario(Guid cuentaId, AccountStatus estado, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await service.ChangeAccountStatusAsync(new ChangeAccountStatusCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), AccountId.From(cuentaId), estado), ct);
        return AfterAccountChange(result, cuentaId, null,
            estado == AccountStatus.Suspended
                ? "Cuenta suspendida: no podrá entrar con ningún perfil hasta que la reactives."
                : "Cuenta reactivada.",
            invalid: "No se puede cambiar el estado: la cuenta tiene perfiles vigentes en otros centros, o es la tuya.",
            conflict: "La cuenta ya estaba en ese estado.");
    }

    public async Task<IActionResult> ConcederPerfil(Guid cuentaId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(ConcederPerfil), new { cuentaId }) });
        }

        var detail = await FindAccountAsync(activeScope, cuentaId, ct);
        if (detail is null || detail.IsOwnAccount)
        {
            return detail is null ? RedirectToAction(nameof(Usuarios)) : RedirectToAction(nameof(Usuario), new { cuentaId });
        }

        var units = await AdministratorUnitsAsync(activeScope, ct);
        return View(new GrantAccountProfileViewModel(detail.Account, new GrantAccountProfileFormModel
        {
            CuentaId = cuentaId,
            OperacionId = Guid.NewGuid(),
            Unidades = units.Count == 1 ? [units[0].UnitId.Value] : [],
        }, GrantableProfiles(detail), units));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConcederPerfil([Bind(Prefix = "Form")] GrantAccountProfileFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var detail = await FindAccountAsync(activeScope, form.CuentaId, ct);
        if (detail is null)
        {
            return RedirectToAction(nameof(Usuarios));
        }

        if (form.Unidades.Count == 0)
        {
            ModelState.AddModelError("Form.Unidades", "Elige al menos una unidad.");
        }

        if (ModelState.IsValid)
        {
            var result = await service.GrantProfileAsync(new GrantAccountProfileCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), AccountId.From(form.CuentaId), form.OperacionId,
                form.Perfil!.Value, form.Unidades), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Perfil concedido. Vale desde la siguiente pantalla que abra la persona.";
                return RedirectToAction(nameof(PerfilUsuario), new { cuentaId = form.CuentaId, ambitoId = result.Value });
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => "La cuenta ya tiene ese perfil vigente en este centro.",
                ApplicationFailureCode.InvalidInput =>
                    "Elige un perfil y al menos una unidad de tu ámbito. Tu propia cuenta no se cambia desde aquí.",
                _ => result.Error.Message,
            });
        }

        return View(new GrantAccountProfileViewModel(detail.Account, form, GrantableProfiles(detail), await AdministratorUnitsAsync(activeScope, ct)));
    }

    /// <summary>ADM-13: un perfil de la cuenta, con sus unidades, sus residentes (Auxiliar) y su historial.</summary>
    public async Task<IActionResult> PerfilUsuario(Guid cuentaId, Guid ambitoId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(PerfilUsuario), new { cuentaId, ambitoId }) });
        }

        var detail = await FindAccountAsync(activeScope, cuentaId, ct);
        var profile = detail?.Account.Profiles.FirstOrDefault(p => p.ProfileScopeId == ambitoId);
        if (profile is null)
        {
            return detail is null ? RedirectToAction(nameof(Usuarios)) : RedirectToAction(nameof(Usuario), new { cuentaId });
        }

        var model = new AccountProfileViewModel(detail!, profile, await AdministratorUnitsAsync(activeScope, ct), []);
        if (model.CanChange && profile.Profile == SystemProfile.Auxiliar)
        {
            var residents = await service.ListAssignableResidentsAsync(new FindProfessionalAccountQuery(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), AccountId.From(cuentaId)), ambitoId, ct);
            model = model with { AssignableResidents = residents.Value ?? [] };
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevocarPerfil(Guid cuentaId, Guid ambitoId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await service.RevokeProfileAsync(new RevokeAccountProfileCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), AccountId.From(cuentaId), ambitoId), ct);
        return AfterAccountChange(result, cuentaId, ambitoId, "Perfil revocado. Si hace falta otra vez, hay que concederlo de nuevo.",
            invalid: "Este perfil no se puede revocar desde aquí.",
            conflict: "El perfil ya estaba revocado.");
    }

    /// <summary>ADM-13: conceder (conceder = true) o revocar una unidad del perfil.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnidadPerfil(Guid cuentaId, Guid ambitoId, Guid unidadId, bool conceder, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await service.ChangeProfileUnitAsync(new ChangeAccountProfileUnitCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), AccountId.From(cuentaId), ambitoId, UnitId.From(unidadId),
            conceder), ct);
        return AfterAccountChange(result, cuentaId, ambitoId,
            conceder ? "Unidad concedida." : "Unidad revocada.",
            invalid: conceder
                ? "Solo puedes conceder unidades de tu ámbito."
                : "No se puede revocar: es la última unidad del perfil (revoca el perfil) o no es de tu ámbito.",
            conflict: "Las unidades del perfil han cambiado desde que abriste la pantalla. Revisa las vigentes.");
    }

    /// <summary>ADM-13 (0024): conceder (conceder = true) o revocar un permiso del catálogo del perfil.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PermisoPerfil(Guid cuentaId, Guid ambitoId, string? permiso, bool conceder, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await service.ChangeProfilePermissionAsync(new ChangeAccountProfilePermissionCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), AccountId.From(cuentaId), ambitoId, permiso, conceder), ct);
        return AfterAccountChange(result, cuentaId, ambitoId,
            conceder ? "Permiso concedido." : "Permiso revocado.",
            invalid: "Ese permiso no es de este perfil.",
            conflict: "Los permisos del perfil han cambiado desde que abriste la pantalla. Revisa los vigentes.");
    }

    /// <summary>ADM-13: asignar (asignar = true) o retirar un residente de un perfil Auxiliar.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResidentePerfil(Guid cuentaId, Guid ambitoId, Guid residenteId, bool asignar, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await service.ChangeProfileResidentAsync(new ChangeAccountProfileResidentCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), AccountId.From(cuentaId), ambitoId,
            ResidentId.From(residenteId), asignar), ct);
        return AfterAccountChange(result, cuentaId, ambitoId,
            asignar ? "Residente asignado." : "Residente retirado.",
            invalid: "Solo se asignan residentes de las unidades del perfil y de tu ámbito.",
            conflict: "Los residentes asignados han cambiado desde que abriste la pantalla. Revisa los vigentes.");
    }

    /// <summary>ADM-28: auditoría administrativa. Un valor mal formado en la URL se ignora; un periodo imposible se explica.</summary>
    public async Task<IActionResult> Auditoria(AuditFilter filtro, CancellationToken ct)
    {
        ModelState.Clear();
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Request.Path + Request.QueryString });
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var period = filtro.Period();
        var (from, to) = period.Resolve(today);
        var accounts = (await service.ListAccountsAsync(Query(activeScope), ct)).Value ?? [];
        if (period.Validate(today) is { } periodError)
        {
            ModelState.AddModelError(string.Empty, periodError);
            return View(new AuditViewModel(filtro, from, to, null, accounts));
        }

        var result = await estructura.ListAuditAsync(new ListAdministrativeAuditQuery(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), from, to,
            ResidApp.Domain.Audit.AdministrativeAudit.IsAdministrative(filtro.Accion) ? filtro.Accion : null,
            filtro.Cuenta is { } account ? AccountId.From(account) : null), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        return View(new AuditViewModel(filtro, from, to, result.Value, accounts));
    }

    /// <summary>ADM-05 (0025): las unidades concedidas al ámbito de Administración, con alta, renombrado e inactivación.</summary>
    public async Task<IActionResult> Estructura(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Estructura)) });
        }

        var result = await estructura.ListStructureUnitsAsync(Query(activeScope), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        return View(result.Value ?? []);
    }

    public IActionResult NuevaUnidad()
    {
        if (ActiveProfileScopeCookie.Read(Request) is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(NuevaUnidad)) });
        }

        return View(new NewUnitViewModel(new NewUnitFormModel { OperacionId = Guid.NewGuid() }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuevaUnidad([Bind(Prefix = "Form")] NewUnitFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        if (ModelState.IsValid)
        {
            var result = await estructura.CreateUnitAsync(new CreateUnitCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.OperacionId, form.Codigo, form.Nombre), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Unidad creada. Ya la ofrecen el alta de residentes y la gestión de usuarios de tu ámbito.";
                return RedirectToAction(nameof(Estructura));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => "Ya existe una unidad con ese código o con ese nombre en el centro.",
                ApplicationFailureCode.InvalidInput =>
                    "Revisa los datos: el código lleva de 2 a 64 caracteres (letras sin acentos, dígitos, «-» o «_») y el nombre es obligatorio.",
                _ => result.Error.Message,
            });
        }

        return View(new NewUnitViewModel(form));
    }

    public async Task<IActionResult> NombreUnidad(Guid unidadId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(NombreUnidad), new { unidadId }) });
        }

        var unit = await FindStructureUnitAsync(activeScope, unidadId, ct);
        return unit is null
            ? RedirectToAction(nameof(Estructura))
            : View(new RenameUnitViewModel(unit, new RenameUnitFormModel { UnidadId = unidadId, Nombre = unit.Name }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NombreUnidad([Bind(Prefix = "Form")] RenameUnitFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var unit = await FindStructureUnitAsync(activeScope, form.UnidadId, ct);
        if (unit is null)
        {
            return RedirectToAction(nameof(Estructura));
        }

        if (ModelState.IsValid)
        {
            var result = await estructura.RenameUnitAsync(new RenameUnitCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), UnitId.From(form.UnidadId), form.Nombre), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Nombre guardado.";
                return RedirectToAction(nameof(Estructura));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => "Ya existe otra unidad con ese nombre en el centro.",
                ApplicationFailureCode.InvalidInput => "Escribe un nombre distinto del actual.",
                _ => result.Error.Message,
            });
        }

        return View(new RenameUnitViewModel(unit, form));
    }

    /// <summary>ADM-05: Activa es el estado que se quiere (false para inactivar, true para reactivar).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EstadoUnidad(Guid unidadId, bool activa, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await estructura.ChangeUnitStatusAsync(new ChangeUnitStatusCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), UnitId.From(unidadId), activa), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = activa
                ? "Unidad reactivada."
                : "Unidad inactivada: ya no se ofrece en el alta de residentes ni en la gestión de usuarios.";
        }
        else
        {
            TempData["Error"] = result.Error!.Code switch
            {
                ApplicationFailureCode.InvalidInput => "No se puede inactivar una unidad con residentes ubicados en ella.",
                ApplicationFailureCode.Conflict => "La unidad ya estaba en ese estado.",
                _ => result.Error.Message,
            };
        }

        return RedirectToAction(nameof(Estructura));
    }

    private async Task<StructureUnit?> FindStructureUnitAsync(ActiveProfileScopeCookieValue activeScope, Guid unidadId, CancellationToken ct) =>
        (await estructura.ListStructureUnitsAsync(Query(activeScope), ct)).Value?.FirstOrDefault(u => u.UnitId.Value == unidadId);

    /// <summary>Tras un cambio pedido desde la ficha de la cuenta (ambitoId null) o de uno de sus perfiles: el mensaje o
    /// el error vuelven con TempData. Una cuenta ajena vuelve a la lista sin distinguir el motivo.</summary>
    private IActionResult AfterAccountChange(
        ApplicationResult<bool> result, Guid cuentaId, Guid? ambitoId, string done, string invalid, string conflict)
    {
        if (result.Ok)
        {
            TempData["Mensaje"] = done;
        }
        else if (result.Error!.Code == ApplicationFailureCode.AccessDenied)
        {
            return RedirectToAction(nameof(Usuarios));
        }
        else
        {
            TempData["Error"] = result.Error.Code switch
            {
                ApplicationFailureCode.InvalidInput => invalid,
                ApplicationFailureCode.Conflict => conflict,
                _ => result.Error.Message,
            };
        }

        return ambitoId is { } scope
            ? RedirectToAction(nameof(PerfilUsuario), new { cuentaId, ambitoId = scope })
            : RedirectToAction(nameof(Usuario), new { cuentaId });
    }

    private async Task<ProfessionalAccountDetail?> FindAccountAsync(ActiveProfileScopeCookieValue activeScope, Guid cuentaId, CancellationToken ct)
    {
        var result = await service.FindAccountAsync(new FindProfessionalAccountQuery(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), AccountId.From(cuentaId)), ct);
        return result.Ok ? result.Value : null;
    }

    private async Task<IReadOnlyList<ScopeUnit>> AdministratorUnitsAsync(ActiveProfileScopeCookieValue activeScope, CancellationToken ct) =>
        (await service.ListAdministrationUnitsAsync(Query(activeScope), ct)).Value ?? [];

    /// <summary>Los perfiles concedibles que la cuenta no tiene vigentes en este centro.</summary>
    private static IReadOnlyList<SystemProfile> GrantableProfiles(ProfessionalAccountDetail detail) => ProfessionalAccount.GrantableProfiles
        .Where(p => !detail.Account.Profiles.Any(g => g.Active && g.Profile == p))
        .ToList();

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
