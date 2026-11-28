using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ResidApp.Application.Ports;

namespace ResidApp.FunctionalTests;

/// <summary>«Comunicados a la familia» de Dirección Clínica y de Administración cuando la lista no se puede leer: no puede decir que no haya comunicados.</summary>
public class ComunicadosFalloDeLecturaPagesTests(ResidentsFlowTests.WebAppFactory factory) : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    private const string Failure = "Ahora mismo no se pueden mostrar los comunicados a la familia.";
    private const string Empty = "No hay comunicados a la familia en los últimos";

    [Theory]
    [InlineData("DIRECCION_CLINICA", "/Direccion/Comunicados", true)]
    [InlineData("DIRECCION_CLINICA", "/Direccion/Comunicados", false)]
    [InlineData("ADMINISTRACION", "/Administracion/Comunicados", true)]
    [InlineData("ADMINISTRACION", "/Administracion/Comunicados", false)]
    public async Task SoloDiceQueNoHayComunicados_SiLosHaPodidoLeer(string profile, string url, bool fails)
    {
        WebApplicationFactory<Program> app = fails
            ? factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddScoped<IFamilyCommunicationDirectory, FailingCommunications>()))
            : factory;
        var (subject, _) = await SesionTests.SeedAccountAsync(profile);
        var client = await SesionTests.LoginAsync(app, subject);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/ProfileScope/Select")).StatusCode);

        var page = WebUtility.HtmlDecode(await client.GetStringAsync(url));

        Assert.Equal(fails, page.Contains(Failure, StringComparison.Ordinal));
        Assert.Equal(!fails, page.Contains(Empty, StringComparison.Ordinal));
    }

    private sealed class FailingCommunications : IFamilyCommunicationDirectory
    {
        public Task<IReadOnlyList<AdministrationFamilyCommunication>> ListAsync(
            FamilyCommunicationAccess access, DateTimeOffset since, CancellationToken ct = default) =>
            throw new InvalidOperationException("Fallo de prueba.");
    }
}
