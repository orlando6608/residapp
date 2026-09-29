using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ResidApp.FunctionalTests;

/// <summary>La interfaz se sirve siempre en es-ES, aunque el navegador pida otro idioma y aunque la cultura
/// del servidor sea otra (en el contenedor Linux de Azure es la invariante y las fechas salían como
/// MM/dd/yyyy), con el mismo formato de fecha en Windows y en Linux.</summary>
public class LocalizationTests : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    private readonly ResidentsFlowTests.WebAppFactory _factory;

    public LocalizationTests(ResidentsFlowTests.WebAppFactory factory) => _factory = factory;

    [Theory]
    [InlineData("en-US")]
    [InlineData("es-ES")]
    public async Task AnyRequest_IsServedInSpanish_WhateverTheBrowserAsksFor(string acceptLanguage)
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/DevAuth/Login");
        request.Headers.AcceptLanguage.ParseAdd(acceptLanguage);

        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.Equal(["es-ES"], response.Content.Headers.ContentLanguage);
    }

    [Fact]
    public void SpanishCulture_FormatsShortDatesTheSameOnEveryPlatform()
    {
        var options = _factory.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;

        var culture = Assert.Single(options.SupportedCultures!);
        Assert.Same(culture, options.DefaultRequestCulture.Culture);
        Assert.Equal("29/09/2026 09:05", new DateTime(2026, 9, 29, 9, 5, 0).ToString("g", culture));
    }
}
