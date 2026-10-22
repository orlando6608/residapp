using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace ResidApp.Web.Security;

/// <summary>
/// La invitación que se está usando (ADR 0007, incremento I2): al abrir el enlace, su resumen pasa a esta cookie cifrada y la dirección
/// pierde el token, para que no quede en el historial ni viaje en un Referer. 30 minutos y solo para /Acceso, como
/// <see cref="PendingAccessCookie"/>. No basta por sí sola: cada paso vuelve a comprobar en la base que la invitación sigue viva.
/// </summary>
public sealed class InvitationCookie(IDataProtectionProvider dataProtection, TimeProvider clock)
{
    public const string CookieName = "residapp_invitacion";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly IDataProtector protector = dataProtection.CreateProtector("ResidApp.Web.Security.Invitation");

    public void Write(HttpResponse response, string tokenHash) =>
        response.Cookies.Append(CookieName, protector.Protect(JsonSerializer.Serialize(new Payload(tokenHash, clock.GetUtcNow() + Lifetime))),
            new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/Acceso", IsEssential = true, MaxAge = Lifetime });

    /// <summary>El resumen del token, o null si no hay cookie, está manipulada o ha caducado.</summary>
    public string? Read(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(protector.Unprotect(value));
            return payload is not null && payload.ExpiresAt > clock.GetUtcNow() ? payload.TokenHash : null;
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or JsonException)
        {
            return null;
        }
    }

    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(CookieName, new CookieOptions { Path = "/Acceso", SameSite = SameSiteMode.Strict, HttpOnly = true });

    private sealed record Payload(string TokenHash, DateTimeOffset ExpiresAt);
}
