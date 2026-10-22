namespace ResidApp.Web.Models;

/// <summary>Inicio de sesión con contraseña. SesionCaducada: se llegó aquí al guardar un formulario con la sesión ya terminada.</summary>
public sealed record AccesoEntrarViewModel(string? Usuario, string? ReturnUrl, bool SesionCaducada, bool AccesoDesarrollo);

/// <summary>Alta del segundo factor: la clave para escribirla a mano (en grupos de 4) y la dirección otpauth:// del QR.</summary>
public sealed record ActivarSegundoFactorViewModel(string Usuario, string ClaveAgrupada, string DireccionAutenticador);

/// <summary>Los códigos de recuperación recién creados, que solo se enseñan una vez.</summary>
public sealed record CodigosRecuperacionViewModel(IReadOnlyList<string> Codigos, string Continuar);

/// <summary>Credenciales de la cuenta de la sesión en el acceso de desarrollo.</summary>
public sealed record DevAuthCredencialesViewModel(string Usuario, bool TieneContrasena, bool SegundoFactorActivo, string? Aviso);
