using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ResidApp.Application.Ports;
using ResidApp.Web.Security;

namespace ResidApp.FunctionalTests;

/// <summary>
/// Entrada que solo existe en el servidor de las pruebas funcionales (la añade <see cref="ResidentsFlowTests.WebAppFactory"/>; la aplicación
/// no la tiene): abre la sesión de una cuenta sembrada sin contraseña ni código, con el mismo <see cref="SessionSignIn"/> que el inicio de
/// sesión real, para que cada prueba no tenga que darse de alta. El inicio de sesión de verdad lo prueban AccesoTests y sus parciales.
/// Con una sesión abierta, la cierra como al entrar con otra cuenta.
/// </summary>
internal sealed class EntradaDePruebas : IStartupFilter
{
    public const string Ruta = "/pruebas/entrar";

    // Sin app.Map: movería la ruta base y la cookie de la sesión se quedaría con path=/pruebas/entrar.
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((http, nextMiddleware) =>
            HttpMethods.IsPost(http.Request.Method) && http.Request.Path == Ruta ? EntrarAsync(http) : nextMiddleware(http));
        next(app);
    };

    private static async Task EntrarAsync(HttpContext http)
    {
        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        var scopes = await http.RequestServices.GetRequiredService<IProfileScopeDirectoryProvider>()
            .ListActiveAsync(form["sujeto"].ToString().Trim(), http.RequestAborted);
        if (scopes.Count == 0)
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Este desvío va antes de la autenticación de la aplicación: la sesión actual se lee aquí para poder cerrarla.
        if ((await http.AuthenticateAsync()).Principal is { } current)
        {
            http.User = current;
        }

        await http.RequestServices.GetRequiredService<SessionSignIn>().OpenAsync(http, scopes[0], AccessEventType.Entrada, http.RequestAborted);
        http.Response.Redirect("/");
    }
}
