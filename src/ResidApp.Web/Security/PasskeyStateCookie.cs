using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace ResidApp.Web.Security;

/// <summary>Para qué se pidieron las opciones: entrar con una passkey o registrar una nueva. Una cookie de un tipo no vale para el otro.</summary>
public enum PasskeyCeremony
{
    Entrada,
    Alta,
}

/// <summary>El estado que el servidor recuerda entre las dos peticiones de una passkey (el reto y, en el alta, la cuenta). SessionId
/// solo va en el alta: liga el registro a la sesión que lo pidió, que es de una sola cuenta.</summary>
public sealed record PasskeyState(PasskeyCeremony Ceremony, string State, Guid? SessionId, DateTimeOffset ExpiresAt);

/// <summary>
/// El estado de una ceremonia de passkey (ADR 0007, incremento I4), en una cookie cifrada por Data Protection, de 5 minutos y de un solo
/// uso: <see cref="Take"/> la borra siempre. Es lo que exige la documentación de Identity cuando no se usa SignInManager: el cliente no puede
/// leerla ni cambiarla, y un estado alterado registraría la passkey en la cuenta que nombre.
/// </summary>
public sealed class PasskeyStateCookie(IDataProtectionProvider dataProtection, TimeProvider clock)
{
    public const string CookieName = "residapp_passkey";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly IDataProtector protector = dataProtection.CreateProtector("ResidApp.Web.Security.PasskeyState");

    public void Write(HttpResponse response, PasskeyCeremony ceremony, string state, Guid? sessionId = null) =>
        response.Cookies.Append(CookieName,
            protector.Protect(JsonSerializer.Serialize(new PasskeyState(ceremony, state, sessionId, clock.GetUtcNow() + Lifetime))),
            new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", IsEssential = true, MaxAge = Lifetime });

    /// <summary>El estado de esa ceremonia, o null si no hay, está manipulado, ha caducado o es de la otra ceremonia. La cookie se borra
    /// en cualquier caso: cada estado vale una vez.</summary>
    public PasskeyState? Take(HttpRequest request, HttpResponse response, PasskeyCeremony ceremony)
    {
        var found = request.Cookies.TryGetValue(CookieName, out var value) && !string.IsNullOrEmpty(value);
        if (found)
        {
            response.Cookies.Delete(CookieName, new CookieOptions { Path = "/", SameSite = SameSiteMode.Strict, HttpOnly = true });
        }
        if (!found)
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<PasskeyState>(protector.Unprotect(value!));
            return state is not null && state.Ceremony == ceremony && state.ExpiresAt > clock.GetUtcNow() ? state : null;
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or JsonException)
        {
            return null;
        }
    }
}
