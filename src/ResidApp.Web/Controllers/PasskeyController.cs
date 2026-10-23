using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
/// Entrar con una passkey (ADR 0007, incremento I4). Es entrada completa, sin contraseña ni código del móvil: la passkey exige la
/// verificación de la persona en su dispositivo (huella, cara o PIN) y solo responde en el dominio configurado. El estado entre las dos
/// peticiones va en <see cref="PasskeyStateCookie"/>. Todo fallo da el mismo mensaje y cuenta para el límite por IP; una cuenta bloqueada,
/// suspendida o sin ámbito activo no entra, igual que con la contraseña. Registrar y quitar passkeys está en «Mi cuenta».
/// </summary>
[AllowAnonymous]
public sealed class PasskeyController(
    UserManager<CredentialAccount> users, IPasskeyHandler<CredentialAccount> handler, PasskeyStateCookie stateCookie,
    IProfileScopeDirectoryProvider directory, SessionSignIn signIn, IAccessEventLog accessLog, AccessFailureThrottle throttle,
    TimeProvider clock, IOptions<AccesoOptions> acceso, ILogger<PasskeyController> logger) : Controller
{
    public const string FailedMessage = "No se ha podido entrar con esa passkey. Prueba de nuevo o entra con tu usuario y contraseña.";

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AccesoOptions.RateLimitPolicy)]
    public async Task<IActionResult> OpcionesEntrada()
    {
        if (!acceso.Value.PasskeysActivas)
        {
            return NotFound();
        }
        if (throttle.IsBlocked(SessionSignIn.Ip(HttpContext)))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }

        // Sin cuenta: la passkey es «descubrible» y la propia credencial dice de quién es.
        var options = await handler.MakeRequestOptionsAsync(null, HttpContext);
        stateCookie.Write(Response, PasskeyCeremony.Entrada, options.AssertionState ?? throw new InvalidOperationException("Sin estado de la passkey."));
        return Content(options.RequestOptionsJson, "application/json");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AccesoOptions.RateLimitPolicy)]
    public async Task<IActionResult> Entrar([FromBody] PasskeyEntrarRequest request, CancellationToken ct)
    {
        if (!acceso.Value.PasskeysActivas)
        {
            return NotFound();
        }
        if (throttle.IsBlocked(SessionSignIn.Ip(HttpContext)))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var state = stateCookie.Take(Request, Response, PasskeyCeremony.Entrada);
        if (state is null || string.IsNullOrEmpty(request.CredentialJson))
        {
            return await FailAsync(null, ct);
        }

        var result = await handler.PerformAssertionAsync(new()
        {
            HttpContext = HttpContext, CredentialJson = request.CredentialJson, AssertionState = state.State,
        });
        if (!result.Succeeded)
        {
            return await FailAsync(null, ct);
        }

        var account = result.User;
        var scopes = account.Active && !await users.IsLockedOutAsync(account) ? await directory.ListActiveAsync(account.UserName, ct) : [];
        if (scopes.Count == 0)
        {
            return await FailAsync(account, ct);
        }

        // El contador de firmas y las copias de la passkey cambian con cada uso.
        await users.AddOrUpdatePasskeyAsync(account, result.Passkey);
        await signIn.OpenAsync(HttpContext, scopes[0], AccessEventType.EntradaPasskey, ct);
        return Json(new { redirect = Url.IsLocalUrl(request.ReturnUrl) ? request.ReturnUrl : Url.Action("Index", "Home") });
    }

    private async Task<IActionResult> FailAsync(CredentialAccount? account, CancellationToken ct)
    {
        var ip = SessionSignIn.Ip(HttpContext);
        throttle.RecordFailure(ip);
        await accessLog.RecordAsync(AccessEventType.PasskeyFallida, account is null ? null : AccountId.From(account.Id), null, ip,
            clock.GetUtcNow().UtcDateTime, ct);
        logger.LogWarning("Inicio de sesión fallido ({Tipo}).", AccessEventType.PasskeyFallida.ToCode());
        return Unauthorized(new { error = FailedMessage });
    }
}

/// <summary>La credencial que devuelve el navegador (JSON de WebAuthn) y adónde seguir.</summary>
public sealed record PasskeyEntrarRequest(string? CredentialJson, string? ReturnUrl);

/// <summary>La credencial nueva (JSON de WebAuthn) y el nombre con que la persona quiere reconocerla.</summary>
public sealed record PasskeyAltaRequest(string? CredentialJson, string? Nombre);
