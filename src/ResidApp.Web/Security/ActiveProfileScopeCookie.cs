using System.Text.Json;
using ResidApp.Shared;

namespace ResidApp.Web.Security;

/// <summary>Ámbito activo (centro + perfil) tal como se recuerda en la cookie, ya decodificado.</summary>
public sealed record ActiveProfileScopeCookieValue(Guid ProfileScopeId, Guid CenterId, string CenterName, SystemProfile Profile);

/// <summary>
/// Cookie propia que recuerda el ámbito activo elegido en ProfileScope/Select, para precargarlo en
/// pantallas como Residents/Create sin volver a consultar SQL Server en cada request. Es puramente
/// cosmético/UX: la autorización real siempre revalida el par (ProfileScopeId, CenterId) contra SQL Server
/// vía IAuthorizationEvidenceProvider, así que un valor manipulado aquí nunca concede acceso por sí solo.
/// </summary>
public static class ActiveProfileScopeCookie
{
    public const string CookieName = "residapp_active_scope";

    public static void Write(HttpResponse response, Guid profileScopeId, Guid centerId, string centerName, SystemProfile profile)
    {
        var payload = new Payload(profileScopeId, centerId, centerName, profile.ToCode());
        var value = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload));
        response.Cookies.Append(CookieName, value, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
        });
    }

    public static ActiveProfileScopeCookieValue? Read(HttpRequest request)
    {
        var raw = request.Cookies[CookieName];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(Convert.FromBase64String(raw));
            if (payload is null || !EnumCode.TryParseCode<SystemProfile>(payload.ProfileCode, out var profile))
            {
                return null;
            }
            return new ActiveProfileScopeCookieValue(payload.ProfileScopeId, payload.CenterId, payload.CenterName, profile);
        }
        catch (Exception error) when (error is FormatException or JsonException)
        {
            return null;
        }
    }

    public static void Clear(HttpResponse response) => response.Cookies.Delete(CookieName);

    private sealed record Payload(Guid ProfileScopeId, Guid CenterId, string CenterName, string ProfileCode);
}
