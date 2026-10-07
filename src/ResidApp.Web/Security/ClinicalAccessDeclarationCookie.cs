namespace ResidApp.Web.Security;

/// <summary>
/// Recuerda el id de la declaración de acceso clínico de Dirección (CJ, 2026-10-06) para no pedir otra vez finalidad y
/// justificación durante la hora que vale. Solo guarda el id: la finalidad, la justificación, el residente y la caducidad
/// están en SQL, que revalida la declaración (cuenta, ámbito, residente, hora y que no haya terminado) en cada lectura, así que
/// una cookie manipulada o ajena nunca concede nada. Cookie de sesión, HttpOnly; se borra al cambiar de ámbito y al salir.
/// </summary>
public static class ClinicalAccessDeclarationCookie
{
    public const string CookieName = "residapp_clinical_access";

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
