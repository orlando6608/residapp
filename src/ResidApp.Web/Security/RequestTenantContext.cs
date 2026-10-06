using ResidApp.Application.Ports;

namespace ResidApp.Web.Security;

/// <summary>Sujeto verificado de la sesión y ámbito activo de la cookie para la seguridad por filas de SQL. La cookie no es de fiar
/// (ver <see cref="ActiveProfileScopeCookie"/>); por eso solo se toma su id de ámbito, y SQL lo valida contra la cuenta verificada.</summary>
public sealed class RequestTenantContext(IHttpContextAccessor httpContextAccessor, ISessionIdentityProvider session) : ITenantContext
{
    public async Task<TenantScope?> GetAsync(CancellationToken ct = default)
    {
        var request = httpContextAccessor.HttpContext?.Request;
        var activeScope = request is null ? null : ActiveProfileScopeCookie.Read(request);
        if (activeScope is null)
        {
            return null;
        }

        var identity = await session.GetVerifiedIdentityAsync(ct);
        return identity is null || string.IsNullOrWhiteSpace(identity.ExternalSubject)
            ? null
            : new TenantScope(identity.ExternalSubject, activeScope.ProfileScopeId);
    }
}
