using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ResidApp.Infrastructure.Persistence;

namespace ResidApp.FunctionalTests;

/// <summary>
/// ADR 0007, incremento I0 (hallazgos C1 y A2): la sesión es una cookie cifrada validada contra dbo.sesiones en cada petición, toda
/// pantalla exige sesión salvo las públicas, y la cookie de desarrollo antigua ya no da acceso. Equivalen a los casos AUTH-SPIKE T02 a
/// T06 del legado: identidad solo por el puerto, revocación en la petición siguiente, renovación sin ampliar la caducidad absoluta,
/// inactividad y caducidad que fallan cerradas, y cuenta suspendida denegada con sesión válida.
/// </summary>
public class SesionTests(ResidentsFlowTests.WebAppFactory factory) : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    private static readonly string[] AnonymousControllers = ["Acceso", "AccesoCj", "DevAuth", "Home", "Passkey"];

    internal static string ExtractValue(string html, string inputName) =>
        Regex.Match(html, $"name=\"{Regex.Escape(inputName)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

    /// <summary>Una cuenta con un único ámbito activo del perfil indicado, en un centro y una unidad nuevos.</summary>
    internal static async Task<(string Subject, Guid AccountId)> SeedAccountAsync(string profileCode = "ADMINISTRACION")
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var accountId = Guid.NewGuid();
        var centerId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        var subject = $"sesion-{suffix}";
        using var connection = await new SqlConnectionFactory(ResidentsFlowTests.WebAppFactory.TestConnectionString).OpenAsync();
        await connection.ExecuteAsync("""
            DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
            INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @subject, 'ACTIVE', @now);
            INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en) VALUES (@centerId, @code, @code, 'ACTIVE', @now);
            INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@unitId, @centerId, @code, @code, 'ACTIVE', @now);
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@scopeId, @accountId, @centerId, @profileCode, 'ACTIVE', @now, @accountId);
            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @scopeId, @centerId, @unitId, @now, @accountId);
            """, new { accountId, subject, centerId, unitId, scopeId, profileCode, code = $"SESION-{suffix}" });
        return (subject, accountId);
    }

    /// <summary>Inicia sesión por el acceso de desarrollo y devuelve el cliente, que guarda las cookies.</summary>
    internal static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> app, string subject, string? key = null, Uri? baseAddress = null)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = baseAddress ?? new Uri("http://localhost") });
        var response = await PostLoginAsync(client, subject, key);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    internal static async Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string subject, string? key = null)
    {
        var page = await client.GetStringAsync("/DevAuth/Login");
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"), ["externalSubject"] = subject,
        };
        if (key is not null)
        {
            form["clave"] = key;
        }
        return await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(form));
    }

    /// <summary>El valor «residapp_sesion=…» que puso el servidor, para usarlo desde otro cliente.</summary>
    internal static string SessionCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("residapp_sesion=", StringComparison.Ordinal)).Split(';')[0];

    internal static bool RedirectsToLogin(HttpResponseMessage response, string loginPath = "/Acceso/Entrar") =>
        response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location is { } location
        && (location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?')[0]) == loginPath;

    private static async Task<T?> ScalarAsync<T>(string sql, object parameters)
    {
        using var connection = await new SqlConnectionFactory(ResidentsFlowTests.WebAppFactory.TestConnectionString).OpenAsync();
        return await connection.ExecuteScalarAsync<T>(sql, parameters);
    }

    [Fact]
    public async Task SinSesion_TodaAccion_RedirigeAlInicioDeSesion_SalvoLasPublicas()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .ToList();
        var anonymous = endpoints.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerName).Distinct().Order().ToList();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var open = new List<string>();
        foreach (var endpoint in endpoints.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null))
        {
            // La dirección real de la acción: controlador y acción del enrutado convencional, y un identificador cualquiera.
            var pattern = endpoint.RoutePattern;
            var path = "/" + string.Join("/", pattern.PathSegments
                .Select(segment => string.Concat(segment.Parts.Select(part => part switch
                {
                    RoutePatternLiteralPart literal => literal.Content,
                    RoutePatternParameterPart parameter when pattern.RequiredValues.TryGetValue(parameter.Name, out var required) && required is not null
                        => required.ToString(),
                    RoutePatternParameterPart { IsOptional: true } => "",
                    RoutePatternParameterPart { Default: not null } parameter => parameter.Default.ToString(),
                    _ => Guid.NewGuid().ToString(),
                }))).Where(text => text.Length > 0));
            var method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? "GET";
            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
            if (!RedirectsToLogin(response))
            {
                open.Add($"{method} {path} → {(int)response.StatusCode}");
            }
        }

        Assert.Equal(AnonymousControllers, anonymous);
        Assert.True(endpoints.Count > 150, $"Solo se encontraron {endpoints.Count} acciones.");
        Assert.Empty(open);
    }

    [Theory]
    [InlineData("/css/site.css")]
    [InlineData("/js/intercambio.js")]
    [InlineData("/manifest.json")]
    [InlineData("/sw.js")]
    [InlineData("/flujo-de-trabajo.html")]
    [InlineData("/acceso-y-sesion.html")]
    [InlineData("/Home/Offline")]
    [InlineData("/Home/Manual")]
    [InlineData("/")]
    public async Task SinSesion_LoPublicoSeVe(string path)
    {
        var response = await factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LaCookieDeDesarrolloAntigua_YUnaSesionInventada_NoDanAcceso()
    {
        var (subject, _) = await SeedAccountAsync();
        var oldCookie = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        oldCookie.DefaultRequestHeaders.Add("Cookie", $"residapp_dev_subject={subject}");
        var forged = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        forged.DefaultRequestHeaders.Add("Cookie", $"residapp_sesion={subject}");

        Assert.True(RedirectsToLogin(await oldCookie.GetAsync("/ProfileScope/Select")));
        Assert.True(RedirectsToLogin(await forged.GetAsync("/ProfileScope/Select")));
    }

    [Fact]
    public async Task CerrarSesion_InvalidaLaCookieEnLaPeticionSiguiente_AunqueSeReutilice()
    {
        var (subject, accountId) = await SeedAccountAsync();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var cookie = SessionCookie(await PostLoginAsync(client, subject));
        var reused = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        reused.DefaultRequestHeaders.Add("Cookie", cookie);
        var before = await reused.GetAsync("/ProfileScope/Select");

        var home = await client.GetStringAsync("/DevAuth/Login");
        await client.PostAsync("/Acceso/Salir", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(home, "__RequestVerificationToken"),
        }));
        var after = await reused.GetAsync("/ProfileScope/Select");

        Assert.False(RedirectsToLogin(before));
        Assert.True(RedirectsToLogin(after));
        Assert.Equal("LOGOUT", await ScalarAsync<string>(
            "SELECT motivo_cierre FROM dbo.sesiones WHERE cuenta_id = @accountId", new { accountId }));
    }

    [Fact]
    public async Task CuentaSuspendida_SeDeniegaConLaSesionAbierta()
    {
        var (subject, accountId) = await SeedAccountAsync();
        var client = await LoginAsync(factory, subject);
        var before = await client.GetAsync("/ProfileScope/Select");
        await ScalarAsync<int>("UPDATE dbo.cuentas SET estado = 'SUSPENDED' WHERE id = @accountId; SELECT 1", new { accountId });

        Assert.False(RedirectsToLogin(before));
        Assert.True(RedirectsToLogin(await client.GetAsync("/ProfileScope/Select")));
    }

    [Fact]
    public async Task EntrarConOtraCuenta_CierraLaSesionAnterior()
    {
        var first = await SeedAccountAsync();
        var second = await SeedAccountAsync();
        var client = await LoginAsync(factory, first.Subject);

        await PostLoginAsync(client, second.Subject);

        Assert.Equal("CAMBIO_IDENTIDAD", await ScalarAsync<string>(
            "SELECT motivo_cierre FROM dbo.sesiones WHERE cuenta_id = @accountId", new { first.AccountId }));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.sesiones WHERE cuenta_id = @accountId AND cerrada_en IS NULL", new { second.AccountId }));
    }

    [Fact]
    public async Task Inactividad_FallaCerrada()
    {
        var clock = new TestClock();
        var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));
        var (subject, _) = await SeedAccountAsync();
        var client = await LoginAsync(app, subject);

        clock.Advance(TimeSpan.FromMinutes(29));
        var active = await client.GetAsync("/ProfileScope/Select");
        clock.Advance(TimeSpan.FromMinutes(31));
        var idle = await client.GetAsync("/ProfileScope/Select");

        Assert.False(RedirectsToLogin(active));
        Assert.True(RedirectsToLogin(idle));
    }

    [Fact]
    public async Task Renovar_NoAmpliaLaCaducidadAbsoluta_QueFallaCerrada()
    {
        var clock = new TestClock();
        var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));
        var (subject, accountId) = await SeedAccountAsync();
        var client = await LoginAsync(app, subject);
        var expiresAt = await ScalarAsync<DateTime>("SELECT caduca_en FROM dbo.sesiones WHERE cuenta_id = @accountId", new { accountId });

        // Con actividad cada 20 minutos la sesión nunca está inactiva, y la cookie se renueva una y otra vez.
        var toLogin = new List<bool>();
        for (var elapsed = TimeSpan.FromMinutes(20); elapsed < TimeSpan.FromHours(12); elapsed += TimeSpan.FromMinutes(20))
        {
            clock.Advance(TimeSpan.FromMinutes(20));
            toLogin.Add(RedirectsToLogin(await client.GetAsync("/ProfileScope/Select")));
        }
        // Un minuto pasadas las 12 horas (SQL guarda la caducidad redondeada al milisegundo), con solo 21 minutos sin uso.
        clock.Advance(TimeSpan.FromMinutes(21));
        var expired = await client.GetAsync("/ProfileScope/Select");

        Assert.DoesNotContain(true, toLogin);
        Assert.Equal(expiresAt, await ScalarAsync<DateTime>("SELECT caduca_en FROM dbo.sesiones WHERE cuenta_id = @accountId", new { accountId }));
        Assert.True(RedirectsToLogin(expired));
        Assert.Equal("CADUCIDAD", await ScalarAsync<string>("SELECT motivo_cierre FROM dbo.sesiones WHERE cuenta_id = @accountId", new { accountId }));
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan time) => _now += time;
    }
}

/// <summary>
/// El acceso de desarrollo fuera de Development, como en Azure dev: pide la clave, no dice si el usuario existe, limita los intentos,
/// todas las cookies llevan Secure, solo se fía del balanceador de App Service y, sin clave, la aplicación no arranca. Deshabilitado, no existe.
/// </summary>
public class SesionFueraDeDesarrolloTests(SesionFueraDeDesarrolloTests.StagingFactory factory) : IClassFixture<SesionFueraDeDesarrolloTests.StagingFactory>
{
    private const string Key = "clave-de-desarrollo-de-prueba";
    private static readonly Uri Https = new("https://localhost");

    public sealed class StagingFactory : ResidentsFlowTests.WebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Staging");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DevAuth:Habilitado"] = "true", ["DevAuth:Clave"] = Key, ["DevAuth:IntentosPorMinuto"] = "1000",
            }));
        }
    }

    private WebApplicationFactory<Program> With(string name, string? value) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { [name] = value })));

    [Fact]
    public async Task PideLaClave_ConMensajeNeutro_YLasCookiesSonSecure()
    {
        var (subject, _) = await SesionTests.SeedAccountAsync();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = Https });
        var page = await client.GetAsync("/DevAuth/Login");

        var wrongKey = WebUtility.HtmlDecode(await (await SesionTests.PostLoginAsync(client, subject, "otra-clave")).Content.ReadAsStringAsync());
        var unknownUser = WebUtility.HtmlDecode(await (await SesionTests.PostLoginAsync(client, $"sesion-{Guid.NewGuid():N}", Key)).Content.ReadAsStringAsync());
        var ok = await SesionTests.PostLoginAsync(client, subject, Key);

        Assert.Contains("Clave de desarrollo", await page.Content.ReadAsStringAsync());
        Assert.All(page.Headers.GetValues("Set-Cookie"), cookie => Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Usuario o clave incorrectos.", wrongKey);
        Assert.Contains("Usuario o clave incorrectos.", unknownUser);
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Contains("secure", ok.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("residapp_sesion=", StringComparison.Ordinal)),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LimitaLosIntentosPorMinuto()
    {
        var client = With("DevAuth:IntentosPorMinuto", "2").CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = Https });

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            statuses.Add((await SesionTests.PostLoginAsync(client, "nadie", "otra-clave")).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }

    // App Service termina el TLS y llega con X-Forwarded-Proto; en Linux, desde la red de enlace local (medido en Azure dev). Solo se le
    // cree a su balanceador: desde una IP pública la cabecera se ignora.
    [Theory]
    [InlineData("::ffff:169.254.130.1", true)]
    [InlineData("::ffff:10.0.0.5", true)]
    [InlineData("203.0.113.9", false)]
    public async Task SeFiaDelBalanceadorDeAppService_YDeNadieMas(string remoteIp, bool trusted)
    {
        Task<HttpContext> Send(string path) => factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            context.Request.Method = HttpMethods.Get;
            context.Request.Host = new HostString("app-residapp-dev.azurewebsites.net");
            context.Request.Path = path;
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            context.Request.Headers["X-Forwarded-For"] = "198.51.100.7";
        });

        var publicPage = await Send("/Home/Manual");
        var protectedPage = await Send("/Administracion");

        Assert.Equal(trusted, publicPage.Response.Headers.ContainsKey("Strict-Transport-Security"));
        Assert.Equal(trusted ? "https" : "http", new Uri(protectedPage.Response.Headers.Location!).Scheme);
    }

    [Fact]
    public async Task Deshabilitado_NoExiste_YSinSesionSeVaAlInicioDeSesionConContrasena()
    {
        var client = With("DevAuth:Habilitado", "false").CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = Https });

        var login = await client.GetAsync("/DevAuth/Login");
        var credentials = await client.GetAsync("/DevAuth/Credenciales");
        var protectedPage = await client.GetAsync("/Administracion");
        var entrar = WebUtility.HtmlDecode(await client.GetStringAsync("/Acceso/Entrar"));

        Assert.Equal(HttpStatusCode.NotFound, login.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, credentials.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, protectedPage.StatusCode);
        Assert.Equal("/Acceso/Entrar", protectedPage.Headers.Location!.AbsolutePath);
        Assert.Contains("Contraseña", entrar);
        Assert.DoesNotContain("Acceso de desarrollo", entrar);
    }

    [Fact]
    public void HabilitadoSinClave_LaAplicacionNoArranca()
    {
        var error = Assert.ThrowsAny<Exception>(() => With("DevAuth:Clave", "corta").CreateClient());

        Assert.Contains("DevAuth:Clave", error.ToString());
    }
}
