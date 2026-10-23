using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
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
    IPasskeyHandler<CredentialAccount> passkeyHandler, PasskeyStateCookie passkeyState, IOptions<AccesoOptions> acceso,
    ILogger<CuentaController> logger) : Controller
{
    /// <summary>Tope de passkeys por cuenta: un móvil, un portátil, una llave y alguna de reserva.</summary>
    public const int MaxPasskeys = 10;
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

    /// <summary>Primer paso del alta de una passkey (ADR 0007, I4): como generar códigos de recuperación, pide la contraseña y el código del
    /// móvil vigente. Si valen, devuelve las opciones de WebAuthn y deja el estado en una cookie ligada a esta cuenta y a esta sesión.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AccesoOptions.RateLimitPolicy)]
    public async Task<IActionResult> PasskeyOpciones(string? contrasena, string? codigo, CancellationToken ct)
    {
        if (!acceso.Value.PasskeysActivas)
        {
            return NotFound();
        }
        if (await CurrentAccountAsync() is not { } account || SessionAuthentication.SessionId(User) is not { } sessionId)
        {
            return Challenge();
        }
        if (throttle.IsBlocked(SessionSignIn.Ip(HttpContext)))
        {
            return TooManyFailures();
        }
        if (!account.TwoFactorEnabled)
        {
            return PasskeyError("Tu perfil entra solo con la contraseña: las passkeys son para el personal.");
        }
        if (string.IsNullOrEmpty(contrasena) || string.IsNullOrWhiteSpace(codigo))
        {
            return PasskeyError("Escribe tu contraseña y el código de tu móvil.");
        }
        if (await VerifyPasswordAsync(account, contrasena, AccessEventType.ContrasenaFallida, ct) is { } passwordError)
        {
            return PasskeyError(passwordError);
        }
        if (!await users.VerifyTwoFactorTokenAsync(account, users.Options.Tokens.AuthenticatorTokenProvider, CleanCode(codigo)))
        {
            await FailAsync(account, AccessEventType.SegundoFactorFallido, ct);
            return PasskeyError("El código del móvil no es correcto. Comprueba que es el de ResidApp y que no ha cambiado ya.");
        }
        if ((await users.GetPasskeysAsync(account)).Count >= MaxPasskeys)
        {
            return PasskeyError($"Ya tienes {MaxPasskeys} passkeys, que es el máximo. Quita alguna antes de añadir otra.");
        }

        await users.ResetAccessFailedCountAsync(account);
        var name = await DisplayNameAsync(account, ct);
        var options = await passkeyHandler.MakeCreationOptionsAsync(
            new PasskeyUserEntity { Id = account.Id.ToString("D"), Name = account.UserName, DisplayName = name }, HttpContext);
        passkeyState.Write(Response, PasskeyCeremony.Alta,
            options.AttestationState ?? throw new InvalidOperationException("Sin estado de la passkey."), sessionId);
        return Content(options.CreationOptionsJson, "application/json");
    }

    /// <summary>Segundo paso: valida la credencial que ha creado el dispositivo y la guarda en esta cuenta. El estado debe ser el que se
    /// pidió desde esta misma sesión y la passkey, de esta misma cuenta.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AccesoOptions.RateLimitPolicy)]
    public async Task<IActionResult> PasskeyAlta([FromBody] PasskeyAltaRequest request, CancellationToken ct)
    {
        if (!acceso.Value.PasskeysActivas)
        {
            return NotFound();
        }
        if (await CurrentAccountAsync() is not { } account || SessionAuthentication.SessionId(User) is not { } sessionId)
        {
            return Challenge();
        }

        var state = passkeyState.Take(Request, Response, PasskeyCeremony.Alta);
        if (state is null || state.SessionId != sessionId || string.IsNullOrEmpty(request.CredentialJson))
        {
            return PasskeyError("El registro ha caducado o no es el que pediste. Vuelve a empezar.");
        }

        var attestation = await passkeyHandler.PerformAttestationAsync(new()
        {
            HttpContext = HttpContext, CredentialJson = request.CredentialJson, AttestationState = state.State,
        });
        if (!attestation.Succeeded || !string.Equals(attestation.UserEntity.Id, account.Id.ToString("D"), StringComparison.Ordinal))
        {
            logger.LogWarning("Alta de passkey rechazada: {Motivo}", attestation.Failure?.Message);
            await accessLog.RecordAsync(AccessEventType.PasskeyFallida, AccountId.From(account.Id), sessionId, SessionSignIn.Ip(HttpContext), Now, ct);
            return PasskeyError("No se ha podido registrar la passkey. Vuelve a intentarlo.");
        }

        var passkey = attestation.Passkey;
        passkey.Name = PasskeyName(request.Nombre);
        try
        {
            await users.AddOrUpdatePasskeyAsync(account, passkey);
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            return PasskeyError("Esa passkey ya está registrada.");
        }

        await accessLog.RecordAsync(AccessEventType.PasskeyAlta, AccountId.From(account.Id), sessionId, SessionSignIn.Ip(HttpContext), Now, ct);
        TempData[AvisoKey] = $"Passkey «{passkey.Name}» añadida. Ya puedes entrar con ella desde «Entrar con passkey».";
        return Json(new { ok = true });
    }

    /// <summary>Quita una passkey de esta cuenta. Una cuenta de Plataforma no puede quedarse sin ninguna: entraría con contraseña y código,
    /// que es justo lo que la passkey obligatoria sustituye.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuitarPasskey(string? id, CancellationToken ct)
    {
        if (await CurrentAccountAsync() is not { } account)
        {
            return Challenge();
        }

        var passkeys = await users.GetPasskeysAsync(account);
        var passkey = passkeys.FirstOrDefault(p => PasskeyId(p) == id);
        if (passkey is null)
        {
            TempData[AvisoKey] = "Esa passkey ya no estaba.";
        }
        else if (passkeys.Count == 1 && await IsPlatformAsync(account, ct))
        {
            TempData[AvisoKey] = "Tu cuenta de Plataforma necesita al menos una passkey: añade otra antes de quitar ésta.";
        }
        else
        {
            await users.RemovePasskeyAsync(account, passkey.CredentialId);
            await accessLog.RecordAsync(AccessEventType.PasskeyBaja, AccountId.From(account.Id), SessionAuthentication.SessionId(User),
                SessionSignIn.Ip(HttpContext), Now, ct);
            TempData[AvisoKey] = $"Passkey «{passkey.Name}» quitada.";
        }
        return RedirectToAction(nameof(Index));
    }

    private JsonResult PasskeyError(string message)
    {
        var result = Json(new { error = message });
        result.StatusCode = StatusCodes.Status400BadRequest;
        return result;
    }

    /// <summary>El nombre con que se reconoce la passkey en la lista; sin nombre, la fecha de alta.</summary>
    private string PasskeyName(string? requested)
    {
        var name = requested?.Trim();
        return string.IsNullOrEmpty(name) ? $"Passkey del {Now:dd/MM/yyyy}" : name.Length > 100 ? name[..100] : name;
    }

    private async Task<string> DisplayNameAsync(CredentialAccount account, CancellationToken ct) =>
        (await directory.ListActiveAsync(account.UserName, ct)).Select(s => s.AccountDisplayName)
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? account.UserName;

    private async Task<bool> IsPlatformAsync(CredentialAccount account, CancellationToken ct) =>
        (await directory.ListActiveAsync(account.UserName, ct)).Any(s => s.Profile == SystemProfile.Plataforma);

    /// <summary>El identificador de la passkey en formularios y direcciones.</summary>
    private static string PasskeyId(UserPasskeyInfo passkey) => Microsoft.AspNetCore.WebUtilities.Base64UrlTextEncoder.Encode(passkey.CredentialId);

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
            Passkeys = (await users.GetPasskeysAsync(account)).Select(p => new PasskeyViewModel(
                PasskeyId(p), p.Name ?? "Passkey", p.CreatedAt.UtcDateTime, p.IsBackedUp)).ToList(),
            PasskeysActivas = acceso.Value.PasskeysActivas,
            PasskeyObligatoria = acceso.Value.PasskeysActivas && scopes.Any(s => s.Profile == SystemProfile.Plataforma),
            Aviso = TempData[AvisoKey] as string,
            ErrorContrasena = errorContrasena,
            ErrorCodigos = errorCodigos,
            ErrorVinculacion = errorVinculacion,
        };
    }
}
