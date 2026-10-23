using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ResidApp.Application.Ports;
using ResidApp.Application.Errors;
using ResidApp.Application.Sessions;
using ResidApp.Application.UseCases;
using ResidApp.Infrastructure.Identity;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// «Mi cuenta» (ADM-30 y FAM-16, ADR 0007 I3): la propia contraseña, los códigos de recuperación y las sesiones abiertas. Es de la cuenta
/// de la sesión y de nadie más: todo se resuelve con su sujeto, y cerrar una sesión solo actúa sobre las de esa cuenta. Dar de alta,
/// quitar o cambiar el segundo factor no está aquí a propósito: solo se prepara desde una invitación (decisión de Orlando, 2026-10-22).
/// Cambiar la contraseña y generar códigos nuevos vuelven a pedir la contraseña actual, y los fallos cuentan para el bloqueo de la
/// cuenta y para el límite por IP, como al entrar.
/// </summary>
[Authorize]
public sealed class CuentaController(
    UserManager<CredentialAccount> users, IProfileScopeDirectoryProvider directory, ISessionStore sessions, IAccessEventLog accessLog,
    AccessFailureThrottle throttle, SessionPolicy policy, AccountLinksApplicationService links, TimeProvider clock,
    ILogger<CuentaController> logger) : Controller
{
    private const string AvisoKey = "CuentaAviso";
    private const int RecentEvents = 10;
    private const string WrongPasswordMessage = "La contraseña actual no es correcta. Tras 5 intentos fallidos seguidos, la cuenta se bloquea 15 minutos.";
    private const string InvalidLinkMessage = "Ese código no vale: no existe, ya se ha usado, se ha anulado o ha caducado. Pide otra hoja a la Administración del centro.";
    private const string NeedsSecondFactorMessage = "El acceso a otro centro es para el personal: tu cuenta no tiene el código del móvil preparado.";
    private const string LockedMessage = "La cuenta está bloqueada unos minutos por demasiados intentos fallidos. Vuelve a intentarlo más tarde.";

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        await CurrentAccountAsync() is { } account ? View(await ModelAsync(account, ct)) : Challenge();

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AccesoOptions.RateLimitPolicy)]
    public async Task<IActionResult> CambiarContrasena(string? actual, string? nueva, string? confirmacion, CancellationToken ct)
    {
        if (await CurrentAccountAsync() is not { } account)
        {
            return Challenge();
        }
        if (throttle.IsBlocked(SessionSignIn.Ip(HttpContext)))
        {
            return TooManyFailures();
        }

        string? error = null;
        if (account.PasswordHash is null)
        {
            error = "Tu cuenta todavía no tiene contraseña. Pídele una invitación a la Administración de tu centro.";
        }
        else if (string.IsNullOrEmpty(actual) || string.IsNullOrEmpty(nueva))
        {
            error = "Escribe la contraseña actual y la nueva.";
        }
        else if (nueva.Length < AccesoOptions.LongitudMinimaContrasena)
        {
            error = $"La contraseña nueva tiene que tener al menos {AccesoOptions.LongitudMinimaContrasena} caracteres.";
        }
        else if (!string.Equals(nueva, confirmacion, StringComparison.Ordinal))
        {
            error = "Las dos contraseñas nuevas no coinciden.";
        }
        else if (string.Equals(nueva, actual, StringComparison.Ordinal))
        {
            error = "La contraseña nueva tiene que ser distinta de la actual.";
        }
        else if (await VerifyPasswordAsync(account, actual, AccessEventType.ContrasenaFallida, ct) is { } failure)
        {
            error = failure;
        }

        if (error is not null)
        {
            return View("Index", await ModelAsync(account, ct, errorContrasena: error));
        }

        var accountId = AccountId.From(account.Id);
        if (!(await users.ChangePasswordAsync(account, actual!, nueva!)).Succeeded)
        {
            return View("Index", await ModelAsync(account, ct, errorContrasena: "No se ha podido cambiar la contraseña. Prueba con otra."));
        }

        await users.ResetAccessFailedCountAsync(account);
        // Quien tuviera la contraseña antigua pudo dejar otras sesiones abiertas: solo se conserva esta.
        var current = SessionAuthentication.SessionId(User);
        if (current is { } keep)
        {
            await sessions.CloseOthersAsync(accountId, keep, SessionCloseReason.CierrePropio, Now, ct);
        }
        await accessLog.RecordAsync(AccessEventType.ContrasenaCambiada, accountId, current, SessionSignIn.Ip(HttpContext), Now, ct);
        TempData[AvisoKey] = "Contraseña cambiada. Se han cerrado las demás sesiones abiertas de tu cuenta.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AccesoOptions.RateLimitPolicy)]
    public async Task<IActionResult> RegenerarCodigos(string? contrasena, string? codigo, CancellationToken ct)
    {
        if (await CurrentAccountAsync() is not { } account)
        {
            return Challenge();
        }
        if (throttle.IsBlocked(SessionSignIn.Ip(HttpContext)))
        {
            return TooManyFailures();
        }
        if (!account.TwoFactorEnabled)
        {
            return View("Index", await ModelAsync(account, ct, errorCodigos: "Tu cuenta no tiene el código del móvil preparado, así que no tiene códigos de recuperación."));
        }
        if (string.IsNullOrEmpty(contrasena) || string.IsNullOrWhiteSpace(codigo))
        {
            return View("Index", await ModelAsync(account, ct, errorCodigos: "Escribe tu contraseña y el código de tu móvil."));
        }

        if (await VerifyPasswordAsync(account, contrasena, AccessEventType.ContrasenaFallida, ct) is { } passwordError)
        {
            return View("Index", await ModelAsync(account, ct, errorCodigos: passwordError));
        }
        if (!await users.VerifyTwoFactorTokenAsync(account, users.Options.Tokens.AuthenticatorTokenProvider, CleanCode(codigo)))
        {
            await FailAsync(account, AccessEventType.SegundoFactorFallido, ct);
            return View("Index", await ModelAsync(account, ct, errorCodigos: "El código del móvil no es correcto. Comprueba que es el de ResidApp y que no ha cambiado ya."));
        }

        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(account, 10))!.ToList();
        await users.ResetAccessFailedCountAsync(account);
        await accessLog.RecordAsync(AccessEventType.CodigosRegenerados, AccountId.From(account.Id),
            SessionAuthentication.SessionId(User), SessionSignIn.Ip(HttpContext), Now, ct);

        // Los códigos solo se enseñan en esta respuesta: sin redirección y sin que el navegador la guarde.
        Response.Headers.CacheControl = "no-store";
        return View("~/Views/Acceso/CodigosRecuperacion.cshtml", new CodigosRecuperacionViewModel(
            codes, Url.Action(nameof(Index))!, "Códigos de recuperación nuevos"));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CerrarSesion(Guid id, CancellationToken ct)
    {
        if (await CurrentAccountAsync() is not { } account)
        {
            return Challenge();
        }

        var accountId = AccountId.From(account.Id);
        var current = SessionAuthentication.SessionId(User);
        if (id == current)
        {
            TempData[AvisoKey] = "Esta es la sesión en la que estás. Para cerrarla, usa «Salir» en el menú.";
        }
        else if (await sessions.CloseOwnedAsync(accountId, id, SessionCloseReason.CierrePropio, Now, ct))
        {
            await accessLog.RecordAsync(AccessEventType.SesionCerrada, accountId, current, SessionSignIn.Ip(HttpContext), Now, ct);
            TempData[AvisoKey] = "Sesión cerrada.";
        }
        else
        {
            TempData[AvisoKey] = "Esa sesión ya estaba cerrada.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CerrarOtrasSesiones(CancellationToken ct)
    {
        if (await CurrentAccountAsync() is not { } account || SessionAuthentication.SessionId(User) is not { } current)
        {
            return Challenge();
        }

        var accountId = AccountId.From(account.Id);
        await sessions.CloseOthersAsync(accountId, current, SessionCloseReason.CierrePropio, Now, ct);
        await accessLog.RecordAsync(AccessEventType.SesionCerrada, accountId, current, SessionSignIn.Ip(HttpContext), Now, ct);
        TempData[AvisoKey] = "Se han cerrado las demás sesiones abiertas de tu cuenta.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>La hoja de la Administración de otro centro (ADR 0007, I2b), por su enlace o su código: se enseña lo que ofrece y la
    /// persona acepta o no. El token llega por la dirección solo si escanea el QR; lo escrito a mano va por POST.</summary>
    [HttpGet]
    public async Task<IActionResult> Vincular(string? id, CancellationToken ct) => await ShowLinkAsync(id, ct);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AccesoOptions.RateLimitPolicy)]
    [ActionName(nameof(Vincular))]
    public async Task<IActionResult> VincularCodigo(string? codigo, CancellationToken ct) => await ShowLinkAsync(codigo, ct);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AccesoOptions.RateLimitPolicy)]
    public async Task<IActionResult> AceptarVinculacion(string? token, CancellationToken ct)
    {
        if (await CurrentAccountAsync() is not { } account)
        {
            return Challenge();
        }
        if (throttle.IsBlocked(SessionSignIn.Ip(HttpContext)))
        {
            return TooManyFailures();
        }

        var link = (await links.FindAsync(LinkToken(token), ct)).Value;
        if (link is null)
        {
            throttle.RecordFailure(SessionSignIn.Ip(HttpContext));
            return View("Index", await ModelAsync(account, ct, errorVinculacion: InvalidLinkMessage));
        }
        if (!account.TwoFactorEnabled)
        {
            return View("Index", await ModelAsync(account, ct, errorVinculacion: NeedsSecondFactorMessage));
        }

        var accountId = AccountId.From(account.Id);
        var result = await links.RedeemAsync(accountId, link.Id, ct);
        if (!result.Ok)
        {
            return View("Index", await ModelAsync(account, ct, errorVinculacion: result.Error!.Code == ApplicationFailureCode.Conflict
                ? $"Ya tienes el perfil {SystemProfileDisplay.Label(link.Profile)} en {link.CenterName}."
                : InvalidLinkMessage));
        }

        await accessLog.RecordAsync(AccessEventType.VinculacionAceptada, accountId, SessionAuthentication.SessionId(User),
            SessionSignIn.Ip(HttpContext), Now, ct);
        TempData[AvisoKey] = $"Has aceptado el acceso a {link.CenterName}. Su Administración tiene que confirmar que eres tú; hasta entonces no verás ese centro.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> ShowLinkAsync(string? input, CancellationToken ct)
    {
        if (await CurrentAccountAsync() is not { } account)
        {
            return Challenge();
        }
        if (throttle.IsBlocked(SessionSignIn.Ip(HttpContext)))
        {
            return TooManyFailures();
        }

        var token = LinkToken(input);
        var link = (await links.FindAsync(token, ct)).Value;
        if (link is null)
        {
            throttle.RecordFailure(SessionSignIn.Ip(HttpContext));
            return View("Index", await ModelAsync(account, ct, errorVinculacion: InvalidLinkMessage));
        }
        if (!account.TwoFactorEnabled)
        {
            return View("Index", await ModelAsync(account, ct, errorVinculacion: NeedsSecondFactorMessage));
        }

        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers.CacheControl = "no-store";
        return View("Vincular", new AceptarVinculacionViewModel(link, token!));
    }

    /// <summary>Acepta el código suelto o la dirección completa de la hoja: se queda con lo que va tras la última barra.</summary>
    private static string? LinkToken(string? input)
    {
        var text = input?.Trim().Split('?')[0].TrimEnd('/');
        return string.IsNullOrEmpty(text) ? null : text[(text.LastIndexOf('/') + 1)..];
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task<CredentialAccount?> CurrentAccountAsync() =>
        SessionAuthentication.Subject(User) is { } subject ? await users.FindByNameAsync(subject) : null;

    private ObjectResult TooManyFailures() => StatusCode(StatusCodes.Status429TooManyRequests,
        "Demasiados accesos fallidos desde esta red. Espera unos minutos y vuelve a intentarlo.");

    private static string CleanCode(string? code) => (code ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);

    /// <summary>Null si la contraseña es la de la cuenta; si no, el mensaje para la pantalla (y el fallo queda contado).</summary>
    private async Task<string?> VerifyPasswordAsync(CredentialAccount account, string password, AccessEventType failure, CancellationToken ct)
    {
        if (await users.IsLockedOutAsync(account))
        {
            return LockedMessage;
        }
        if (await users.CheckPasswordAsync(account, password))
        {
            return null;
        }

        await FailAsync(account, failure, ct);
        return await users.IsLockedOutAsync(account) ? LockedMessage : WrongPasswordMessage;
    }

    /// <summary>Anota el fallo y lo cuenta para el bloqueo de la cuenta y para el límite por IP, como en el inicio de sesión.</summary>
    private async Task FailAsync(CredentialAccount account, AccessEventType type, CancellationToken ct)
    {
        var accountId = AccountId.From(account.Id);
        var ip = SessionSignIn.Ip(HttpContext);
        throttle.RecordFailure(ip);
        await accessLog.RecordAsync(type, accountId, SessionAuthentication.SessionId(User), ip, Now, ct);
        logger.LogWarning("Fallo en Mi cuenta ({Tipo}).", type.ToCode());
        await users.AccessFailedAsync(account);
        if (await users.IsLockedOutAsync(account))
        {
            await accessLog.RecordAsync(AccessEventType.Bloqueo, accountId, SessionAuthentication.SessionId(User), ip, Now, ct);
            logger.LogWarning("Cuenta bloqueada {Minutos} minutos tras {Intentos} intentos fallidos.",
                AccesoOptions.DuracionBloqueo.TotalMinutes, AccesoOptions.IntentosAntesDeBloquear);
        }
    }

    private async Task<CuentaViewModel> ModelAsync(
        CredentialAccount account, CancellationToken ct, string? errorContrasena = null, string? errorCodigos = null, string? errorVinculacion = null)
    {
        var accountId = AccountId.From(account.Id);
        var scopes = await directory.ListActiveAsync(account.UserName, ct);
        var now = Now;
        var current = SessionAuthentication.SessionId(User);
        var open = (await sessions.ListOpenAsync(accountId, now, ct)).Where(s => now - s.LastActivityAt <= policy.IdleTimeout);
        var events = await accessLog.ListRecentAsync(accountId, RecentEvents, ct);
        return new CuentaViewModel
        {
            Usuario = account.UserName,
            Nombre = scopes.Select(s => s.AccountDisplayName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? account.UserName,
            Ambitos = scopes.Select(s => $"{SystemProfileDisplay.Label(s.Profile)} · {s.CenterName}").ToList(),
            TieneContrasena = account.PasswordHash is not null,
            SegundoFactorActivo = account.TwoFactorEnabled,
            CodigosRestantes = account.TwoFactorEnabled ? await users.CountRecoveryCodesAsync(account) : 0,
            Sesiones = open.Select(s => new SesionAbiertaViewModel(s.Id, s.CreatedAt, s.LastActivityAt, s.ExpiresAt, s.Ip, s.Id == current)).ToList(),
            Accesos = events.Select(e => new AccesoRecienteViewModel(
                e.OccurredAt, AccesoRecienteDisplay.Descripcion(e.Type), AccesoRecienteDisplay.EsFallo(e.Type), e.Ip)).ToList(),
            Aviso = TempData[AvisoKey] as string,
            ErrorContrasena = errorContrasena,
            ErrorCodigos = errorCodigos,
            ErrorVinculacion = errorVinculacion,
        };
    }
}
