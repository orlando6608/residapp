namespace ResidApp.Web.Models;

/// <summary>Inicio de sesión con contraseña. SesionCaducada: se llegó aquí al guardar un formulario con la sesión ya terminada.</summary>
public sealed record AccesoEntrarViewModel(string? Usuario, string? ReturnUrl, bool SesionCaducada, bool AccesoDesarrollo);

/// <summary>Alta del segundo factor: la clave para escribirla a mano (en grupos de 4) y la dirección otpauth:// del QR.</summary>
public sealed record ActivarSegundoFactorViewModel(string Usuario, string ClaveAgrupada, string DireccionAutenticador);

/// <summary>Los códigos de recuperación recién creados, que solo se enseñan una vez.</summary>
public sealed record CodigosRecuperacionViewModel(IReadOnlyList<string> Codigos, string Continuar);

/// <summary>Credenciales de la cuenta de la sesión en el acceso de desarrollo.</summary>
public sealed record DevAuthCredencialesViewModel(string Usuario, bool TieneContrasena, bool SegundoFactorActivo);

/// <summary>La hoja que se entrega a la persona (ADR 0007, I2): Direccion lleva el token y solo se enseña esta vez. Volver es la
/// pantalla desde la que se emitió.</summary>
public sealed record HojaInvitacionViewModel(
    ResidApp.Domain.Accounts.AccessInvitationKind Tipo, string Usuario, string? Nombre, string Centro, DateTime CaducaEnUtc, string Direccion,
    string Volver);

/// <summary>Usar una invitación: para quién es y si es de recuperación.</summary>
public sealed record UsarInvitacionViewModel(string Usuario, bool Recuperacion);

/// <summary>Estado del acceso de una cuenta en su ficha o en la autorización del familiar.</summary>
public sealed record EstadoInvitacionViewModel(string Usuario, bool TieneContrasena, DateTime? CaducaEnUtc, string Volver);
