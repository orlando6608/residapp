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

    public static readonly TimeSpan VentanaFallos = TimeSpan.FromMinutes(15);

    /// <summary>Peticiones por minuto y dirección IP en las pantallas de acceso, acierten o fallen: solo un techo contra inundaciones.
    /// Una residencia sale por una sola IP, y un inicio de sesión correcto con segundo factor gasta 2 y usar una invitación, 4.</summary>
    public int IntentosPorMinuto { get; set; } = 120;

    /// <summary>Accesos fallidos (contraseña, segundo factor o invitación no válida) que se admiten por dirección IP en <see cref="VentanaFallos"/>.</summary>
    public int FallosPorIp { get; set; } = 20;
}
