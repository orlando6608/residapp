using ResidApp.Application.Ports;

namespace ResidApp.Infrastructure.Correo;

/// <summary>
/// El correo de la aplicación (ADR 0012): un solo proveedor, Azure Communication Services Email, con datos en la UE. La cadena de conexión va en un ajuste de la Web App
/// (<c>Correo__CadenaConexion</c>), nunca en appsettings.json ni en el repositorio. Está apagado por defecto: sin interruptor, cadena, remitente y dirección base, la
/// aplicación no reserva ni envía nada. Fuera de producción, además, solo se envía a las direcciones de <see cref="DireccionesPermitidas"/> (los datos de prueba
/// pueden llevar correos de personas reales); un entorno solo manda a cualquiera si lo dice <see cref="TodosLosDestinos"/>.
/// </summary>
public sealed class CorreoOptions
{
    public const string Section = "Correo";

    /// <summary>El remitente verificado en el dominio de avisos; el nombre «ResidApp» lo fija el propio dominio en Azure. Sin «Responder a».</summary>
    public const string RemitenteDefecto = "no-responder@avisos.residapp.es";

    public bool Activo { get; set; }

    public string? CadenaConexion { get; set; }

    public string Remitente { get; set; } = RemitenteDefecto;

    /// <summary>Dirección de la aplicación en este entorno (por ejemplo https://dev.residapp.es): el único enlace del correo. Nunca se deduce de la cabecera Host.</summary>
    public string? UrlBase { get; set; }

    /// <summary>Idioma de los correos mientras no exista el ajuste del centro (ADR 0013): «es» o «ca».</summary>
    public string Idioma { get; set; } = "es";

    public bool TodosLosDestinos { get; set; }

    /// <summary>Interruptor propio del aviso de cambio de cita (reprogramar o cancelar el centro), además de <see cref="Activo"/>: apagado por defecto, para poder encender
    /// el aviso de propuesta sin él mientras espera su validación.</summary>
    public bool AvisoCambioCita { get; set; }

    public List<string> DireccionesPermitidas { get; set; } = [];

    public bool Operativo =>
        Activo && !string.IsNullOrWhiteSpace(CadenaConexion) && !string.IsNullOrWhiteSpace(Remitente) && UrlBaseValida
        && ProposalNoticeIdiomaValido && (TodosLosDestinos || DireccionesPermitidas.Count > 0);

    private bool UrlBaseValida =>
        Uri.TryCreate(UrlBase, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(url.Query) && string.IsNullOrEmpty(url.Fragment);

    private bool ProposalNoticeIdiomaValido => Application.Email.ProposalNoticeTemplate.Languages.Contains(Idioma);

    public EmailSettings ToSettings() => Operativo
        ? new EmailSettings(true, UrlBase!, Idioma, TodosLosDestinos, [.. DireccionesPermitidas], AvisoCambioCita)
        : EmailSettings.Off;
}
