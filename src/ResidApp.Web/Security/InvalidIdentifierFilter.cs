using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ResidApp.Shared;

namespace ResidApp.Web.Security;

/// <summary>
/// Una dirección o un formulario sin identificador (Guid.Empty) llega a la acción como una petición mal formada, no como un fallo: se devuelve a
/// la pantalla de inicio, igual que cuando se abre a mano algo que no es del ámbito. Antes salía un 500. Se deja constancia en el registro.
/// </summary>
public sealed class InvalidIdentifierFilter(ILogger<InvalidIdentifierFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not InvalidIdentifierException error)
        {
            return;
        }

        logger.LogWarning("Petición con identificador vacío en {Path} ({Parameter}).", context.HttpContext.Request.Path, error.ParamName);
        context.Result = new RedirectToActionResult("Index", "Home", null);
        context.ExceptionHandled = true;
    }
}
