using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using ResidApp.Shared;

namespace ResidApp.Web.Security;

/// <summary>
/// Una dirección o un formulario sin identificador (Guid.Empty) llega a la acción como una petición mal formada, no como un fallo: se devuelve a
/// la pantalla de inicio, igual que cuando se abre a mano algo que no es del ámbito. Antes salía un 500. Se deja constancia en el registro, sin la ruta (ver abajo).
/// </summary>
public sealed class InvalidIdentifierFilter(ILogger<InvalidIdentifierFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not InvalidIdentifierException error)
        {
            return;
        }

        // Hallazgo B10: no se escribe la ruta que manda el cliente (puede traer saltos de línea, otros identificadores o un token de invitación):
        // solo el controlador y la acción, que salen del código, y el nombre del parámetro.
        var action = context.ActionDescriptor is ControllerActionDescriptor descriptor ? descriptor.ControllerName + "." + descriptor.ActionName : "desconocida";
        logger.LogWarning("Petición con identificador vacío en {Accion} ({Parameter}).", action, error.ParamName);
        context.Result = new RedirectToActionResult("Index", "Home", null);
        context.ExceptionHandled = true;
    }
}
