using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Una imagen que viaja dentro del propio mensaje y el HTML cita por <c>cid:</c> (sin petición a ningún servidor al abrirlo, así que no hay manera de saber quién lo abrió).</summary>
public sealed record InlineImage(string ContentId, string Name, string ContentType, byte[] Content);

/// <summary>Un correo ya redactado, con un solo destinatario (ADR 0012, 2.3 de la lectura de privacidad: nunca copia ni copia oculta ni varias personas).</summary>
public sealed record EmailMessage(string To, string Subject, string PlainText, string Html, IReadOnlyList<InlineImage>? InlineImages = null);

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
/// llevar direcciones de personas reales. <see cref="ChangeNotice"/> es el interruptor propio del aviso de cambio de cita (apagado por defecto): además de <see cref="Active"/>.</summary>
public sealed record EmailSettings(
    bool Active, string BaseUrl, string Language, bool AnyRecipient, IReadOnlyList<string> AllowedRecipients, bool ChangeNotice = false)
{
    public static EmailSettings Off { get; } = new(false, "", "es", false, []);
}

/// <summary>Códigos cerrados del registro de envíos (scripts 0081 y 0082).</summary>
public static class EmailLog
{
    public const string ProposalTemplate = "AVISO_PROPUESTA_CITA";
    public const string ChangeTemplate = "AVISO_CAMBIO_CITA";

    public const string Pending = "PENDIENTE";
    public const string Sent = "ENVIADO";
    public const string Failed = "FALLIDO";
    public const string Omitted = "OMITIDO";

    public const string NoAddress = "SIN_CORREO";
    public const string NoAccess = "SIN_ACCESO";
    public const string ProposalNotCurrent = "PROPUESTA_NO_VIGENTE";
    public const string AppointmentNotCurrent = "CITA_NO_VIGENTE";
    public const string AppointmentPassed = "CITA_PASADA";
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

/// <summary>Un aviso de cambio reservado y todavía sin enviar. Solo lo necesario para comprobarlo y mandarlo: ni dirección, ni hora, ni motivo.</summary>
public sealed record PendingChangeNotice(Guid Id, CenterId CenterId, Guid AppointmentId, int ChangeNumber, string Language);

/// <summary>Qué hay que hacer con un aviso de cambio al llegar el momento de enviarlo: la dirección a la que escribir o el motivo cerrado de no enviarlo.</summary>
public sealed record ChangeNoticePreparation(string? Address, string? OmitReason);

/// <summary>El registro de envíos del aviso de cambio de cita (script 0082). Se reserva al cambiar la cita (una fila PENDIENTE, sin dirección) y la tarea programada
/// lo envía más tarde: reclamarlo, volver a comprobar que sigue siendo vigente y a quién escribir, y resolverlo. La clave única (plantilla, cita, número del cambio)
/// impide reservarlo dos veces. El motivo del cambio no se lee nunca.</summary>
public interface IChangeNoticeStore
{
    /// <summary>Reserva el aviso del cambio (la cita y su número). No hace nada si ya estaba reservado o si la cita no existe. Si el familiar no tiene correo, la fila se resuelve más tarde como OMITIDO.</summary>
    Task RegisterAsync(CenterId centerId, Guid appointmentId, int changeNumber, string language, string templateVersion, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Los avisos PENDIENTES sin reclamar reservados antes de <paramref name="reservedBefore"/>, los más antiguos primero.</summary>
    Task<IReadOnlyList<PendingChangeNotice>> ListDueAsync(DateTimeOffset reservedBefore, int limit, CancellationToken ct = default);

    /// <summary>Lo reclama para esta ejecución. False si otra ejecución ya lo reclamó: así no sale dos veces aunque haya dos copias de la aplicación.</summary>
    Task<bool> ClaimAsync(Guid id, CancellationToken ct = default);

    /// <summary>Vuelve a leer la cita y su último cambio, y el vínculo (la misma consulta que el portal), y dice a quién escribir o por qué no.</summary>
    Task<ChangeNoticePreparation> PrepareAsync(PendingChangeNotice notice, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Cierra una fila PENDIENTE: ENVIADO, FALLIDO u OMITIDO.</summary>
    Task ResolveAsync(Guid id, string state, string? reason, string? operationId, int attempts, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Si la conexión de la tarea tiene la identidad que ve todos los centros (miembro del rol residapp_tareas, o db_owner en local y en las pruebas). Con el usuario limitado
    /// de la aplicación no ve ninguna fila y no lo avisa: así la tarea lo comprueba y lo dice como error en vez de quedarse callada.</summary>
    Task<bool> HasTaskIdentityAsync(CancellationToken ct = default);
}
