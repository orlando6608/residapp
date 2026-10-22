using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ResidApp.Application.Ports;
using ResidApp.Infrastructure.Identity;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Acceso de desarrollo: elegir una cuenta ya sembrada en dbo.cuentas (por ejemplo, «dev-admin» de
/// database/seed/dev_seed_residente_basal.sql) sin contraseña. No es autenticación real: solo existe si DevAuth está habilitado y,
/// fuera de Development, pide además la clave de desarrollo (<see cref="DevAuthOptions"/>). Abre una sesión de verdad (dbo.sesiones y
/// cookie cifrada), como el inicio de sesión con contraseña (AccesoController). Mientras no haya invitaciones (ADR 0007, I2), también
/// sirve para poner una contraseña a la cuenta de la sesión y probar con ella el inicio de sesión real.
/// </summary>
[AllowAnonymous]
public sealed class DevAuthController(
    IProfileScopeDirectoryProvider directory, SessionSignIn signIn, UserManager<CredentialAccount> users, IAccessEventLog accessLog,
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
        return View(CredencialesModel(account, null));
    }

    /// <summary>Pone o cambia la contraseña de la cuenta de la sesión, y la desbloquea.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Credenciales(string? contrasena, string? confirmacion, CancellationToken ct)
    {
        if (await SessionAccountAsync() is not { } account)
        {
            return RedirectToAction(nameof(Login));
        }

        if ((contrasena?.Length ?? 0) < AccesoOptions.LongitudMinimaContrasena)
        {
            ModelState.AddModelError(string.Empty, $"La contraseña tiene que tener al menos {AccesoOptions.LongitudMinimaContrasena} caracteres.");
            return View(CredencialesModel(account, null));
        }
        if (!string.Equals(contrasena, confirmacion, StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "Las dos contraseñas no coinciden.");
            return View(CredencialesModel(account, null));
        }

        if (account.PasswordHash is not null)
        {
            await users.RemovePasswordAsync(account);
        }
        var result = await users.AddPasswordAsync(account, contrasena!);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "No se ha podido guardar la contraseña.");
            return View(CredencialesModel(account, null));
        }
        await users.SetLockoutEndDateAsync(account, null);
        await users.ResetAccessFailedCountAsync(account);
        await RecordAsync(account, ct);
        return View(CredencialesModel(account, "Contraseña guardada. Ya puedes entrar con ella en «Iniciar sesión»."));
    }

    /// <summary>Quita el segundo factor (y cambia su clave), para volver a probar el alta en el siguiente inicio de sesión.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuitarSegundoFactor(CancellationToken ct)
    {
        if (await SessionAccountAsync() is not { } account)
        {
            return RedirectToAction(nameof(Login));
        }

        await users.SetTwoFactorEnabledAsync(account, false);
        await users.ResetAuthenticatorKeyAsync(account);
        await RecordAsync(account, ct);
        return View(nameof(Credenciales), CredencialesModel(account,
            "Segundo factor quitado. Al entrar con la contraseña se pedirá darlo de alta otra vez, con una clave nueva."));
    }

    private DevAuthLoginViewModel LoginModel(string? returnUrl) =>
        new(SessionAuthentication.Subject(User), Settings.RequiresKey, returnUrl);

    private async Task<CredentialAccount?> SessionAccountAsync() =>
        SessionAuthentication.Subject(User) is { } subject ? await users.FindByNameAsync(subject) : null;

    private static DevAuthCredencialesViewModel CredencialesModel(CredentialAccount account, string? aviso) =>
        new(account.UserName, account.PasswordHash is not null, account.TwoFactorEnabled, aviso);

    private Task RecordAsync(CredentialAccount account, CancellationToken ct) =>
        accessLog.RecordAsync(AccessEventType.CredencialesDesarrollo, AccountId.From(account.Id), SessionAuthentication.SessionId(User),
            SessionSignIn.Ip(HttpContext), clock.GetUtcNow().UtcDateTime, ct);
}
