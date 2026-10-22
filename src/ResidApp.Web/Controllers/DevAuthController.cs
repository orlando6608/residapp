using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Infrastructure.Identity;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Acceso de desarrollo: elegir una cuenta ya sembrada en dbo.cuentas (por ejemplo, «dev-admin» de
/// database/seed/dev_seed_residente_basal.sql) sin contraseña. No es autenticación real: solo existe si DevAuth está habilitado y,
/// fuera de Development, pide además la clave de desarrollo (<see cref="DevAuthOptions"/>). Abre una sesión de verdad (dbo.sesiones y
/// cookie cifrada), como el inicio de sesión con contraseña (AccesoController). También crea una invitación para la cuenta de la sesión
/// (ADR 0007, I2), para probar el camino real sin pasar por Administración.
/// </summary>
[AllowAnonymous]
public sealed class DevAuthController(
    IProfileScopeDirectoryProvider directory, SessionSignIn signIn, UserManager<CredentialAccount> users, IAccessInvitationRepository invitations,
    IAccessEventLog accessLog, TimeProvider clock, IOptions<DevAuthOptions> options, ILogger<DevAuthController> logger) : Controller
{
    private DevAuthOptions Settings => options.Value;

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        // Sin habilitar, el acceso de desarrollo no existe.
        if (!Settings.Habilitado)
        {
            context.Result = NotFound();
        }
    }

    public IActionResult Login(string? returnUrl) => View(LoginModel(returnUrl));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(DevAuthOptions.RateLimitPolicy)]
    public async Task<IActionResult> Login(string? externalSubject, string? clave, string? returnUrl, CancellationToken ct)
    {
        // Sin cuenta activa con algún ámbito activo no se podría operar: se rechaza aquí en vez de dejar la sesión atrapada en la
        // selección de ámbito. La sesión anterior, si la había, se conserva.
        var subject = externalSubject?.Trim();
        if (string.IsNullOrEmpty(subject))
        {
            ModelState.AddModelError(string.Empty, "Escribe el usuario de una cuenta de prueba.");
            return View(LoginModel(returnUrl));
        }

        IReadOnlyList<ActiveProfileScope> scopes = !Settings.KeyMatches(clave)
            ? []
            : await directory.ListActiveAsync(subject, ct);
        if (scopes.Count == 0)
        {
            logger.LogWarning("Inicio de sesión de desarrollo rechazado.");
            // Con clave, el mensaje no dice si el usuario existe (hallazgo B1).
            ModelState.AddModelError(string.Empty, Settings.RequiresKey
                ? "Usuario o clave incorrectos."
                : "No existe ninguna cuenta con ese usuario, o no tiene ningún ámbito activo. Revisa el nombre.");
            return View(LoginModel(returnUrl));
        }

        await signIn.OpenAsync(HttpContext, scopes[0], AccessEventType.EntradaDesarrollo, ct);
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public async Task<IActionResult> Credenciales()
    {
        if (await SessionAccountAsync() is not { } account)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Credenciales)) });
        }
        return View(new DevAuthCredencialesViewModel(account.UserName, account.PasswordHash is not null, account.TwoFactorEnabled));
    }

    /// <summary>Una invitación para la cuenta de la sesión, por el mismo camino que la que entrega Administración (ADR 0007, I2): de
    /// alta si no tiene contraseña, de recuperación si ya la tiene. Anula la que tuviera.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Credenciales(CancellationToken ct)
    {
        if (await SessionAccountAsync() is not { } account)
        {
            return RedirectToAction(nameof(Login));
        }

        var token = AccessInvitation.NewToken();
        var issued = await invitations.IssueForDevelopmentAsync(
            AccountId.From(account.Id), AccessInvitation.Hash(token), clock.GetUtcNow().UtcDateTime, ct);
        await accessLog.RecordAsync(AccessEventType.CredencialesDesarrollo, AccountId.From(account.Id), SessionAuthentication.SessionId(User),
            SessionSignIn.Ip(HttpContext), clock.GetUtcNow().UtcDateTime, ct);
        Response.Headers.CacheControl = "no-store";
        return View("HojaInvitacion", new HojaInvitacionViewModel(
            issued.Kind, issued.Subject, issued.DisplayName, issued.CenterName, issued.ExpiresAt,
            Url.Action("Invitacion", "Acceso", new { id = token }, Request.Scheme)!, Url.Action(nameof(Credenciales))!));
    }

    private DevAuthLoginViewModel LoginModel(string? returnUrl) =>
        new(SessionAuthentication.Subject(User), Settings.RequiresKey, returnUrl);

    private async Task<CredentialAccount?> SessionAccountAsync() =>
        SessionAuthentication.Subject(User) is { } subject ? await users.FindByNameAsync(subject) : null;
}
