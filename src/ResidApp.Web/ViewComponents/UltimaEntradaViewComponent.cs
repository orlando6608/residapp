using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Ports;
using ResidApp.Web.Security;

namespace ResidApp.Web.ViewComponents;

/// <summary>Cuando es null no consta ninguna otra entrada.</summary>
public sealed record UltimaEntradaViewModel(DateTimeOffset? Otra);

/// <summary>
/// La fecha de la última entrada en la cuenta, sin contar esta (ADR 0012, pieza 3): con ella la familia puede ver si alguien más ha entrado. Solo la fecha y la hora, sin dirección
/// IP ni aparato, y solo cuando se entra con código. Si no se puede leer, no se enseña y la pantalla sigue.
/// </summary>
public sealed class UltimaEntradaViewComponent(
    IAccessEventLog accessLog, FamilyAccessPolicy familyAccess, ILogger<UltimaEntradaViewComponent> logger) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!familyAccess.EmailCode || SessionAuthentication.SessionId(UserClaimsPrincipal) is not { } sessionId)
        {
            return Content(string.Empty);
        }

        try
        {
            var at = await accessLog.FindOtherEntryAsync(sessionId, HttpContext.RequestAborted);
            return View(new UltimaEntradaViewModel(at is { } value ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)) : null));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("No se pudo leer la última entrada ({Tipo}).", ex.GetType().Name);
            return Content(string.Empty);
        }
    }
}
