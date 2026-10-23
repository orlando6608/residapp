using ResidApp.Application.Ports;

namespace ResidApp.Web.Models;

/// <summary>Inicio de sesión con contraseña. SesionCaducada: se llegó aquí al guardar un formulario con la sesión ya terminada.</summary>
public sealed record AccesoEntrarViewModel(string? Usuario, string? ReturnUrl, bool SesionCaducada, bool AccesoDesarrollo, bool PasskeysActivas);

/// <summary>Alta del segundo factor: la clave para escribirla a mano (en grupos de 4) y la dirección otpauth:// del QR.</summary>
public sealed record ActivarSegundoFactorViewModel(string Usuario, string ClaveAgrupada, string DireccionAutenticador);

/// <summary>Los códigos de recuperación recién creados, que solo se enseñan una vez.</summary>
public sealed record CodigosRecuperacionViewModel(IReadOnlyList<string> Codigos, string Continuar, string Titulo = "Segundo factor activado");

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

/// <summary>Una sesión abierta de la cuenta en «Mi cuenta» (ADR 0007, I3). Las fechas son UTC; EsActual marca la de esta petición.</summary>
public sealed record SesionAbiertaViewModel(Guid Id, DateTime CreadaUtc, DateTime UltimaActividadUtc, DateTime CaducaUtc, string? Ip, bool EsActual);

/// <summary>Un acceso reciente de la cuenta, con su descripción en español.</summary>
public sealed record AccesoRecienteViewModel(DateTime OcurridoUtc, string Descripcion, bool EsFallo, string? Ip);

/// <summary>Una passkey de la cuenta en «Mi cuenta» (ADR 0007, I4). Id va en base64url; la fecha es UTC. Sincronizada: el sistema del
/// dispositivo la copia a otros de la misma persona (iCloud, Google).</summary>
public sealed record PasskeyViewModel(string Id, string Nombre, DateTime CreadaUtc, bool Sincronizada);

/// <summary>«Mi cuenta» (ADM-30 y FAM-16): la propia contraseña, los códigos de recuperación y las sesiones abiertas. Los errores son de la
/// sección que se acaba de enviar; Aviso, el resultado de una acción correcta.</summary>
public sealed class CuentaViewModel
{
    public required string Usuario { get; init; }
    public required string Nombre { get; init; }
    public required IReadOnlyList<string> Ambitos { get; init; }
    public required bool TieneContrasena { get; init; }
    public required bool SegundoFactorActivo { get; init; }
    public required int CodigosRestantes { get; init; }
    public required IReadOnlyList<SesionAbiertaViewModel> Sesiones { get; init; }
    public required IReadOnlyList<AccesoRecienteViewModel> Accesos { get; init; }
    public required IReadOnlyList<PasskeyViewModel> Passkeys { get; init; }
    public bool PasskeysActivas { get; init; }
    public bool PasskeyObligatoria { get; init; }
    public string? Aviso { get; init; }
    public string? ErrorContrasena { get; init; }
    public string? ErrorCodigos { get; init; }
    public string? ErrorVinculacion { get; init; }
}

public static class AccesoRecienteDisplay
{
    public static string Descripcion(AccessEventType type) => type switch
    {
        AccessEventType.Entrada => "Entrada",
        AccessEventType.EntradaDesarrollo => "Entrada (acceso de desarrollo)",
        AccessEventType.ContrasenaFallida => "Contraseña incorrecta",
        AccessEventType.Bloqueo => "Cuenta bloqueada por demasiados intentos",
        AccessEventType.SegundoFactorFallido => "Código del móvil incorrecto",
        AccessEventType.SegundoFactorAlta => "Código del móvil preparado",
        AccessEventType.CodigoRecuperacionUsado => "Entrada con un código de recuperación",
        AccessEventType.CredencialesDesarrollo => "Credenciales cambiadas desde el acceso de desarrollo",
        AccessEventType.InvitacionUsada => "Invitación de acceso usada",
        AccessEventType.RecuperacionUsada => "Invitación de recuperación usada",
        AccessEventType.InvitacionNoValida => "Invitación no válida",
        AccessEventType.ContrasenaCambiada => "Contraseña cambiada",
        AccessEventType.CodigosRegenerados => "Códigos de recuperación nuevos",
        AccessEventType.SesionCerrada => "Sesión cerrada desde Mi cuenta",
        AccessEventType.VinculacionAceptada => "Vinculación con otro centro aceptada",
        AccessEventType.EntradaPasskey => "Entrada con passkey",
        AccessEventType.PasskeyAlta => "Passkey añadida",
        AccessEventType.PasskeyBaja => "Passkey quitada",
        AccessEventType.PasskeyFallida => "Passkey rechazada",
        _ => type.ToString(),
    };

    public static bool EsFallo(AccessEventType type) => type is
        AccessEventType.ContrasenaFallida or AccessEventType.Bloqueo
        or AccessEventType.SegundoFactorFallido or AccessEventType.InvitacionNoValida or AccessEventType.PasskeyFallida;
}
