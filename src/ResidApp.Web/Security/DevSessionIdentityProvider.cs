using ResidApp.Application.Ports;

namespace ResidApp.Web.Security;

/// <summary>
/// Implementación de desarrollo de ISessionIdentityProvider: NO es autenticación real. Lee el sujeto
/// externo verificado desde una cookie propia que rellena DevAuthController, sin contraseña ni proveedor
/// de identidad. El proveedor productivo (Auth0, WorkOS u otro) sigue siendo una decisión abierta — ver
/// README.md "Decisiones pendientes" — y sustituirá esta clase entera, no la extenderá.
/// </summary>
public sealed class DevSessionIdentityProvider(IHttpContextAccessor httpContextAccessor) : ISessionIdentityProvider
{
    public const string CookieName = "residapp_dev_subject";

    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default)
    {
        var externalSubject = httpContextAccessor.HttpContext?.Request.Cookies[CookieName];
        VerifiedIdentity? identity = string.IsNullOrWhiteSpace(externalSubject)
            ? null
            : new VerifiedIdentity(externalSubject);
        return Task.FromResult(identity);
    }
}
