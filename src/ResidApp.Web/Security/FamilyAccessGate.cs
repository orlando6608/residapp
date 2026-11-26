using Microsoft.Extensions.Options;
using ResidApp.Infrastructure.Modules;

namespace ResidApp.Web.Security;

/// <summary>Cómo entra una cuenta que solo es de familia, además de con su contraseña.</summary>
public enum FamilyEntry
{
    /// <summary>Solo con la contraseña: únicamente en un entorno que se declara de desarrollo y con el código apagado.</summary>
    PasswordOnly,
    /// <summary>Con el código que se le manda por correo.</summary>
    EmailCode,
    /// <summary>No entra: el código está apagado en un entorno que no se declara de desarrollo.</summary>
    Closed,
}

/// <summary>
/// Decide cómo entra la familia (ADR 0012, pieza 3; Orlando, 2026-11-26): con <c>Acceso__CodigoFamiliarActivo</c>, con el código; apagado, solo con la contraseña y solo si el
/// entorno declara ser de desarrollo (<c>ASPNETCORE_ENVIRONMENT=Development</c> o <c>Entorno__Tipo=Desarrollo</c>, como los módulos del ADR 0015). Un entorno sin declarar cuenta
/// como producción: allí la familia nunca entra solo con la contraseña.
/// </summary>
public sealed class FamilyAccessGate(IOptions<AccesoOptions> options, IWebHostEnvironment environment, IConfiguration configuration)
{
    public FamilyEntry Entry =>
        options.Value.CodigoFamiliarActivo ? FamilyEntry.EmailCode
        : environment.IsDevelopment()
          || string.Equals(configuration[ModuleOptions.EnvironmentTypeKey], ModuleOptions.DevelopmentType, StringComparison.Ordinal)
            ? FamilyEntry.PasswordOnly
            : FamilyEntry.Closed;
}
