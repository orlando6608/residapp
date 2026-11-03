using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using ResidApp.Web.Security;

namespace ResidApp.FunctionalTests;

/// <summary>La política de privacidad (Home/Privacy) es pública y ya no es el texto provisional. Su tabla de cookies sale del código: si se añade o se renombra una
/// cookie, esta prueba avisa de que hay que actualizar la página. Borrador del texto y decisiones abiertas: docs/privacidad/politica-privacidad-portal.md.</summary>
public class PrivacidadPageTests : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    private readonly ResidentsFlowTests.WebAppFactory _factory;

    public PrivacidadPageTests(ResidentsFlowTests.WebAppFactory factory) => _factory = factory;

    private static readonly string[] Cookies =
    [
        SessionAuthentication.CookieName, ActiveProfileScopeCookie.CookieName, ClinicalAccessDeclarationCookie.CookieName,
        TransferHistoryCookie.CookieName, PendingAccessCookie.CookieName, InvitationCookie.CookieName,
        PasskeyStateCookie.CookieName, PendientesCjAccess.CookieName,
    ];

    [Fact]
    public async Task Privacidad_SeVeSinSesion_YYaNoEsElTextoProvisional()
    {
        var response = await _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).GetAsync("/Home/Privacy");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Todavía no está redactada", html);
        Assert.Contains("El responsable de tus datos es el centro", html);
        // Mientras falten datos, la página lo dice a la vista y no los inventa.
        Assert.Contains("Texto en revisión", html);
        Assert.Contains("Por completar", html);
    }

    [Fact]
    public async Task Privacidad_ListaTodasLasCookiesDeLaAplicacion()
    {
        var html = await _factory.CreateClient().GetStringAsync("/Home/Privacy");

        foreach (var cookie in Cookies)
            Assert.Contains($"<code>{cookie}</code>", html);
    }
}
