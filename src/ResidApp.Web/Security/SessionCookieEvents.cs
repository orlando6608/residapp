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

    /// <summary>
    /// Al guardar un formulario con la sesión ya terminada, volver después del inicio de sesión a esa acción, que solo admite POST, daría
    /// un 405. Se vuelve a la pantalla desde la que se envió (el Referer, si es de esta misma aplicación) o al Inicio, y el inicio de
    /// sesión avisa de que lo enviado no se ha guardado.
    /// </summary>
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        var request = context.Request;
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            var referer = request.GetTypedHeaders().Referer;
            var returnUrl = referer is { IsAbsoluteUri: true }
                && string.Equals(referer.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase)
                ? referer.PathAndQuery
                : null;
            context.RedirectUri = request.PathBase + context.Options.LoginPath + "?sesionCaducada=true"
                + (returnUrl is null ? string.Empty : "&" + context.Options.ReturnUrlParameter + "=" + Uri.EscapeDataString(returnUrl));
        }
        return base.RedirectToLogin(context);
    }
}
