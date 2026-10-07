using System.Security.Cryptography;
using System.Text;

namespace ResidApp.Web.Security;

/// <summary>
/// La clave de los documentos de CJ (/pendientes-cj). Es un freno para la fase de desarrollo, no autenticación real: la clave está en
/// la configuración (PendientesCj:Clave) y el repositorio es público. Sin clave configurada nadie entra.
/// La cookie vale SHA-256("residapp-cj|" + clave): se recalcula en cada petición, así sobrevive a reinicios y a varias instancias y
/// una cookie inventada no vale. La clave se lee en cada petición (no al arrancar) para poder sobrescribirla en las pruebas.
/// </summary>
public sealed class PendientesCjAccess(IConfiguration configuration)
{
    public const string CookieName = "residapp_cj_acceso";
    public const string Prefix = "/pendientes-cj";
    public const string Home = "/pendientes-cj/index.html";

    private string? Key => configuration["PendientesCj:Clave"];

    /// <summary>¿Es esta la clave? Comparación en tiempo constante sobre los hashes; vacía o sin clave configurada, nunca.</summary>
    public bool KeyMatches(string? attempt)
    {
        var key = Key;
        return !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(attempt)
            && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(attempt)), SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    public bool HasAccess(HttpRequest request)
    {
        var key = Key;
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(request.Cookies[CookieName]))
        {
            return false;
        }
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(request.Cookies[CookieName]!), Token(key));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public void Grant(HttpContext context)
    {
        var key = Key;
        if (string.IsNullOrEmpty(key))
        {
            return;
        }
        context.Response.Cookies.Append(CookieName, Convert.ToHexStringLower(Token(key)), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            IsEssential = true,
        });
    }

    public void Revoke(HttpResponse response) => response.Cookies.Delete(CookieName);

    private static byte[] Token(string key) => SHA256.HashData(Encoding.UTF8.GetBytes("residapp-cj|" + key));
}
