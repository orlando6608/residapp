using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using ResidApp.Shared;
using ResidApp.Web.Security;

namespace ResidApp.FunctionalTests;

/// <summary>Hallazgo B10 de seguridad (CodeQL cs/log-forging): el filtro de identificadores vacíos escribía en el registro la ruta tal como la manda el
/// cliente, que puede traer un salto de línea (para falsificar una línea del registro), otros identificadores o el token de una invitación. Ahora solo
/// escribe el controlador y la acción, que salen del código, y el nombre del parámetro.</summary>
public sealed class InvalidIdentifierFilterTests
{
    [Fact]
    public void IdentificadorVacio_VuelveAlInicio_YElRegistroNoLlevaLaRutaDelCliente()
    {
        var log = new CapturingLogger();
        var http = new DefaultHttpContext();
        http.Request.Path = "/Acceso/Invitacion/token-secreto-123\nWarning: línea falsificada";
        var action = new ControllerActionDescriptor { ControllerName = "Enfermeria", ActionName = "Residente" };
        var context = new ExceptionContext(new ActionContext(http, new RouteData(), action), new List<IFilterMetadata>())
        {
            Exception = new InvalidIdentifierException("residenteId"),
        };

        new InvalidIdentifierFilter(log).OnException(context);

        Assert.True(context.ExceptionHandled);
        var redirect = Assert.IsType<RedirectToActionResult>(context.Result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);

        var line = Assert.Single(log.Lines);
        Assert.Contains("Enfermeria.Residente", line);
        Assert.Contains("residenteId", line);
        Assert.DoesNotContain("token-secreto-123", line);
        Assert.DoesNotContain("falsificada", line);
        Assert.DoesNotContain('\n', line);
    }

    [Fact]
    public void OtraExcepcion_NoSeTocaNiSeRegistra()
    {
        var log = new CapturingLogger();
        var context = new ExceptionContext(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>())
        {
            Exception = new InvalidOperationException("otra cosa"),
        };

        new InvalidIdentifierFilter(log).OnException(context);

        Assert.False(context.ExceptionHandled);
        Assert.Empty(log.Lines);
    }

    private sealed class CapturingLogger : ILogger<InvalidIdentifierFilter>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }
}
