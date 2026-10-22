namespace ResidApp.Web.Security;

/// <summary>
/// Inicio de sesión con contraseña (ADR 0007, incremento I1). El bloqueo y la longitud mínima son fijos; el límite de intentos por IP se
/// ajusta en la sección «Acceso» (alto en Development, por las pruebas).
/// </summary>
public sealed class AccesoOptions
{
    public const string Section = "Acceso";
    public const string RateLimitPolicy = "acceso-login";

    /// <summary>Contraseñas fallidas o segundos factores fallidos seguidos antes de bloquear la cuenta.</summary>
    public const int IntentosAntesDeBloquear = 5;

    public static readonly TimeSpan DuracionBloqueo = TimeSpan.FromMinutes(15);

    /// <summary>Longitud mínima, sin reglas de composición (NIST SP 800-63B).</summary>
    public const int LongitudMinimaContrasena = 12;

    /// <summary>Intentos por minuto y dirección IP en las pantallas de acceso.</summary>
    public int IntentosPorMinuto { get; set; } = 10;
}
