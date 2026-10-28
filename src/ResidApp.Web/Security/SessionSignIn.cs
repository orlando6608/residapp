using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using ResidApp.Application.Ports;
using ResidApp.Application.Sessions;
using ResidApp.Application.UseCases;

namespace ResidApp.Web.Security;

/// <summary>
/// Abre y cierra la sesión (ADR 0007): una fila en dbo.sesiones y la cookie cifrada que la apunta. Lo usan el inicio de sesión con
/// contraseña (AccesoController) y el de passkey (PasskeyController), que solo se diferencian en cómo comprueban quién es la persona.
/// </summary>
public sealed class SessionSignIn(
    ClinicalAccessDeclarations declarations, ISessionStore sessions, SessionPolicy policy, TimeProvider clock, IAccessEventLog accessLog)
{
    /// <summary>Cierra la sesión anterior, si la había, abre una nueva para la cuenta del ámbito y anota la entrada.</summary>
    public async Task OpenAsync(HttpContext http, ActiveProfileScope scope, AccessEventType entry, CancellationToken ct)
    {
        await CloseCurrentAsync(http, SessionCloseReason.CambioIdentidad, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var sessionId = Guid.NewGuid();
        await sessions.OpenAsync(sessionId, scope.AccountId, now, now + policy.MaxDuration, ct);
        // El sujeto de la cookie es el de la cuenta tal como está guardado, no como se escribió.
        var stored = await sessions.FindAsync(sessionId, ct);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            SessionAuthentication.Principal(stored!.ExternalSubject, sessionId, scope.AccountDisplayName));
        ActiveProfileScopeCookie.Clear(http.Response);
        ClinicalAccessDeclarationCookie.Clear(http.Response);
        TransferHistoryCookie.Clear(http.Response);
        await accessLog.RecordAsync(entry, scope.AccountId, sessionId, Ip(http), now, ct);
    }

    public async Task CloseCurrentAsync(HttpContext http, SessionCloseReason reason, CancellationToken ct)
    {
        if (SessionAuthentication.SessionId(http.User) is not { } sessionId)
        {
            return;
        }

        // Cerrar sesión termina las declaraciones de acceso clínico (CJ, 2026-10-06), mientras la identidad y el ámbito aún están.
        await declarations.EndAllAsync(ct);
        await sessions.CloseAsync(sessionId, reason, clock.GetUtcNow().UtcDateTime, ct);
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    /// <summary>La dirección del cliente, ya corregida por las cabeceras del balanceador (UseForwardedHeaders).</summary>
    public static string? Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();
}
