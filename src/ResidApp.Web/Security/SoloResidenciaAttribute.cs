using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ResidApp.Domain.Platform;

namespace ResidApp.Web.Security;

/// <summary>
/// La pantalla o la acción solo existe en una Residencia (ADR 0017, fase 2): un Centro de Día no tiene habitaciones ni camas. Si el centro del ámbito activo es un Centro de Día, no se hace nada y se
/// vuelve a la pantalla indicada con un aviso, aunque la dirección se escriba a mano. El tipo lo fija el middleware desde la base de datos (<see cref="CurrentCenterService"/>), no la cookie.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SoloResidenciaAttribute(string accionDeVuelta) : ActionFilterAttribute
{
    public const string Aviso = "Un Centro de Día no tiene habitaciones ni plazas.";

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!CurrentCenterService.IsDayCenter)
        {
            return;
        }

        if (context.Controller is Controller controller)
        {
            controller.TempData["Error"] = Aviso;
        }

        context.Result = new RedirectToActionResult(accionDeVuelta, null, null);
    }
}
