namespace ResidApp.Web.Security;

/// <summary>
/// Recuerda el id de la petición del historial tras un traslado (CJ, tercera tanda 5.2 C; script 0077) mientras vale. Solo guarda el id: el motivo, la cuenta, el
/// ámbito, el residente y la caducidad están en SQL, que revalida la petición en cada lectura, así que una cookie manipulada o ajena nunca concede nada. Cookie
/// de sesión, HttpOnly; se borra donde se borra la declaración de acceso clínico de Dirección (cambio de ámbito, entrada y salida de sesión).
/// </summary>
public static class TransferHistoryCookie
{
    public const string CookieName = "residapp_transfer_history";

    public static void Write(HttpResponse response, Guid declarationId) =>
        response.Cookies.Append(CookieName, declarationId.ToString("D"), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
        });

    public static Guid? Read(HttpRequest request) =>
        Guid.TryParse(request.Cookies[CookieName], out var id) ? id : null;

    public static void Clear(HttpResponse response) => response.Cookies.Delete(CookieName);
}
