using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using ResidApp.Application.Ports;
using ResidApp.Application.Sessions;

namespace ResidApp.Web.Security;

/// <summary>
/// Valida la sesión contra dbo.sesiones en cada petición: si está cerrada, caducada, inactiva demasiado tiempo o su cuenta ya no está
/// activa, se rechaza y se borra la cookie. Así un cierre de sesión o una suspensión se notan en la petición siguiente aunque alguien
/// reutilice la cookie. La renovación deslizante de la cookie nunca amplía la caducidad absoluta, que está en SQL.
/// </summary>
public sealed class SessionCookieEvents(ISessionStore sessions, SessionPolicy policy, TimeProvider clock) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var sessionId = SessionAuthentication.SessionId(context.Principal);
        var ct = context.HttpContext.RequestAborted;
        var session = sessionId is { } id ? await sessions.FindAsync(id, ct) : null;
        var now = clock.GetUtcNow().UtcDateTime;
        var check = policy.Check(session, SessionAuthentication.Subject(context.Principal), now);

        if (check == SessionCheck.Valid)
        {
            if (policy.NeedsTouch(session!, now))
            {
                await sessions.TouchAsync(session!.Id, now, ct);
            }
            return;
        }

        if (check is SessionCheck.Idle or SessionCheck.Expired)
        {
            await sessions.CloseAsync(session!.Id, check == SessionCheck.Idle ? SessionCloseReason.Inactividad : SessionCloseReason.Caducidad, now, ct);
        }
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
