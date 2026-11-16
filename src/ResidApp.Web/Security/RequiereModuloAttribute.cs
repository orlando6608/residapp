using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Modules;

namespace ResidApp.Web.Security;

/// <summary>
/// Marca una acción como perteneciente a un módulo opcional del catálogo (ADR 0015, punto 8). Si el centro del ámbito activo (verificado contra la cuenta de la sesión, no el de la
/// cookie) no tiene el módulo, la acción responde 404, igual que el dictado sin clave. Con <see cref="Lectura"/> la acción también se atiende con el módulo en solo lectura (consultas
/// y copias); sin ella, solo con el módulo activo (crear y editar), y en solo lectura responde 409 con un mensaje. Este filtro solo oculta y orienta: la comprobación que cuenta va en el caso de uso, con
/// <see cref="CenterModuleAccess.RequireWritableAsync"/>. <c>ModulosGuardiaTests</c> falla si un módulo del catálogo no tiene ninguna acción marcada.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class RequiereModuloAttribute(string moduloCodigo) : Attribute, IAsyncActionFilter
{
    public string ModuloCodigo { get; } = moduloCodigo;

    /// <summary>La acción solo lee: vale también con el módulo en solo lectura.</summary>
    public bool Lectura { get; init; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var scope = ActiveProfileScopeCookie.Read(context.HttpContext.Request);
        var access = context.HttpContext.RequestServices.GetRequiredService<CenterModuleAccess>();
        var state = scope is null
            ? ModuleState.NotAvailable
            : await access.StateForScopeAsync(scope.ProfileScopeId, ModuloCodigo, context.HttpContext.RequestAborted);
        if (state == ModuleState.Active || (Lectura && state == ModuleState.ReadOnly))
        {
            await next();
            return;
        }

        // En solo lectura, crear o editar por una URL directa se rechaza con un mensaje claro (no con un 404 que lo esconda).
        context.Result = state == ModuleState.ReadOnly
            ? new ObjectResult(new { mensaje = "Este módulo está en solo lectura: puedes consultar lo registrado, pero no crear ni cambiar nada." })
            { StatusCode = StatusCodes.Status409Conflict }
            : new NotFoundResult();
    }
}
