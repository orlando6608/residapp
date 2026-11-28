using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ResidApp.Application.Ports;

namespace ResidApp.FunctionalTests;

/// <summary>Bandejas, listas e inicios de Enfermería, Medicina y Auxiliar cuando la lectura falla: ninguna pantalla puede decir que no haya
/// nada ni enseñar un cero. Cada caso se mira dos veces, leyendo bien y con la lectura fallando.</summary>
public class ClinicoFalloDeLecturaPagesTests(ResidentsFlowTests.WebAppFactory factory) : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    [Theory]
    [InlineData("ENFERMERIA", "/Enfermeria/ParaCerrar", "No hay episodios pendientes de cerrar.")]
    [InlineData("ENFERMERIA", "/Enfermeria/Indicaciones", "No hay indicaciones pendientes.")]
    [InlineData("ENFERMERIA", "/Enfermeria/Seguimientos", "No hay seguimientos abiertos.")]
    [InlineData("ENFERMERIA", "/Enfermeria/Escalados", "No hay escalados abiertos.")]
    [InlineData("ENFERMERIA", "/Enfermeria/Comunicaciones", "No hay comunicaciones familiares en los últimos 30 días.")]
    [InlineData("ENFERMERIA", "/Enfermeria/Ordinarios", "No hay eventos ordinarios abiertos.")]
    [InlineData("ENFERMERIA", "/Enfermeria/Prioritarios", "No hay eventos prioritarios abiertos.")]
    [InlineData("ENFERMERIA", "/Enfermeria/Protocolos", "No hay protocolos urgentes activos.")]
    [InlineData("ENFERMERIA", "/Enfermeria/Residentes", "No hay residentes en tu ámbito.")]
    [InlineData("MEDICINA", "/Medicina/Comunicaciones", "No hay comunicaciones familiares en los últimos 30 días.")]
    [InlineData("MEDICINA", "/Medicina/Escalados", "No hay escalados ni eventos propios pendientes.")]
    [InlineData("MEDICINA", "/Medicina/Protocolos", "No hay protocolos urgentes activos.")]
    [InlineData("MEDICINA", "/Medicina/Seguimientos", "No hay seguimientos médicos abiertos.")]
    [InlineData("MEDICINA", "/Medicina/Indicaciones", "No hay indicaciones emitidas pendientes.")]
    [InlineData("MEDICINA", "/Medicina/Residentes", "No hay residentes en tu ámbito.")]
    [InlineData("AUXILIAR", "/Auxiliar", "No tienes residentes asignados.")]
    public async Task UnaListaSoloDiceQueNoHayNada_SiLaHaPodidoLeer(string profile, string url, string empty)
    {
        Assert.Contains(empty, await PageAsync(factory, profile, url));

        var failed = await PageAsync(Failing(), profile, url);
        Assert.Contains("No se ha podido completar la operación.", failed);
        Assert.DoesNotContain(empty, failed);
        Assert.DoesNotContain("<table", failed);
    }

    [Theory]
    [InlineData("ENFERMERIA", "/Enfermeria", 8)]
    [InlineData("MEDICINA", "/Medicina", 5)]
    public async Task UnInicio_SiNoPuedeLeerLasCifras_LoDiceYNoEnseñaCeros(string profile, string url, int figures)
    {
        const string noFigure = "Ahora mismo no se puede mostrar la cifra.";
        const string noWarnings = "Ahora mismo no se pueden mostrar los avisos de hitos fuera de plazo o a punto de vencer.";

        var page = await PageAsync(factory, profile, url);
        Assert.Equal(figures, Regex.Count(page, "class=\"cifra\">0<"));
        Assert.DoesNotContain(noFigure, page);
        Assert.DoesNotContain(noWarnings, page);

        var failed = await PageAsync(Failing(), profile, url);
        Assert.Equal(figures, Regex.Count(failed, Regex.Escape(noFigure)));
        Assert.DoesNotContain("class=\"cifra", failed);
        Assert.Contains(noWarnings, failed);
    }

    private WebApplicationFactory<Program> Failing() => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.AddScoped(_ => DispatchProxy.Create<IChangeInboxDirectory, FailingPort>());
        services.AddScoped(_ => DispatchProxy.Create<IEnfermeriaResidentDirectory, FailingPort>());
        services.AddScoped(_ => DispatchProxy.Create<IAssignedResidentDirectory, FailingPort>());
    }));

    private static async Task<string> PageAsync(WebApplicationFactory<Program> app, string profile, string url)
    {
        var (subject, _) = await SesionTests.SeedAccountAsync(profile);
        var client = await SesionTests.LoginAsync(app, subject);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/ProfileScope/Select")).StatusCode);
        return WebUtility.HtmlDecode(await client.GetStringAsync(url));
    }

    /// <summary>Un puerto de lectura en el que falla cualquier consulta. Es público porque DispatchProxy hereda de él.</summary>
    public class FailingPort : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new InvalidOperationException("Fallo de prueba.");
    }
}
