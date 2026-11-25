using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ResidApp.Domain.Platform;

namespace ResidApp.Web.Security;

/// <summary>
/// La pantalla solo existe en un Centro de Día (ADR 0019): una Residencia no anota la asistencia diaria. Si el centro del ámbito activo no es un Centro de Día, no se hace nada y se vuelve al inicio con un
/// aviso, aunque la dirección se escriba a mano. El tipo lo fija el middleware desde la base de datos (<see cref="CurrentCenterService"/>), no la cookie; el almacén lo vuelve a comprobar en cada operación.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SoloCentroDeDiaAttribute : ActionFilterAttribute
{
    public const string Aviso = "La asistencia diaria solo existe en un Centro de Día.";

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (CurrentCenterService.IsDayCenter)
        {
            return;
        }

        if (context.Controller is Controller controller)
        {
            controller.TempData["Error"] = Aviso;
        }

        context.Result = new RedirectToActionResult("Index", "Home", null);
    }
}
