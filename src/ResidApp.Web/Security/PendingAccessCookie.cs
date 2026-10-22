using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace ResidApp.Web.Security;

/// <summary>Quién ha superado la contraseña y le falta el segundo factor. Lleva el sello de seguridad de ese momento: si cambia (otra
/// contraseña, otro segundo factor), el paso pendiente deja de valer. CanEnroll: viene de usar una invitación, la única vía para dar de
/// alta el segundo factor (ADR 0007, I2).</summary>
public sealed record PendingAccess(Guid AccountId, string SecurityStamp, string? ReturnUrl, DateTimeOffset ExpiresAt, bool CanEnroll = false);

/// <summary>
/// El paso entre la contraseña y el segundo factor (ADR 0007, incremento I1): una cookie cifrada por Data Protection, de 10 minutos y
/// solo para /Acceso. No es una sesión: no abre ninguna pantalla, solo deja seguir con el segundo factor. La caducidad va dentro y se
/// comprueba con <see cref="TimeProvider"/>, como la de la sesión.
/// </summary>
public sealed class PendingAccessCookie(IDataProtectionProvider dataProtection, TimeProvider clock)
{
    public const string CookieName = "residapp_acceso_pendiente";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly IDataProtector protector = dataProtection.CreateProtector("ResidApp.Web.Security.PendingAccess");

    public void Write(HttpResponse response, Guid accountId, string securityStamp, string? returnUrl, bool canEnroll = false) =>
        response.Cookies.Append(CookieName,
            protector.Protect(JsonSerializer.Serialize(
                new PendingAccess(accountId, securityStamp, returnUrl, clock.GetUtcNow() + Lifetime, canEnroll))),
            new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/Acceso", IsEssential = true, MaxAge = Lifetime });

    /// <summary>El paso pendiente, o null si no hay, está manipulado o ha caducado.</summary>
    public PendingAccess? Read(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            var pending = JsonSerializer.Deserialize<PendingAccess>(protector.Unprotect(value));
            return pending is not null && pending.ExpiresAt > clock.GetUtcNow() ? pending : null;
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or JsonException)
        {
            return null;
        }
    }

    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(CookieName, new CookieOptions { Path = "/Acceso", SameSite = SameSiteMode.Strict, HttpOnly = true });
}
