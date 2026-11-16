using Microsoft.Extensions.Options;
using ResidApp.Application.Ports;
using ResidApp.Domain.Modules;
using ResidApp.Infrastructure.IA;

namespace ResidApp.Infrastructure.Modules;

/// <summary>
/// Ajustes de los módulos opcionales por centro (ADR 0015). <see cref="ActivarTodos"/> (<c>Modulos__ActivarTodos</c>) enciende todos los módulos sin filas, para desarrollo y pruebas:
/// solo vale si el entorno DECLARA que no es producción (<c>ASPNETCORE_ENVIRONMENT=Development</c>, o <c>Entorno__Tipo=Desarrollo</c> en la Web App de dev). Un entorno sin declarar o
/// desconocido cuenta como producción, y en producción la aplicación no arranca con el ajuste puesto (<see cref="Validate"/>), igual que <see cref="IaOptions.Validate"/> con un proveedor
/// no autorizado.
/// </summary>
public sealed class ModuleOptions
{
    public const string Section = "Modulos";
    public const string EnvironmentTypeKey = "Entorno:Tipo";
    public const string DevelopmentType = "Desarrollo";

    public bool ActivarTodos { get; set; }

    /// <summary>Falla cerrado: con <c>ActivarTodos</c> y sin declarar un entorno de desarrollo, la aplicación no arranca.</summary>
    public static void Validate(ModuleOptions options, bool isDevelopmentEnvironment, string? declaredEnvironmentType)
    {
        if (options.ActivarTodos && !isDevelopmentEnvironment && !string.Equals(declaredEnvironmentType, DevelopmentType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Modulos:ActivarTodos solo se admite en desarrollo (ASPNETCORE_ENVIRONMENT=Development, o Entorno:Tipo=Desarrollo en la Web App de dev). " +
                "Un entorno sin declarar cuenta como producción, y allí los módulos los activa Plataforma, centro a centro.");
        }
    }
}

/// <summary>Los interruptores de entorno de los módulos: el ajuste de desarrollo y la clave global del proveedor de cada módulo con tercero.</summary>
public sealed class ModuleSettings(IOptions<ModuleOptions> options, IOptions<IaOptions> ia) : IModuleSettings
{
    public bool ActivateAll => options.Value.ActivarTodos;

    /// <summary>Un módulo con tercero que aquí no se conozca no funciona: falla cerrado.</summary>
    public bool IsThirdPartyConfigured(string moduleCode) => moduleCode switch
    {
        CenterModules.DictadoVoz => ia.Value.Activa,
        _ => false,
    };
}
