using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>ADR 0007, incremento I2: Administración entrega la invitación de acceso (o de recuperación) de una cuenta de su centro, desde
/// su ficha o desde la autorización del familiar. La hoja con el token se enseña en la respuesta, una sola vez.</summary>
public sealed partial class AdministracionController
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Invitacion(
        string? usuario, string? volver, [FromServices] AccessInvitationsApplicationService invitations, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var back = Url.IsLocalUrl(volver) ? volver! : Url.Action(nameof(Usuarios))!;
        var result = await invitations.IssueAsync(new AccountInvitationCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), usuario), ct);
        if (!result.Ok)
        {
            return InvitationFailed(result.Error!, back);
        }

        Response.Headers.CacheControl = "no-store";
        var issued = result.Value!;
        return View("HojaInvitacion", new HojaInvitacionViewModel(
            issued.Details.Kind, issued.Details.Subject, issued.Details.DisplayName, issued.Details.CenterName, issued.Details.ExpiresAt,
            Url.Action("Invitacion", "Acceso", new { id = issued.Token }, Request.Scheme)!, back));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnularInvitacion(
        string? usuario, string? volver, [FromServices] AccessInvitationsApplicationService invitations, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var back = Url.IsLocalUrl(volver) ? volver! : Url.Action(nameof(Usuarios))!;
        var result = await invitations.CancelAsync(new AccountInvitationCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), usuario), ct);
        if (!result.Ok)
        {
            return InvitationFailed(result.Error!, back);
        }

        TempData["Mensaje"] = "Invitación anulada: ya no sirve para entrar.";
        return LocalRedirect(back);
    }

    /// <summary>El estado de la invitación de la cuenta para el parcial _EstadoInvitacion; null si no se puede leer.</summary>
    private async Task<EstadoInvitacionViewModel?> InvitationStatusAsync(
        ActiveProfileScopeCookieValue activeScope, string subject, string back, AccessInvitationsApplicationService invitations,
        CancellationToken ct)
    {
        var result = await invitations.FindStatusAsync(new AccountInvitationCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), subject), ct);
        return result.Ok ? new EstadoInvitacionViewModel(subject, result.Value!.HasPassword, result.Value.LiveExpiresAt, back) : null;
    }

    private IActionResult InvitationFailed(ApplicationFailure error, string back)
    {
        if (error.Code == ApplicationFailureCode.AccessDenied)
        {
            return RedirectToAction(nameof(Usuarios));
        }

        TempData["Error"] = error.Code switch
        {
            ApplicationFailureCode.InvalidInput =>
                "No se puede entregar una invitación a esta cuenta: es la tuya o tiene perfiles vigentes en otros centros.",
            ApplicationFailureCode.Conflict => "La cuenta está suspendida, o la invitación ya se había usado o anulado.",
            _ => error.Message,
        };
        return LocalRedirect(back);
    }
}
