using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Modules;
using ResidApp.Shared;
using ResidApp.Web.Security;

namespace ResidApp.FunctionalTests;

/// <summary>
/// Módulos opcionales por centro (ADR 0015, punto 8): la guardia que impide dejar un módulo sin interruptor, el comportamiento del atributo <c>[RequiereModulo]</c> y el arranque con
/// <c>Modulos__ActivarTodos</c> (solo en un entorno que declara ser de desarrollo; uno desconocido cuenta como producción y no arranca).
/// </summary>
public class ModulosGuardiaTests
{
    private static IEnumerable<(MethodInfo Action, RequiereModuloAttribute Mark)> MarkedActions() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(m => (Action: m, Mark: m.GetCustomAttribute<RequiereModuloAttribute>() ?? m.DeclaringType!.GetCustomAttribute<RequiereModuloAttribute>()))
            .Where(x => x.Mark is not null)
            .Select(x => (x.Action, x.Mark!));

    [Fact]
    public void Cada_modulo_del_catalogo_tiene_alguna_accion_marcada_y_toda_marca_apunta_a_un_modulo_del_catalogo()
    {
        var marked = MarkedActions().ToList();
        Assert.All(marked, x => Assert.NotNull(CenterModules.Find(x.Mark.ModuloCodigo)));

        var withoutAction = CenterModules.All.Select(m => m.Code).Where(code => marked.All(x => x.Mark.ModuloCodigo != code)).ToHashSet();
        Assert.True(withoutAction.Count == 0, "Módulos del catálogo sin ninguna acción con [RequiereModulo]: " + string.Join(", ", withoutAction));
    }

    [Fact]
    public void El_catalogo_declara_sus_codigos_sin_repetir_y_cada_modulo_con_tercero_lleva_su_corte_global_y_su_ficha()
    {
        Assert.Equal(CenterModules.All.Count, CenterModules.All.Select(m => m.Code).Distinct().Count());
        Assert.All(CenterModules.All, module =>
        {
            Assert.Matches("^[A-Z_]{3,24}$", module.Code);
            Assert.NotEmpty(module.PrivacyConditions);
            Assert.False(string.IsNullOrWhiteSpace(module.GlobalCut));
            Assert.True(File.Exists(Path.Combine(RepositoryRoot(), module.PrivacySheetPath.Replace('/', Path.DirectorySeparatorChar))), $"Falta la ficha {module.PrivacySheetPath}");
            Assert.All(module.Dependencies, dependency => Assert.NotNull(CenterModules.Find(dependency)));
        });
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("No se encuentra la raíz del repositorio (CLAUDE.md).");
    }

    // ---- El atributo ----

    private static readonly CenterModule WithData = new(
        "CON_DATOS", "Módulo con datos propios", [], "1", "docs/privacidad/x.md", ["C"], HasOwnData: true, UsesThirdParty: false, GlobalCut: "n/a");

    private sealed class Repository(CenterModuleChange? last) : ICenterModuleRepository
    {
        public Task<CenterModuleChange?> FindLastAsync(CenterId centerId, string moduleCode, CancellationToken ct = default) => Task.FromResult(last);

        public Task AppendAsync(PlatformAccess access, NewCenterModuleChange change, DateOnly today, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Scopes(Guid scopeId, CenterId centerId) : IProfileScopeDirectoryProvider
    {
        public Task<IReadOnlyList<ActiveProfileScope>> ListActiveAsync(string externalSubject, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ActiveProfileScope>>([new ActiveProfileScope(scopeId, AccountId.From(Guid.NewGuid()), centerId, "Centro", SystemProfile.Administracion)]);

        public Task<IReadOnlyList<ScopeUnit>> ListUnitsAsync(string externalSubject, Guid profileScopeId, CenterId centerId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListPermissionsAsync(string externalSubject, Guid profileScopeId, CenterId centerId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Session(bool signedIn) : ISessionIdentityProvider
    {
        public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
            Task.FromResult(signedIn ? new VerifiedIdentity("test-sujeto") : null);
    }

    private sealed class Settings(bool thirdParty = true) : IModuleSettings
    {
        public bool ActivateAll => false;

        public bool IsThirdPartyConfigured(string moduleCode) => thirdParty;
    }

    private static async Task<(IActionResult? Result, bool Called)> RunAsync(
        RequiereModuloAttribute attribute, CenterModuleChange? last, bool withCookie = true, bool signedIn = true, string module = "CON_DATOS")
    {
        var scopeId = Guid.NewGuid();
        var access = new CenterModuleAccess(
            new Repository(last), new Settings(), new Scopes(scopeId, CenterId.From(Guid.NewGuid())), new Session(signedIn), TimeProvider.System, [WithData]);
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(access).BuildServiceProvider() };
        if (withCookie)
        {
            var response = new DefaultHttpContext().Response;
            ActiveProfileScopeCookie.Write(response, scopeId, Guid.NewGuid(), "Centro", SystemProfile.Administracion);
            http.Request.Headers.Cookie = response.Headers.SetCookie.ToString().Split(';')[0];
        }

        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), new object());
        var called = false;
        await attribute.OnActionExecutionAsync(context, () =>
        {
            called = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, [], new object()));
        });
        return (context.Result, called);
    }

    private static CenterModuleChange Enabled(DateOnly from, DateOnly until) => new(1, true, from, until);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task El_atributo_deja_pasar_con_el_modulo_activo()
    {
        var (result, called) = await RunAsync(new RequiereModuloAttribute("CON_DATOS"), Enabled(Today.AddDays(-1), Today.AddDays(1)));

        Assert.True(called);
        Assert.Null(result);
    }

    [Fact]
    public async Task El_atributo_responde_404_si_el_centro_no_tiene_el_modulo_no_hay_cookie_o_no_hay_sesion()
    {
        var attribute = new RequiereModuloAttribute("CON_DATOS");
        foreach (var (result, called) in new[]
                 {
                     await RunAsync(attribute, null),
                     await RunAsync(attribute, Enabled(Today.AddDays(-1), Today.AddDays(1)), withCookie: false),
                     await RunAsync(attribute, Enabled(Today.AddDays(-1), Today.AddDays(1)), signedIn: false),
                 })
        {
            Assert.False(called);
            Assert.IsType<NotFoundResult>(result);
        }
    }

    [Fact]
    public async Task En_solo_lectura_el_atributo_rechaza_crear_o_editar_con_un_mensaje_y_deja_consultar_si_la_accion_es_de_lectura()
    {
        var expired = Enabled(Today.AddDays(-10), Today.AddDays(-1));

        var (write, writeCalled) = await RunAsync(new RequiereModuloAttribute("CON_DATOS"), expired);
        var (read, readCalled) = await RunAsync(new RequiereModuloAttribute("CON_DATOS") { Lectura = true }, expired);

        Assert.False(writeCalled);
        var conflict = Assert.IsType<ObjectResult>(write);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.True(readCalled);
        Assert.Null(read);
    }

    // ---- Arranque ----

    private static ResidentsFlowTests.WebAppFactory FactoryWith(string environment, params (string Key, string Value)[] settings) =>
        new StagingLikeFactory(environment, settings);

    private sealed class StagingLikeFactory(string environment, (string Key, string Value)[] settings) : ResidentsFlowTests.WebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value)));
        }
    }

    /// <summary>En «Production» no se arranca el host: en CI (servidor en UTC) cae antes por la zona horaria (ServerTimeZone.Verify) y la prueba no llegaría a la regla de módulos. Se prueba la regla tal como la llama Program.cs.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("desarrollo")]
    [InlineData("Produccion")]
    public void En_produccion_ActivarTodos_no_pasa_la_validacion_de_arranque(string? declared)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ResidApp.Infrastructure.Modules.ModuleOptions.Validate(new ResidApp.Infrastructure.Modules.ModuleOptions { ActivarTodos = true }, isDevelopmentEnvironment: false, declared));

        Assert.Contains("Modulos:ActivarTodos", error.Message);
        ResidApp.Infrastructure.Modules.ModuleOptions.Validate(new ResidApp.Infrastructure.Modules.ModuleOptions { ActivarTodos = false }, isDevelopmentEnvironment: false, declared);
        ResidApp.Infrastructure.Modules.ModuleOptions.Validate(new ResidApp.Infrastructure.Modules.ModuleOptions { ActivarTodos = true }, isDevelopmentEnvironment: true, declared);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Otro")]
    public void Con_ActivarTodos_un_entorno_que_no_declara_ser_de_desarrollo_no_arranca(string environment)
    {
        using var factory = FactoryWith(environment, ("Modulos:ActivarTodos", "true"));

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Modulos:ActivarTodos", error.ToString());
    }

    [Theory]
    [InlineData("desarrollo")]
    [InlineData("Produccion")]
    [InlineData("")]
    [InlineData(" Desarrollo")]
    public void Con_ActivarTodos_una_declaracion_que_no_es_exactamente_Desarrollo_no_vale(string declared)
    {
        using var factory = FactoryWith("Staging", ("Modulos:ActivarTodos", "true"), ("Entorno:Tipo", declared));

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Modulos:ActivarTodos", error.ToString());
    }

    [Fact]
    public void Con_ActivarTodos_arranca_en_el_entorno_Development()
    {
        using var factory = FactoryWith("Development", ("Modulos:ActivarTodos", "true"));

        using var client = factory.CreateClient();
    }

    [Fact]
    public void Con_ActivarTodos_arranca_si_el_entorno_declara_ser_de_desarrollo_y_sin_el_ajuste_arranca_en_cualquier_entorno()
    {
        using var declared = FactoryWith("Staging", ("Modulos:ActivarTodos", "true"), ("Entorno:Tipo", "Desarrollo"));
        using var withoutSetting = FactoryWith("Staging");

        using (declared.CreateClient())
        {
        }

        using (withoutSetting.CreateClient())
        {
        }
    }
}
