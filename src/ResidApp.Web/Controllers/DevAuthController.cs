using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ResidApp.Application.Ports;
using ResidApp.Application.Sessions;
using ResidApp.Application.UseCases;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Acceso de desarrollo: elegir una cuenta ya sembrada en dbo.cuentas (por ejemplo, «dev-admin» de
/// database/seed/dev_seed_residente_basal.sql) sin contraseña. No es autenticación real: solo existe si DevAuth está habilitado y,
/// fuera de Development, pide además la clave de desarrollo (<see cref="DevAuthOptions"/>). Abre una sesión de verdad (dbo.sesiones y
/// cookie cifrada), así que el resto de la aplicación ya funciona como funcionará con el proveedor de identidad (ADR 0007).
/// </summary>
[AllowAnonymous]
public sealed class DevAuthController(
    IProfileScopeDirectoryProvider directory, ClinicalAccessDeclarations declarations, ISessionStore sessions, SessionPolicy policy,
    TimeProvider clock, IOptions<DevAuthOptions> options, ILogger<DevAuthController> logger) : Controller
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

        await EndCurrentSessionAsync(SessionCloseReason.CambioIdentidad, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var sessionId = Guid.NewGuid();
        await sessions.OpenAsync(sessionId, scopes[0].AccountId, now, now + policy.MaxDuration, ct);
        // El sujeto de la cookie es el de la cuenta tal como está guardado, no como se escribió.
        var stored = await sessions.FindAsync(sessionId, ct);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            SessionAuthentication.Principal(stored!.ExternalSubject, sessionId, scopes[0].AccountDisplayName));
        ActiveProfileScopeCookie.Clear(Response);
        ClinicalAccessDeclarationCookie.Clear(Response);
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await EndCurrentSessionAsync(SessionCloseReason.Logout, ct);
        ActiveProfileScopeCookie.Clear(Response);
        ClinicalAccessDeclarationCookie.Clear(Response);
        return RedirectToAction("Index", "Home");
    }

    /// <summary>Maqueta visual, no funcional: el alta de cuentas llegará con el proveedor de identidad (ADR 0007).</summary>
    public IActionResult Register() => View();

    private DevAuthLoginViewModel LoginModel(string? returnUrl) =>
        new(SessionAuthentication.Subject(User), Settings.RequiresKey, returnUrl);

    private async Task EndCurrentSessionAsync(SessionCloseReason reason, CancellationToken ct)
    {
        if (SessionAuthentication.SessionId(User) is not { } sessionId)
        {
            return;
        }

        // Cerrar sesión termina las declaraciones de acceso clínico (CJ, 2026-10-06), mientras la identidad y el ámbito aún están.
        await declarations.EndAllAsync(ct);
        await sessions.CloseAsync(sessionId, reason, clock.GetUtcNow().UtcDateTime, ct);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
