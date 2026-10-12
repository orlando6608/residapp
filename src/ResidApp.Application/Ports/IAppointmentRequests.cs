using ResidApp.Domain.Appointments;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Un paso de la vida de una solicitud (CIT-C03), tal como lo ve Administración. Detail es lo interno: el profesional apuntado a una propuesta
/// o el motivo de un cierre.</summary>
public sealed record AppointmentRequestChange(
    AppointmentRequestAction Action, SystemProfile ActorProfile, DateTimeOffset At, DateTimeOffset? ProposedStartsAt, string? Detail);

/// <summary>Una solicitud de cita tal como la ve Administración (script 0057): con el residente y el familiar que la envió, lo que pidió y la
/// propuesta vigente. ProposalExpired es una propuesta cuya hora ya empezó (se calcula al leer).</summary>
public sealed record AdministrationAppointmentRequest(
    Guid Id, Guid ResidentId, string ResidentName, string FamilyName, string Relationship, AppointmentTeam Team, AppointmentModality Modality,
    AppointmentRequestReason Reason, string? Comment, AppointmentAvailability Availability, AppointmentRequestStatus Status, DateTimeOffset CreatedAt,
    DateTimeOffset? ProposedStartsAt, DateTimeOffset? ProposedEndsAt, string? InternalProfessional, bool ProposalExpired, Guid? AppointmentId,
    IReadOnlyList<AppointmentRequestChange> Changes);

/// <summary>
/// Las solicitudes de cita para Administración del centro (script 0057; historia 8 de Administración, CIT-S02, CIT-S04, CIT-C02, CIT-C03). Cada
/// operación comprueba en su transacción que quien actúa es Administración del centro y deja su auditoría. Errores de negocio como excepciones de
/// dominio: APPOINTMENT_REQUEST_NOT_OPEN, APPOINTMENT_SLOT_UNAVAILABLE, APPOINTMENT_PROPOSAL_INVALID. Una solicitud que no es del centro: AccessDenied.
/// </summary>
public interface IAppointmentRequestRepository
{
    /// <summary>Las solicitudes del centro, las más antiguas primero; con <paramref name="onlyOpen"/>, solo las pendientes y las que tienen una propuesta.</summary>
    Task<IReadOnlyList<AdministrationAppointmentRequest>> ListAsync(
        AccountAdministrationAccess access, bool onlyOpen, DateTimeOffset now, CancellationToken ct = default);

    Task<AdministrationAppointmentRequest> ReadAsync(
        AccountAdministrationAccess access, Guid requestId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Los huecos libres del equipo y la modalidad de una solicitud pendiente, sin la antelación mínima (Administración no la tiene).</summary>
    Task<IReadOnlyList<AppointmentSlot>> ListProposalSlotsAsync(
        AccountAdministrationAccess access, Guid requestId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Propone un hueco libre a una solicitud pendiente y lo reserva mientras la propuesta viva.</summary>
    Task ProposeAsync(
        AccountAdministrationAccess access, Guid requestId, DateTimeOffset startsAt, string? professional, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Cierra una solicitud pendiente o con propuesta; el motivo es interno.</summary>
    Task CloseAsync(AccountAdministrationAccess access, Guid requestId, string reason, DateTimeOffset now, CancellationToken ct = default);
}

/// <summary>Un paso de la vida de una solicitud tal como lo ve la familia: sin quién y sin lo interno; ByCenter distingue lo que hizo el centro.</summary>
public sealed record FamilyAppointmentRequestChange(AppointmentRequestAction Action, DateTimeOffset At, bool ByCenter);

/// <summary>Una solicitud propia de la familia (CIT-S01 a CIT-S04). La propuesta solo viaja cuando existe; nunca lleva un profesional ni nada interno.
/// CanAccept es una propuesta viva; CanRequestAnotherTime, una propuesta (viva o caducada); CanCancel, una solicitud aún abierta.</summary>
public sealed record FamilyAppointmentRequest(
    Guid Id, AppointmentTeam Team, AppointmentModality Modality, AppointmentRequestReason Reason, string? Comment, AppointmentAvailability Availability,
    AppointmentRequestStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? ProposedStartsAt, DateTimeOffset? ProposedEndsAt, bool ProposalExpired,
    Guid? AppointmentId, bool CanAccept, bool CanRequestAnotherTime, bool CanCancel, IReadOnlyList<FamilyAppointmentRequestChange> Changes);

/// <summary>
/// Solicitudes de cita del Portal Familiar (script 0057; CIT-S01 a CIT-S04). El acceso a cada residente es el del resto del portal (vínculo y
/// autorización Activa y vigente: <c>AccessibleLinks</c>); sin él, null o AccessDenied sin distinguir un vínculo ajeno de uno inexistente. Cada
/// solicitud la ve y la cambia solo la cuenta que la envió. Errores de negocio como excepciones de dominio: APPOINTMENT_REQUEST_DISABLED,
/// APPOINTMENT_MODALITY_UNAVAILABLE, APPOINTMENT_LIMIT_REACHED, APPOINTMENT_REQUEST_LIMIT_REACHED, APPOINTMENT_REQUEST_NOT_OPEN,
/// APPOINTMENT_REQUEST_EXPIRED, APPOINTMENT_SLOT_UNAVAILABLE.
/// </summary>
public interface IFamilyAppointmentRequests
{
    /// <summary>Las solicitudes de la cuenta sobre el residente, las abiertas primero. Null si el vínculo no da acceso.</summary>
    Task<IReadOnlyList<FamilyAppointmentRequest>?> ListAsync(
        FamilyPortalAccess access, Guid linkId, DateOnly today, DateTimeOffset now, CancellationToken ct = default);

    Task<FamilyAppointmentRequest?> ReadAsync(
        FamilyPortalAccess access, Guid linkId, Guid requestId, DateOnly today, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Envía una solicitud. La misma operación de la misma cuenta devuelve la solicitud ya creada. Null si el vínculo no da acceso.</summary>
    Task<FamilyAppointmentRequest?> CreateAsync(
        FamilyPortalAccess access, Guid linkId, AppointmentRequestInput input, Guid operationId, DateOnly today, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Acepta la propuesta viva: crea la cita confirmada. Repetirlo sobre una solicitud ya aceptada devuelve la misma. Null si no hay acceso.</summary>
    Task<FamilyAppointmentRequest?> AcceptAsync(
        FamilyPortalAccess access, Guid linkId, Guid requestId, DateOnly today, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Descarta la propuesta y vuelve a pedir horario: la solicitud queda pendiente y el hueco, libre.</summary>
    Task<FamilyAppointmentRequest?> RequestAnotherTimeAsync(
        FamilyPortalAccess access, Guid linkId, Guid requestId, DateOnly today, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Cancela una solicitud propia aún abierta. Devuelve false si el vínculo no da acceso o la solicitud no es de esta cuenta.</summary>
    Task<bool> CancelAsync(
        FamilyPortalAccess access, Guid linkId, Guid requestId, DateOnly today, DateTimeOffset now, CancellationToken ct = default);
}
