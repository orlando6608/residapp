using System.Globalization;
using System.Net.Http;

namespace ResidApp.FunctionalTests;

/// <summary>La interfaz se sirve siempre en es-ES, aunque el navegador pida otro idioma y aunque la cultura
/// del servidor sea otra (en el contenedor Linux de Azure es la invariante y las fechas salían como
/// MM/dd/yyyy).</summary>
public class LocalizationTests : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    private readonly ResidentsFlowTests.WebAppFactory _factory;

    public LocalizationTests(ResidentsFlowTests.WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task AnyRequest_IsServedInSpanish_WhateverTheBrowserAsksFor()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/DevAuth/Login");
        request.Headers.AcceptLanguage.ParseAdd("en-US");

        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.Equal(["es-ES"], response.Content.Headers.ContentLanguage);
        Assert.Equal("29/09/2026 15:53", new DateTime(2026, 9, 29, 15, 53, 0).ToString("g", new CultureInfo("es-ES")));
    }
}
