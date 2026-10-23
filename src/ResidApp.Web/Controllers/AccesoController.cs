using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ResidApp.Application.Ports;
using ResidApp.Application.Sessions;
using ResidApp.Domain.Accounts;
using ResidApp.Infrastructure.Identity;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Inicio de sesión con contraseña (ADR 0007, incremento I1) y uso de las invitaciones (I2). El personal (cualquier ámbito activo que no
/// sea de familiar) necesita además un segundo factor TOTP, que solo se da de alta al usar una invitación (decisión de Orlando del
/// 2026-10-22): así, saber la contraseña de una cuenta no basta para registrar otro móvil. Los familiares entran solo con la contraseña
/// hasta que CJ decida su segundo factor (pregunta 3.2 del Portal Familiar). Cualquier fallo con la contraseña da el mismo mensaje, exista
/// o no la cuenta; los fallos de contraseña y de segundo factor cuentan para el bloqueo y quedan en dbo.eventos_acceso.
/// </summary>
[AllowAnonymous]
public sealed class AccesoController(
    UserManager<CredentialAccount> users, IProfileScopeDirectoryProvider directory, SessionSignIn signIn, PendingAccessCookie pendingCookie,
    IAccessInvitationRepository invitations, InvitationCookie invitationCookie, ISessionStore sessions,
    IAccessEventLog accessLog, TimeProvider clock, AccessFailureThrottle throttle, IOptions<DevAuthOptions> devAuth,
    IOptions<AccesoOptions> acceso, ILogger<AccesoController> logger) : Controller
{
    public const string PlatformUsesPasskeyMessage =
        "Las cuentas de Plataforma entran solo con passkey. Usa «Entrar con passkey».";
    public const string FailedMessage =
        "Usuario o contraseña incorrectos. Tras 5 intentos fallidos seguidos, la cuenta se bloquea 15 minutos.";
    public const string NeedsInvitationMessage =
        "Tu cuenta necesita preparar el código del móvil, y eso solo se hace con una invitación: pídela a la Administración de tu centro.";
    public const string InvalidInvitationMessage =
        "Esta invitación ya no vale: se ha usado, se ha anulado o ha caducado. Pide otra a la Administración de tu centro.";
    private const string AvisoKey = "AccesoAviso";
    private const string Issuer = "ResidApp";

    // Para que un usuario que no existe tarde lo mismo que uno con contraseña.
    private static readonly CredentialAccount Nobody = new() { Id = Guid.Empty, UserName = string.Empty };
    private static readonly Lazy<string> NobodyHash = new(() => new PasswordHasher<CredentialAccount>().HashPassword(Nobody, Guid.NewGuid().ToString()));

    [HttpGet]
    public IActionResult Entrar(string? returnUrl, bool sesionCaducada = false)
    {
        if (TempData[AvisoKey] is string aviso)
        {
            ModelState.AddModelError(string.Empty, aviso);
        }
        return View(EntrarModel(null, returnUrl, sesionCaducada));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AccesoOptions.RateLimitPolicy)]
    public async Task<IActionResult> Entrar(string? usuario, string? contrasena, string? returnUrl, CancellationToken ct)
    {
        if (TooManyFailures())
        {
            return TooManyFailuresResult();
        }

        var subject = usuario?.Trim();
        if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(contrasena))
        {
            ModelState.AddModelError(string.Empty, "Escribe tu usuario y tu contraseña.");
            return View(EntrarModel(subject, returnUrl, false));
        }

        var account = await users.FindByNameAsync(subject);
        if (account is null || await users.IsLockedOutAsync(account) || !await PasswordMatchesAsync(account, contrasena))
        {
            await FailAsync(account, AccessEventType.ContrasenaFallida, ct);
            ModelState.AddModelError(string.Empty, FailedMessage);
            return View(EntrarModel(subject, returnUrl, false));
        }

        // Contraseña correcta, pero sin cuenta activa o sin ningún ámbito no se podría operar: el mismo mensaje, sin contar el intento.
        var scopes = account.Active ? await directory.ListActiveAsync(account.UserName, ct) : [];
        if (scopes.Count == 0)
        {
            throttle.RecordFailure(SessionSignIn.Ip(HttpContext));
            await accessLog.RecordAsync(AccessEventType.ContrasenaFallida, AccountId.From(account.Id), null, SessionSignIn.Ip(HttpContext), Now, ct);
            logger.LogWarning("Inicio de sesión rechazado: cuenta no activa o sin ámbito activo.");
            ModelState.AddModelError(string.Empty, FailedMessage);
            return View(EntrarModel(subject, returnUrl, false));
        }

        // Plataforma con alguna passkey ya no entra con contraseña y código (ADR 0007, I4): sería rebajar lo que la passkey exige.
        if (await MustUsePasskeyAsync(account, scopes))
        {
            ModelState.AddModelError(string.Empty, PlatformUsesPasskeyMessage);
            return View(EntrarModel(subject, returnUrl, false));
        }

        if (!account.TwoFactorEnabled && scopes.All(scope => scope.Profile == SystemProfile.Familiar))
        {
            await users.ResetAccessFailedCountAsync(account);
            return await OpenSessionAsync(account, scopes[0], returnUrl, ct);
        }

        if (!account.TwoFactorEnabled)
        {
            // Personal sin segundo factor (dejó a medias la invitación): solo otra invitación se lo deja preparar.
            logger.LogWarning("Inicio de sesión del personal sin segundo factor: necesita una invitación.");
            ModelState.AddModelError(string.Empty, NeedsInvitationMessage);
            return View(EntrarModel(subject, returnUrl, false));
        }

        pendingCookie.Write(Response, account.Id, account.SecurityStamp ?? string.Empty, Url.IsLocalUrl(returnUrl) ? returnUrl : null);
        return RedirectToAction(nameof(SegundoFactor));
    }

    /// <summary>Con el token (el enlace o el QR de la hoja): si la invitación vale, pasa a una cookie y se quita de la dirección.</summary>
    [HttpGet]
    [EnableRateLimiting(AccesoOptions.RateLimitPolicy)]
    public async Task<IActionResult> Invitacion(string? id, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(id))
        {
            return await ResolveInvitationAsync(ct) is { } current
                ? View(await InvitationModelAsync(current))
                : InvalidInvitation();
        }

        if (TooManyFailures())
        {
            return TooManyFailuresResult();
        }

        var tokenHash = AccessInvitation.Hash(id);
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (await invitations.FindLiveAsync(tokenHash, Now, ct) is null)
        {
            throttle.RecordFailure(SessionSignIn.Ip(HttpContext));
            await accessLog.RecordAsync(AccessEventType.InvitacionNoValida, null, null, SessionSignIn.Ip(HttpContext), Now, ct);
            logger.LogWarning("Invitación no válida.");
            return InvalidInvitation();
        }

        invitationCookie.Write(Response, tokenHash);
        return RedirectToAction(nameof(Invitacion), new { id = (string?)null });
    }

    /// <summary>Usa la invitación: elige la contraseña, que sustituye a la anterior junto con el segundo factor, y desbloquea la cuenta. En
    /// una recuperación, además, se cierran todas sus sesiones. Después el personal activa el segundo factor y el familiar entra.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AccesoOptions.RateLimitPolicy)]
    public async Task<IActionResult> Invitacion(string? contrasena, string? confirmacion, CancellationToken ct)
    {
        if (await ResolveInvitationAsync(ct) is not { } invitation)
        {
            return InvalidInvitation();
        }

        if ((contrasena?.Length ?? 0) < AccesoOptions.LongitudMinimaContrasena || !string.Equals(contrasena, confirmacion, StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, (contrasena?.Length ?? 0) < AccesoOptions.LongitudMinimaContrasena
                ? $"La contraseña tiene que tener al menos {AccesoOptions.LongitudMinimaContrasena} caracteres."
                : "Las dos contraseñas no coinciden.");
            return View(await InvitationModelAsync(invitation));
        }

        if (!await invitations.ConsumeAsync(invitation.Id, Now, ct)
            || await users.FindByIdAsync(invitation.AccountId.Value.ToString("D")) is not { Active: true } account)
        {
            return InvalidInvitation();
        }

        if (account.PasswordHash is not null)
        {
            await users.RemovePasswordAsync(account);
        }
        await users.AddPasswordAsync(account, contrasena!);
        await users.SetTwoFactorEnabledAsync(account, false);
        await users.ResetAuthenticatorKeyAsync(account);
        await users.SetLockoutEndDateAsync(account, null);
        await users.ResetAccessFailedCountAsync(account);
        // La recuperación sustituye también las passkeys: el dispositivo perdido o robado no puede seguir entrando (ADR 0007, I4).
        foreach (var passkey in await users.GetPasskeysAsync(account))
        {
            await users.RemovePasskeyAsync(account, passkey.CredentialId);
        }
        var recovery = invitation.Kind == AccessInvitationKind.Recuperacion;
        if (recovery)
        {
            await sessions.CloseAllAsync(invitation.AccountId, SessionCloseReason.Recuperacion, Now, ct);
        }
        await accessLog.RecordAsync(recovery ? AccessEventType.RecuperacionUsada : AccessEventType.InvitacionUsada,
            invitation.AccountId, null, SessionSignIn.Ip(HttpContext), Now, ct);
        InvitationCookie.Clear(Response);

        var scopes = await directory.ListActiveAsync(account.UserName, ct);
        if (scopes.Count == 0)
        {
            TempData[AvisoKey] = "Tu contraseña está guardada, pero tu cuenta no tiene ningún perfil activo. Pregunta a la Administración de tu centro.";
            return RedirectToAction(nameof(Entrar));
        }
        if (scopes.All(scope => scope.Profile == SystemProfile.Familiar))
        {
            return await OpenSessionAsync(account, scopes[0], null, ct);
        }

        pendingCookie.Write(Response, account.Id, account.SecurityStamp ?? string.Empty, null, canEnroll: true);
        return RedirectToAction(nameof(ActivarSegundoFactor));
    }

    [HttpGet]
    public async Task<IActionResult> SegundoFactor(CancellationToken ct)
    {
        if (await ResolvePendingAsync(ct) is not { } pending)
        {
            return PendingExpired();
        }
        return pending.Account.TwoFactorEnabled ? View() : RedirectToAction(nameof(ActivarSegundoFactor));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AccesoOptions.RateLimitPolicy)]
    public async Task<IActionResult> SegundoFactor(string? codigo, CancellationToken ct)
    {
        if (TooManyFailures())
        {
            return TooManyFailuresResult();
        }

        if (await ResolvePendingAsync(ct) is not { } pending || !pending.Account.TwoFactorEnabled)
        {
            return PendingExpired();
        }

        if (!await users.VerifyTwoFactorTokenAsync(pending.Account, users.Options.Tokens.AuthenticatorTokenProvider, CleanCode(codigo)))
        {
            return await SecondFactorFailedAsync(pending.Account, View(), ct);
        }

        return await CompleteAsync(pending, ct);
    }

    [HttpGet]
    public async Task<IActionResult> CodigoRecuperacion(CancellationToken ct) =>
        await ResolvePendingAsync(ct) is { Account.TwoFactorEnabled: true } ? View() : PendingExpired();

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AccesoOptions.RateLimitPolicy)]
    public async Task<IActionResult> CodigoRecuperacion(string? codigo, CancellationToken ct)
    {
        if (TooManyFailures())
        {
            return TooManyFailuresResult();
        }

        if (await ResolvePendingAsync(ct) is not { } pending || !pending.Account.TwoFactorEnabled)
        {
            return PendingExpired();
        }

        if (string.IsNullOrWhiteSpace(codigo) || !(await users.RedeemTwoFactorRecoveryCodeAsync(pending.Account, codigo.Trim())).Succeeded)
        {
            return await SecondFactorFailedAsync(pending.Account, View(), ct);
        }

        await accessLog.RecordAsync(
            AccessEventType.CodigoRecuperacionUsado, AccountId.From(pending.Account.Id), null, SessionSignIn.Ip(HttpContext), Now, ct);
        return await CompleteAsync(pending, ct);
    }

    [HttpGet]
    public async Task<IActionResult> ActivarSegundoFactor(CancellationToken ct)
    {
        if (await ResolvePendingAsync(ct) is not { State.CanEnroll: true } pending)
        {
            return PendingExpired();
        }
        if (pending.Account.TwoFactorEnabled)
        {
            return RedirectToAction(nameof(SegundoFactor));
        }

        // La clave se crea una vez y se conserva hasta que se activa: recargar la página no invalida lo ya escaneado. Crearla cambia el
        // sello de seguridad, así que el paso pendiente se vuelve a escribir con el nuevo.
        if (pending.Account.AuthenticatorKey is null)
        {
            await users.ResetAuthenticatorKeyAsync(pending.Account);
            pendingCookie.Write(Response, pending.Account.Id, pending.Account.SecurityStamp ?? string.Empty, pending.State.ReturnUrl, canEnroll: true);
        }
        return View(EnrollModel(pending.Account));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AccesoOptions.RateLimitPolicy)]
    public async Task<IActionResult> ActivarSegundoFactor(string? codigo, CancellationToken ct)
    {
        if (TooManyFailures())
        {
            return TooManyFailuresResult();
        }

        if (await ResolvePendingAsync(ct) is not { State.CanEnroll: true } pending || pending.Account.TwoFactorEnabled
            || pending.Account.AuthenticatorKey is null)
        {
            return PendingExpired();
        }

        if (!await users.VerifyTwoFactorTokenAsync(pending.Account, users.Options.Tokens.AuthenticatorTokenProvider, CleanCode(codigo)))
        {
            return await SecondFactorFailedAsync(pending.Account, View(EnrollModel(pending.Account)), ct);
        }

        await users.SetTwoFactorEnabledAsync(pending.Account, true);
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(pending.Account, 10))!.ToList();
        await accessLog.RecordAsync(
            AccessEventType.SegundoFactorAlta, AccountId.From(pending.Account.Id), null, SessionSignIn.Ip(HttpContext), Now, ct);
        await users.ResetAccessFailedCountAsync(pending.Account);
        PendingAccessCookie.Clear(Response);
        await signIn.OpenAsync(HttpContext, pending.Scopes[0], AccessEventType.Entrada, ct);

        // Los códigos solo se enseñan en esta respuesta: sin redirección y sin que el navegador la guarde.
        Response.Headers.CacheControl = "no-store";
        return View("CodigosRecuperacion", new CodigosRecuperacionViewModel(codes, await AfterSignInUrlAsync(pending.Account, pending.Scopes[0], pending.State.ReturnUrl)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Salir(CancellationToken ct)
    {
        await signIn.CloseCurrentAsync(HttpContext, SessionCloseReason.Logout, ct);
        ActiveProfileScopeCookie.Clear(Response);
        ClinicalAccessDeclarationCookie.Clear(Response);
        PendingAccessCookie.Clear(Response);
        return RedirectToAction("Index", "Home");
    }

    /// <summary>Las cuentas las da la Administración del centro (o Plataforma, la de Administración): no hay alta por cuenta propia.</summary>
    [HttpGet]
    public IActionResult Solicitar() => View();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private bool TooManyFailures() => throttle.IsBlocked(SessionSignIn.Ip(HttpContext));

    private ObjectResult TooManyFailuresResult() => StatusCode(StatusCodes.Status429TooManyRequests,
        "Demasiados accesos fallidos desde esta red. Espera unos minutos y vuelve a intentarlo.");

    private AccesoEntrarViewModel EntrarModel(string? usuario, string? returnUrl, bool sesionCaducada) =>
        new(usuario, Url.IsLocalUrl(returnUrl) ? returnUrl : null, sesionCaducada, devAuth.Value.Habilitado, acceso.Value.PasskeysActivas);

    private async Task<bool> MustUsePasskeyAsync(CredentialAccount account, IReadOnlyList<ActiveProfileScope> scopes) =>
        acceso.Value.PasskeysActivas && scopes.Any(scope => scope.Profile == SystemProfile.Plataforma)
        && (await users.GetPasskeysAsync(account)).Count > 0;

    /// <summary>Adónde sigue quien acaba de entrar con contraseña: una cuenta de Plataforma sin ninguna passkey va a «Mi cuenta» a crear la
    /// primera, que es obligatoria (ADR 0007, I4).</summary>
    private async Task<string> AfterSignInUrlAsync(CredentialAccount account, ActiveProfileScope scope, string? returnUrl) =>
        acceso.Value.PasskeysActivas && scope.Profile == SystemProfile.Plataforma && (await users.GetPasskeysAsync(account)).Count == 0
            ? Url.Action("Index", "Cuenta")!
            : ContinueUrl(returnUrl);

    private async Task<bool> PasswordMatchesAsync(CredentialAccount account, string password)
    {
        if (account.PasswordHash is null)
        {
            users.PasswordHasher.VerifyHashedPassword(Nobody, NobodyHash.Value, password);
            return false;
        }
        return await users.CheckPasswordAsync(account, password);
    }

    /// <summary>Anota el fallo y lo cuenta para el bloqueo (si la cuenta tiene contraseña); al llegar al límite, anota el bloqueo.</summary>
    private async Task FailAsync(CredentialAccount? account, AccessEventType type, CancellationToken ct)
    {
        if (account is null)
        {
            users.PasswordHasher.VerifyHashedPassword(Nobody, NobodyHash.Value, Guid.NewGuid().ToString());
        }

        var accountId = account is null ? (AccountId?)null : AccountId.From(account.Id);
        var ip = SessionSignIn.Ip(HttpContext);
        throttle.RecordFailure(ip);
        await accessLog.RecordAsync(type, accountId, null, ip, Now, ct);
        logger.LogWarning("Inicio de sesión fallido ({Tipo}).", type.ToCode());
        if (account?.PasswordHash is null || await users.IsLockedOutAsync(account))
        {
            return;
        }

        await users.AccessFailedAsync(account);
        if (await users.IsLockedOutAsync(account))
        {
            await accessLog.RecordAsync(AccessEventType.Bloqueo, accountId, null, ip, Now, ct);
            logger.LogWarning("Cuenta bloqueada {Minutos} minutos tras {Intentos} intentos fallidos.",
                AccesoOptions.DuracionBloqueo.TotalMinutes, AccesoOptions.IntentosAntesDeBloquear);
        }
    }

    private async Task<IActionResult> SecondFactorFailedAsync(CredentialAccount account, IActionResult retry, CancellationToken ct)
    {
        await FailAsync(account, AccessEventType.SegundoFactorFallido, ct);
        if (await users.IsLockedOutAsync(account))
        {
            PendingAccessCookie.Clear(Response);
            TempData[AvisoKey] = "Has superado los intentos permitidos: la cuenta queda bloqueada 15 minutos.";
            return RedirectToAction(nameof(Entrar));
        }

        ModelState.AddModelError(string.Empty, "El código no es correcto. Comprueba que es el de ResidApp y que no ha cambiado ya.");
        return retry;
    }

    private async Task<IActionResult> CompleteAsync(Pending pending, CancellationToken ct)
    {
        await users.ResetAccessFailedCountAsync(pending.Account);
        PendingAccessCookie.Clear(Response);
        return await OpenSessionAsync(pending.Account, pending.Scopes[0], pending.State.ReturnUrl, ct);
    }

    private async Task<IActionResult> OpenSessionAsync(CredentialAccount account, ActiveProfileScope scope, string? returnUrl, CancellationToken ct)
    {
        await signIn.OpenAsync(HttpContext, scope, AccessEventType.Entrada, ct);
        return LocalRedirect(await AfterSignInUrlAsync(account, scope, returnUrl));
    }

    private string ContinueUrl(string? returnUrl) => Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action("Index", "Home")!;

    private sealed record Pending(PendingAccess State, CredentialAccount Account, IReadOnlyList<ActiveProfileScope> Scopes);

    /// <summary>El paso pendiente sigue valiendo si la cookie es válida y no ha caducado, la cuenta sigue activa, con algún ámbito activo,
    /// sin bloquear y con el mismo sello de seguridad.</summary>
    private async Task<Pending?> ResolvePendingAsync(CancellationToken ct)
    {
        if (pendingCookie.Read(Request) is not { } state
            || await users.FindByIdAsync(state.AccountId.ToString("D")) is not { Active: true } account
            || !string.Equals(account.SecurityStamp ?? string.Empty, state.SecurityStamp, StringComparison.Ordinal)
            || await users.IsLockedOutAsync(account))
        {
            return null;
        }

        var scopes = await directory.ListActiveAsync(account.UserName, ct);
        return scopes.Count == 0 ? null : new Pending(state, account, scopes);
    }

    private RedirectToActionResult PendingExpired()
    {
        PendingAccessCookie.Clear(Response);
        TempData[AvisoKey] = "Ha pasado demasiado tiempo o algo ha cambiado en la cuenta. Vuelve a escribir tu usuario y tu contraseña.";
        return RedirectToAction(nameof(Entrar));
    }

    /// <summary>La invitación de la cookie, si sigue viva en la base.</summary>
    private async Task<LiveAccessInvitation?> ResolveInvitationAsync(CancellationToken ct) =>
        invitationCookie.Read(Request) is { } tokenHash ? await invitations.FindLiveAsync(tokenHash, Now, ct) : null;

    private async Task<UsarInvitacionViewModel> InvitationModelAsync(LiveAccessInvitation invitation)
    {
        var account = await users.FindByIdAsync(invitation.AccountId.Value.ToString("D"));
        return new UsarInvitacionViewModel(account?.UserName ?? string.Empty, invitation.Kind == AccessInvitationKind.Recuperacion);
    }

    private ViewResult InvalidInvitation()
    {
        InvitationCookie.Clear(Response);
        Response.StatusCode = StatusCodes.Status410Gone;
        return View("InvitacionNoValida");
    }

    private static string CleanCode(string? code) => (code ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);

    private ActivarSegundoFactorViewModel EnrollModel(CredentialAccount account)
    {
        var key = account.AuthenticatorKey!;
        var grouped = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            grouped.Append(key.AsSpan(i, Math.Min(4, key.Length - i))).Append(' ');
        }
        var uri = $"otpauth://totp/{Uri.EscapeDataString($"{Issuer}:{account.UserName}")}?secret={key}&issuer={Issuer}&digits=6";
        return new ActivarSegundoFactorViewModel(account.UserName, grouped.ToString().TrimEnd().ToLowerInvariant(), uri);
    }
}
