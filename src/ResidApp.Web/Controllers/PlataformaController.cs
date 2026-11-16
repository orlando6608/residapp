using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Modules;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Perfil de plataforma (script 0026): lista de centros y alta de un centro con su primera unidad y su primer administrador.
/// Ninguna pantalla muestra residentes ni contenido clínico. Traduce a PlatformApplicationService; la autorización no vive aquí.
/// </summary>
public sealed class PlataformaController(PlatformApplicationService service, IModuleSettings moduleSettings, TimeProvider clock) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Index)) });
        }

        var result = await service.ListCentersAsync(Query(activeScope), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        // ADR 0015: los módulos que vencen pronto (o vencieron sin renovar) de todos los centros, para que nadie se quede sin el servicio por olvido.
        var expiring = await service.ListExpiringModulesAsync(Query(activeScope), ct);
        ViewData["Vencimientos"] = new PlatformExpiringModulesViewModel(expiring.Value ?? [], CenterModuleRules.TodayInMadrid(clock.GetUtcNow()));

        return View(result.Value ?? []);
    }

    public IActionResult NuevoCentro()
    {
        if (ActiveProfileScopeCookie.Read(Request) is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(NuevoCentro)) });
        }

        return View(new NewCenterViewModel(new NewCenterFormModel { OperacionId = Guid.NewGuid() }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(CenterLogoForm.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = CenterLogoForm.MaxRequestBytes)]
    public async Task<IActionResult> NuevoCentro([Bind(Prefix = "Form")] NewCenterFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        byte[]? logo = null;
        if (form.Logo is { Length: > 0 } file)
        {
            if (CenterLogoForm.TooLarge(file))
            {
                ModelState.AddModelError(string.Empty, CenterLogoForm.Invalid);
            }
            else
            {
                logo = await CenterLogoForm.ReadAsync(file, ct);
            }
        }

        if (ModelState.IsValid)
        {
            var result = await service.CreateCenterAsync(new CreateCenterCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.OperacionId, null, form.NombreCentro,
                null, form.NombreUnidad, form.IdentificadorAdministrador, form.NombreAdministrador, logo), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Centro creado. Para que su administrador pueda entrar, pulsa «Administraciones y unidades» en ese centro y luego «Crear invitación de acceso o de recuperación».";
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict =>
                    "Ya existe una cuenta con ese identificador de acceso. Cámbialo y vuelve a intentarlo.",
                ApplicationFailureCode.InvalidInput =>
                    "Revisa los datos: el identificador de acceso lleva de 3 a 200 caracteres sin espacios (letras, dígitos, «.», «_», «-» o «@») y los nombres son obligatorios." + (logo is null ? "" : " " + CenterLogoForm.Invalid),
                _ => result.Error.Message,
            });
        }

        return View(new NewCenterViewModel(form));
    }

    /// <summary>CJ, 2026-10-07 (administracion-ambito-familiares-cargos, 1.1): un centro con sus unidades y sus Administraciones, para marcar la
    /// principal y añadir unidades a una Administración. Sin residentes ni contenido clínico.</summary>
    public async Task<IActionResult> Centro(Guid centroId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Centro), new { centroId }) });
        }

        var result = await service.FindCenterAsync(new PlatformCenterQuery(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), CenterId.From(centroId)), ct);
        if (!result.Ok)
        {
            return RedirectToAction(nameof(Index));
        }

        var modules = await service.ListCenterModulesAsync(new PlatformCenterQuery(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), CenterId.From(centroId)), ct);
        ViewData["Modulos"] = new PlatformCenterModulesViewModel(
            centroId, modules.Value ?? [], CenterModules.All.Where(m => m.UsesThirdParty && !moduleSettings.IsThirdPartyConfigured(m.Code)).Select(m => m.Code).ToHashSet(),
            CenterModuleRules.TodayInMadrid(clock.GetUtcNow()));
        return View(result.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarcarPrincipal(Guid centroId, Guid ambitoId, bool principal, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await service.SetAdministrationPrincipalAsync(new SetPlatformAdministrationPrincipalCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), CenterId.From(centroId), ambitoId, principal), ct);
        TempData[result.Ok ? "Mensaje" : "Error"] = result.Ok
            ? (principal ? "Marcada como Administración principal." : "Ya no es Administración principal.")
            : (result.Error!.Code == ApplicationFailureCode.Conflict ? "Ese perfil ya estaba así. Revisa el centro." : result.Error.Message);
        return RedirectToAction(nameof(Centro), new { centroId });
    }

    /// <summary>ADR 0015: activa o renueva un módulo en un centro, a petición del centro. Las condiciones de la ficha se marcan una a una y han de estar todas.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ActivarModulo(ModuleChangeFormModel form, CancellationToken ct) => ChangeModuleAsync(form, enable: true, ct);

    /// <summary>ADR 0015: apaga un módulo en un centro, a petición del centro y con efecto inmediato; el motivo es de una lista cerrada.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ApagarModulo(ModuleChangeFormModel form, CancellationToken ct) => ChangeModuleAsync(form, enable: false, ct);

    private async Task<IActionResult> ChangeModuleAsync(ModuleChangeFormModel form, bool enable, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var module = CenterModules.Find(form.ModuloCodigo);
        var allConditions = module is not null && module.PrivacyConditions.All(code => form.Condiciones.Contains(code));
        var result = await service.ChangeCenterModuleAsync(new ChangeCenterModuleCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), CenterId.From(form.CentroId), form.ModuloCodigo, enable, form.VigenteDesde, form.VigenteHasta,
            form.ReferenciaAnexo, form.VersionAnexo, allConditions, form.MotivoBaja, MadridInstant(form.SolicitudRecibidaEn), form.Canal), ct);
        TempData[result.Ok ? "Mensaje" : "Error"] = result.Ok
            ? $"{(enable ? "Módulo activado" : "Módulo apagado")} (referencia {result.Value}). Anota esa referencia en tu respuesta al centro. Queda en la auditoría del centro; avisa por escrito a su persona autorizada."
            : result.Error!.Message;
        return RedirectToAction(nameof(Centro), new { centroId = form.CentroId });
    }

    /// <summary>La hora local de Madrid de un campo datetime-local, como instante.</summary>
    private static DateTimeOffset? MadridInstant(DateTime? local)
    {
        if (local is null)
        {
            return null;
        }

        // Una hora de otro siglo (año 0001) no cabe en un instante con desfase: se entrega como una fecha antigua, que el dominio rechaza con su mensaje, en vez de un 500.
        if (local.Value.Year < 2000)
        {
            return DateTimeOffset.UnixEpoch;
        }

        var madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
        var unspecified = DateTime.SpecifyKind(local.Value, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, madrid.GetUtcOffset(unspecified));
    }

    /// <summary>ADR 0007, I2: la invitación de acceso (o de recuperación, si ya tiene contraseña) de una cuenta de Administración del
    /// centro. La hoja con el token se enseña en la respuesta, una sola vez.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Invitacion(Guid centroId, Guid ambitoId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await service.IssueAdministratorInvitationAsync(new PlatformAdministrationCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), CenterId.From(centroId), ambitoId), ct);
        if (!result.Ok)
        {
            TempData["Error"] = result.Error!.Code switch
            {
                ApplicationFailureCode.InvalidInput => "Esa cuenta tiene perfiles vigentes en otros centros: no se le entrega desde aquí.",
                ApplicationFailureCode.Conflict => "La cuenta está suspendida.",
                _ => result.Error.Message,
            };
            return RedirectToAction(nameof(Centro), new { centroId });
        }

        Response.Headers.CacheControl = "no-store";
        var issued = result.Value!;
        return View("HojaInvitacion", new HojaInvitacionViewModel(
            issued.Details.Kind, issued.Details.Subject, issued.Details.DisplayName, issued.Details.CenterName, issued.Details.ExpiresAt,
            Url.Action("Invitacion", "Acceso", new { id = issued.Token }, Request.Scheme)!, Url.Action(nameof(Centro), new { centroId })!));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnadirUnidad(Guid centroId, Guid ambitoId, Guid unidadId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await service.AddUnitToAdministrationAsync(new AddUnitToAdministrationCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), CenterId.From(centroId), ambitoId, UnitId.From(unidadId)), ct);
        TempData[result.Ok ? "Mensaje" : "Error"] = result.Ok
            ? "Unidad añadida al ámbito de esa Administración. Queda en la auditoría."
            : (result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => "Esa Administración ya tiene la unidad.",
                ApplicationFailureCode.InvalidInput => "Esa unidad no existe o no está activa en el centro.",
                _ => result.Error.Message,
            });
        return RedirectToAction(nameof(Centro), new { centroId });
    }

    /// <summary>ADR 0007, I2b: las cuentas con perfiles en varios centros. Ninguna Administración las suspende ni les entrega invitaciones
    /// (cada una solo retira su propio perfil): lo hace la plataforma, con su soporte.</summary>
    public async Task<IActionResult> Cuentas(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Cuentas)) });
        }

        var result = await service.ListMultiCenterAccountsAsync(Query(activeScope), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }

        return View(new PlatformMultiCenterAccountsViewModel(result.Value ?? []));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InvitacionCuenta(Guid cuentaId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await service.IssueMultiCenterInvitationAsync(new PlatformAccountCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), AccountId.From(cuentaId)), ct);
        if (!result.Ok)
        {
            TempData["Error"] = result.Error!.Code switch
            {
                ApplicationFailureCode.InvalidInput => "Esa cuenta ya no tiene perfiles en varios centros, o es la tuya.",
                ApplicationFailureCode.Conflict => "La cuenta está suspendida.",
                _ => result.Error.Message,
            };
            return RedirectToAction(nameof(Cuentas));
        }

        Response.Headers.CacheControl = "no-store";
        var issued = result.Value!;
        return View("HojaInvitacion", new HojaInvitacionViewModel(
            issued.Details.Kind, issued.Details.Subject, issued.Details.DisplayName, issued.Details.CenterName, issued.Details.ExpiresAt,
            Url.Action("Invitacion", "Acceso", new { id = issued.Token }, Request.Scheme)!, Url.Action(nameof(Cuentas))!));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EstadoCuenta(Guid cuentaId, AccountStatus estado, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await service.ChangeMultiCenterAccountStatusAsync(new PlatformAccountCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), AccountId.From(cuentaId)), estado, ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = estado == AccountStatus.Suspended
                ? "Cuenta suspendida en todos sus centros: no podrá entrar hasta que la reactives."
                : "Cuenta reactivada.";
        }
        else
        {
            TempData["Error"] = result.Error!.Code switch
            {
                ApplicationFailureCode.InvalidInput => "Esa cuenta ya no tiene perfiles en varios centros, o es la tuya.",
                ApplicationFailureCode.Conflict => "La cuenta ya estaba en ese estado.",
                _ => result.Error.Message,
            };
        }

        return RedirectToAction(nameof(Cuentas));
    }

    private static PlatformQuery Query(ActiveProfileScopeCookieValue activeScope) =>
        new(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId));
}
