using System.Security.Claims;

namespace ResidApp.Web.Security;

/// <summary>
/// La sesión (ADR 0007, incremento I0): una cookie de autenticación de ASP.NET Core, cifrada y firmada por Data Protection, que lleva
/// el sujeto de la cuenta, el id de su fila en dbo.sesiones y el nombre visible. Nada más: ni perfil, ni centro, ni permisos, que los
/// resuelve SQL en cada petición. <see cref="SessionCookieEvents"/> la valida contra dbo.sesiones en cada petición.
/// </summary>
public static class SessionAuthentication
{
    public const string CookieName = "residapp_sesion";
    public const string SubjectClaim = "sub";
    public const string SessionIdClaim = "sid";
    public const string NameClaim = "name";

    public static ClaimsPrincipal Principal(string externalSubject, Guid sessionId, string? displayName) =>
        new(new ClaimsIdentity(
            [
                new Claim(SubjectClaim, externalSubject),
                new Claim(SessionIdClaim, sessionId.ToString("D")),
                new Claim(NameClaim, string.IsNullOrWhiteSpace(displayName) ? externalSubject : displayName),
            ],
            authenticationType: "Cookies", nameType: NameClaim, roleType: null));

    public static string? Subject(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true ? user.FindFirst(SubjectClaim)?.Value : null;

    public static Guid? SessionId(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true && Guid.TryParse(user.FindFirst(SessionIdClaim)?.Value, out var id) ? id : null;
}
