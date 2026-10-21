using System.Security.Cryptography;
using System.Text;

namespace ResidApp.Web.Security;

/// <summary>
/// El acceso de desarrollo (/DevAuth/Login): elegir una cuenta sembrada sin contraseña. No es autenticación real y solo existe si se
/// habilita a propósito (sección «DevAuth»). Fuera de Development exige además una clave, que vive como ajuste de la Web App y nunca en
/// el repositorio: sin ella, la aplicación no arranca (<see cref="Validate"/>). Cierra el hallazgo C1 hasta que llegue el proveedor de
/// identidad (ADR 0007).
/// </summary>
public sealed class DevAuthOptions
{
    public const string Section = "DevAuth";
    public const string RateLimitPolicy = "devauth-login";
    public const int MinKeyLength = 16;

    public bool Habilitado { get; set; }

    public string? Clave { get; set; }

    /// <summary>Intentos de inicio de sesión por minuto y dirección IP.</summary>
    public int IntentosPorMinuto { get; set; } = 10;

    public bool RequiresKey => !string.IsNullOrEmpty(Clave);

    /// <summary>Compara en tiempo constante (sobre los resúmenes, que tienen la misma longitud).</summary>
    public bool KeyMatches(string? key) =>
        !RequiresKey || CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(key ?? string.Empty)), SHA256.HashData(Encoding.UTF8.GetBytes(Clave!)));

    /// <summary>Falla cerrado: habilitado fuera de Development sin una clave suficiente, la aplicación no arranca.</summary>
    public static void Validate(DevAuthOptions options, IHostEnvironment environment)
    {
        if (options.Habilitado && !environment.IsDevelopment() && (options.Clave?.Length ?? 0) < MinKeyLength)
        {
            throw new InvalidOperationException(
                $"DevAuth:Habilitado está activo fuera de Development sin una DevAuth:Clave de al menos {MinKeyLength} caracteres. " +
                "Ponla como ajuste de la Web App (DevAuth__Clave), nunca en appsettings.json, o deshabilita DevAuth.");
        }
    }
}
