using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Un correo ya redactado, con un solo destinatario (ADR 0012, 2.3 de la lectura de privacidad: nunca copia ni copia oculta ni varias personas).</summary>
public sealed record EmailMessage(string To, string Subject, string PlainText, string Html);

/// <summary>Lo que el proveedor contestó: aceptado (con su identificador de operación) o rechazado con un código cerrado. Aceptado no quiere decir entregado.</summary>
public sealed record EmailSendResult(bool Accepted, string? OperationId, string? FailureCode)
{
    public static EmailSendResult Ok(string? operationId) => new(true, operationId, null);

    public static EmailSendResult Fail(string failureCode) => new(false, null, failureCode);
}

/// <summary>Puerto del envío de correo (ADR 0012): el proveedor (Azure Communication Services Email) queda detrás, como la IA (ADR 0011). El remitente es
/// fijo, no hay «Responder a» y el seguimiento de aperturas está apagado.</summary>
public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>Cómo está el envío en este entorno. <see cref="Active"/> exige interruptor, cadena de conexión, remitente y dirección base; sin ella no se reserva ni se
/// envía nada. Con <see cref="AllowedRecipients"/> vacío solo se envía si <see cref="AnyRecipient"/> está encendido (producción): los datos de prueba pueden
/// llevar direcciones de personas reales.</summary>
public sealed record EmailSettings(bool Active, string BaseUrl, string Language, bool AnyRecipient, IReadOnlyList<string> AllowedRecipients)
{
    public static EmailSettings Off { get; } = new(false, "", "es", false, []);
}

/// <summary>Códigos cerrados del registro de envíos (script 0081).</summary>
public static class EmailLog
{
    public const string ProposalTemplate = "AVISO_PROPUESTA_CITA";

    public const string Pending = "PENDIENTE";
    public const string Sent = "ENVIADO";
    public const string Failed = "FALLIDO";
    public const string Omitted = "OMITIDO";

    public const string NoAddress = "SIN_CORREO";
    public const string NoAccess = "SIN_ACCESO";
    public const string ProposalNotCurrent = "PROPUESTA_NO_VIGENTE";
    public const string RecipientNotAllowed = "DESTINO_NO_PERMITIDO";
    public const string ProviderRejected = "ACS_RECHAZADO";
    public const string ProviderUnavailable = "ACS_NO_DISPONIBLE";
    public const string TimedOut = "TIEMPO_AGOTADO";
}

/// <summary>El envío reservado. Sin dirección, la fila ya quedó en OMITIDO y no hay nada que mandar.</summary>
public sealed record NoticeReservation(Guid Id, string? Address, string Language);

/// <summary>El registro de envíos del aviso de propuesta de cita (script 0081). Reservar y comprobar que sigue siendo vigente es una sola operación: la clave única
/// (plantilla, solicitud, número del cambio PROPUESTA) impide mandarlo dos veces aunque lleguen dos peticiones a la vez.</summary>
public interface IProposalNoticeStore
{
    /// <summary>Vuelve a leer la solicitud (sigue en PROPUESTA, sin caducar y con ese mismo número de cambio) y el vínculo (la misma consulta que el portal) y anota la
    /// fila: PENDIENTE si hay a quién escribir, OMITIDO con su motivo si no. Null si ese aviso ya se había reservado.</summary>
    Task<NoticeReservation?> ReserveAsync(CenterId centerId, Guid requestId, int changeNumber, string language, string templateVersion, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Cierra una fila PENDIENTE: ENVIADO, FALLIDO u OMITIDO.</summary>
    Task ResolveAsync(Guid id, string state, string? reason, string? operationId, int attempts, DateTimeOffset now, CancellationToken ct = default);
}
