using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ResidApp.Infrastructure.Identity;
using ResidApp.Web.Security;

namespace ResidApp.FunctionalTests;

/// <summary>
/// Límites de intentos de /Acceso con los valores reales (fuera de Development): el techo por minuto no bloquea a una residencia entera
/// detrás de una sola IP, y los fallos por IP (password spraying) sí se frenan sin contar los accesos correctos.
/// </summary>
public class AccesoLimitesTests(AccesoLimitesTests.StagingFactory factory) : IClassFixture<AccesoLimitesTests.StagingFactory>
{
    private const string Password = "contraseña de prueba larga";
    private static readonly Uri Https = new("https://localhost");

    public sealed class StagingFactory : ResidentsFlowTests.WebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Staging");
            // Los valores de Development (sin límite) no se aplican en Staging: se usan los de appsettings.json.
        }
    }

    private async Task<string> SeedFamilyAsync()
    {
        var (subject, _) = await SesionTests.SeedAccountAsync("FAMILIAR");
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<CredentialAccount>>();
        Assert.True((await users.AddPasswordAsync((await users.FindByNameAsync(subject))!, Password)).Succeeded);
        return subject;
    }

    private static async Task<HttpResponseMessage> EnterAsync(HttpClient client, string subject, string password)
    {
        var page = await client.GetStringAsync("/Acceso/Entrar");
        return await client.PostAsync("/Acceso/Entrar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = SesionTests.ExtractValue(page, "__RequestVerificationToken"),
            ["usuario"] = subject, ["contrasena"] = password,
        }));
    }

    private WebApplicationFactory<Program> With(Dictionary<string, string?> settings, TimeProvider? clock = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            if (clock is not null)
            {
                builder.ConfigureTestServices(services => services.AddSingleton(clock));
            }
        });

    private static HttpClient Client(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = Https });

    [Fact]
    public void ElTechoPorDefecto_AguantaUnCambioDeTurno()
    {
        var options = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json")).Build()
            .GetSection(AccesoOptions.Section).Get<AccesoOptions>()!;

        // 30 personas con segundo factor gastan 2 peticiones cada una; el resto es margen para reintentos y para las invitaciones (4 cada una).
        Assert.True(options.IntentosPorMinuto >= 30 * 2 * 2, $"Techo por minuto: {options.IntentosPorMinuto}");
    }

    [Fact]
    public async Task VariasPersonasDeLaMismaIp_EntranEnElMismoMinuto_SinBloquearse()
    {
        var subjects = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            subjects.Add(await SeedFamilyAsync());
        }
        var app = With(new Dictionary<string, string?>());

        // 5 personas entrando cuatro veces cada una: 20 accesos correctos desde la misma IP en un minuto (el límite antiguo era 10).
        var statuses = new List<HttpStatusCode>();
        foreach (var subject in subjects.SelectMany(subject => Enumerable.Repeat(subject, 4)))
        {
            statuses.Add((await EnterAsync(Client(app), subject, Password)).StatusCode);
        }

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.Redirect, status));
    }

    [Fact]
    public async Task LosFallosPorIp_FrenanElSpraying_PeroNoElAccesoCorrecto_YSeLevantanAlPasarLaVentana()
    {
        var clock = new TestClock();
        var app = With(new Dictionary<string, string?> { ["Acceso:FallosPorIp"] = "3" }, clock);
        var victims = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            victims.Add(await SeedFamilyAsync());
        }
        var legitimate = await SeedFamilyAsync();

        // Los accesos correctos no cuentan: tantos como se quiera antes de los fallos.
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Redirect, (await EnterAsync(Client(app), legitimate, Password)).StatusCode);
        }

        // Una contraseña probada en cuatro cuentas distintas (ninguna llega a bloquearse): al tercer fallo se corta, aunque la cuarta sea la buena.
        var spraying = new List<HttpStatusCode>();
        foreach (var victim in victims)
        {
            spraying.Add((await EnterAsync(Client(app), victim, "otra contraseña larga")).StatusCode);
        }
        var blocked = await EnterAsync(Client(app), legitimate, Password);
        var page = await Client(app).GetAsync("/Acceso/Entrar");

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], spraying);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Contains("Demasiados accesos fallidos", await blocked.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        clock.Advance(AccesoOptions.VentanaFallos + TimeSpan.FromSeconds(1));

        Assert.Equal(HttpStatusCode.Redirect, (await EnterAsync(Client(app), legitimate, Password)).StatusCode);
    }

    [Fact]
    public void ElContador_CuentaPorIp_Y_ReiniciaLaVentana()
    {
        var clock = new TestClock();
        var throttle = new AccessFailureThrottle(Options.Create(new AccesoOptions { FallosPorIp = 2 }), clock);

        throttle.RecordFailure("203.0.113.1");
        Assert.False(throttle.IsBlocked("203.0.113.1"));
        throttle.RecordFailure("203.0.113.1");
        Assert.True(throttle.IsBlocked("203.0.113.1"));
        Assert.False(throttle.IsBlocked("203.0.113.2"));

        clock.Advance(AccesoOptions.VentanaFallos - TimeSpan.FromSeconds(1));
        Assert.True(throttle.IsBlocked("203.0.113.1"));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(throttle.IsBlocked("203.0.113.1"));

        throttle.RecordFailure("203.0.113.1");
        Assert.False(throttle.IsBlocked("203.0.113.1"));
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan time) => _now += time;
    }
}
