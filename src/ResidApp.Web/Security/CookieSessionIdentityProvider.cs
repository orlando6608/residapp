using ResidApp.Application.Ports;

namespace ResidApp.Web.Security;

/// <summary>
/// Sujeto verificado de la sesión: el de la cookie de autenticación, que <see cref="SessionCookieEvents"/> ya ha validado contra
/// dbo.sesiones en esta petición. Sustituye a la antigua cookie de desarrollo en claro (hallazgo C1). Cuando llegue el proveedor de
/// identidad (ADR 0007, ASP.NET Core Identity), cambiará cómo se abre la sesión, no esta clase.
/// </summary>
public sealed class CookieSessionIdentityProvider(IHttpContextAccessor httpContextAccessor) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default)
    {
        var subject = SessionAuthentication.Subject(httpContextAccessor.HttpContext?.User);
        return Task.FromResult(string.IsNullOrWhiteSpace(subject) ? null : new VerifiedIdentity(subject));
    }
}
