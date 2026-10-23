using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>ADR 0007, incremento I2b: dar acceso en este centro a alguien que ya tiene cuenta en otro. Administración emite una
/// vinculación (perfil y unidades, sin decir a qué cuenta) y la hoja se enseña una sola vez; la persona la canjea desde «Mi cuenta» con su
/// segundo factor; y Administración confirma o rechaza, viendo quién la canjeó.</summary>
public sealed partial class AdministracionController
{
    [HttpGet]
    public async Task<IActionResult> Vinculaciones([FromServices] AccountLinksApplicationService links, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Vinculaciones)) });
        }

        return await LinksViewAsync(links, activeScope, new NewAccountLinkFormModel(), ct);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuevaVinculacion(
        [Bind(Prefix = "Form")] NewAccountLinkFormModel form, [FromServices] AccountLinksApplicationService links, CancellationToken ct)
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
            var result = await links.IssueAsync(new IssueAccountLinkCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), form.Perfil, form.Unidades), ct);
            if (result.Ok)
            {
                Response.Headers.CacheControl = "no-store";
                var issued = result.Value!;
                return View("HojaVinculacion", new HojaVinculacionViewModel(
                    issued.Details.CenterName, issued.Details.Profile, issued.Details.UnitNames, issued.Details.ExpiresAt,
                    Url.Action("Vincular", "Cuenta", new { id = issued.Token }, Request.Scheme)!, issued.Token, Url.Action(nameof(Vinculaciones))!));
            }

            if (result.Error!.Code == ApplicationFailureCode.AccessDenied)
            {
                return RedirectToAction(nameof(Usuarios));
            }

            ModelState.AddModelError(string.Empty, result.Error.Code == ApplicationFailureCode.InvalidInput
                ? "Elige un perfil de personal y al menos una unidad de tu ámbito."
                : result.Error.Message);
        }

        return await LinksViewAsync(links, activeScope, form, ct);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnularVinculacion(Guid id, [FromServices] AccountLinksApplicationService links, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await links.CancelAsync(new AccountLinkCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), id), ct);
        return LinkChanged(result, "Vinculación anulada: ya no sirve para pedir el acceso.",
            "Esa vinculación ya no se puede anular: alguien la ha canjeado, la ha anulado otra persona o ha caducado.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolverVinculacion(
        Guid id, bool confirmar, [FromServices] AccountLinksApplicationService links, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await links.ResolveAsync(new AccountLinkCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), id), confirmar, ct);
        if (!result.Ok && result.Error!.Code == ApplicationFailureCode.InvalidInput)
        {
            TempData["Error"] = "No se puede confirmar: la cuenta ya no está activa, ya no tiene el código del móvil o las unidades ya no son de tu ámbito.";
            return RedirectToAction(nameof(Vinculaciones));
        }

        return LinkChanged(result.Ok ? ApplicationResult<bool>.Success(true) : ApplicationResult<bool>.Failed(result.Error!),
            confirmar ? "Vinculación confirmada: la persona ya puede elegir este centro al entrar." : "Vinculación rechazada: no se ha concedido ningún perfil.",
            confirmar ? "No se ha podido confirmar: la persona ya tiene ese perfil en el centro, o la vinculación ya estaba resuelta." : "Esa vinculación ya estaba resuelta.");
    }

    private async Task<IActionResult> LinksViewAsync(
        AccountLinksApplicationService links, ActiveProfileScopeCookieValue activeScope, NewAccountLinkFormModel form, CancellationToken ct)
    {
        var open = await links.ListOpenAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ct);
        if (!open.Ok)
        {
            return RedirectToAction(nameof(Usuarios));
        }

        var units = await AdministratorUnitsAsync(activeScope, ct);
        if (form.Unidades.Count == 0 && units.Count == 1)
        {
            form.Unidades = [units[0].UnitId.Value];
        }

        return View("Vinculaciones", new AccountLinksViewModel(open.Value!, form, units, ProfessionalAccount.GrantableProfiles));
    }

    private IActionResult LinkChanged(ApplicationResult<bool> result, string done, string conflict)
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
            TempData["Error"] = result.Error.Code == ApplicationFailureCode.Conflict ? conflict : result.Error.Message;
        }

        return RedirectToAction(nameof(Vinculaciones));
    }
}
