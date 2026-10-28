using ResidApp.Domain.Appointments;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Una modalidad que un equipo admite en las citas del centro (CIT-C01).</summary>
public sealed record AppointmentModalitySetting(AppointmentTeam Team, AppointmentModality Modality);

/// <summary>Lo que Administración configura del servicio de citas (script 0054). Blocks son los bloqueos vigentes que aún no han terminado; Windows,
/// las franjas no retiradas. Version ordena dos ediciones simultáneas de la configuración general.</summary>
public sealed record AppointmentServiceView(
    AppointmentServiceSettings Settings, IReadOnlyList<AppointmentModalitySetting> Modalities, IReadOnlyList<AppointmentWindow> Windows,
    IReadOnlyList<AppointmentBlock> Blocks, int Version);

/// <summary>Un paso de la vida de una cita (CIT-C03). Reason es el motivo interno que dio Administración; la familia no lo recibe. FromDischarge: lo hizo el
/// sistema a causa de la baja del residente (script 0078); la autoría sigue siendo la cuenta de Administración que dio la baja.</summary>
public sealed record AppointmentChange(
    AppointmentChangeAction Action, SystemProfile ActorProfile, DateTimeOffset At, DateTimeOffset? PreviousStartsAt, DateTimeOffset? NewStartsAt,
    string? Reason, bool FromDischarge = false);

/// <summary>Una cita tal como la ve Administración: con el residente y el familiar que la reservó.</summary>
public sealed record AdministrationAppointment(
    Guid Id, Guid ResidentId, string ResidentName, string FamilyName, string Relationship, AppointmentTeam Team, AppointmentModality Modality,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, AppointmentStatus Status, IReadOnlyList<AppointmentChange> Changes);

/// <summary>
/// Configuración y agenda de las citas para Administración del centro (script 0054; historia 7 de Administración). Cada operación comprueba en su
/// transacción que quien actúa es Administración del centro, y deja su auditoría. Errores de negocio como excepciones de dominio:
/// APPOINTMENT_SERVICE_REVISION_CONFLICT, APPOINTMENT_WINDOW_OVERLAP, APPOINTMENT_SLOT_UNAVAILABLE, APPOINTMENT_NOT_CHANGEABLE,
/// APPOINTMENT_NOT_FINISHABLE. Una franja, un bloqueo o una cita que no son del centro: AccessDenied.
/// </summary>
public interface IAppointmentServiceRepository
{
    Task<AppointmentServiceView> ReadAsync(AccountAdministrationAccess access, CancellationToken ct = default);

    /// <summary>Guarda la configuración general y las modalidades de cada equipo. Devuelve la versión nueva (la misma si no cambia nada).</summary>
    Task<int> SaveSettingsAsync(
        AccountAdministrationAccess access, int expectedVersion, AppointmentServiceSettings settings,
        IReadOnlyCollection<AppointmentModalitySetting> modalities, CancellationToken ct = default);

    Task AddWindowAsync(AccountAdministrationAccess access, AppointmentWindow window, CancellationToken ct = default);

    Task RetireWindowAsync(AccountAdministrationAccess access, Guid windowId, CancellationToken ct = default);

    Task AddBlockAsync(AccountAdministrationAccess access, AppointmentBlock block, CancellationToken ct = default);

    Task RetireBlockAsync(AccountAdministrationAccess access, Guid blockId, CancellationToken ct = default);

    /// <summary>Las citas que empiezan entre <paramref name="from"/> (incluido) y <paramref name="to"/> (excluido), por hora, con su historial.</summary>
    Task<IReadOnlyList<AdministrationAppointment>> ListAgendaAsync(
        AccountAdministrationAccess access, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Las citas confirmadas del centro que ya empezaron y nadie finalizó (CJ, portal 5.8: «citas pasadas sin cerrar», para que no se olviden),
    /// de la más antigua a la más reciente. Las finaliza Administración a mano: que pasara la hora no dice si se celebró.</summary>
    Task<IReadOnlyList<AdministrationAppointment>> ListPastUnclosedAsync(
        AccountAdministrationAccess access, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Los huecos a los que se puede pasar una cita confirmada: los de su equipo y modalidad, sin la antelación mínima (Administración no la
    /// tiene) y sin contar el hueco de la propia cita.</summary>
    Task<IReadOnlyList<AppointmentSlot>> ListRescheduleSlotsAsync(
        AccountAdministrationAccess access, Guid appointmentId, DateTimeOffset now, CancellationToken ct = default);

    Task CancelAsync(AccountAdministrationAccess access, Guid appointmentId, string reason, DateTimeOffset now, CancellationToken ct = default);

    Task RescheduleAsync(
        AccountAdministrationAccess access, Guid appointmentId, DateTimeOffset newStartsAt, string reason, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Marca como finalizada una cita confirmada cuya hora de inicio ya pasó.</summary>
    Task FinishAsync(AccountAdministrationAccess access, Guid appointmentId, DateTimeOffset now, CancellationToken ct = default);
}

/// <summary>Un paso de la vida de una cita tal como lo ve la familia (CIT-C03): sin quién y sin el motivo; ByCenter distingue lo que hizo el centro.</summary>
public sealed record FamilyAppointmentChange(AppointmentChangeAction Action, DateTimeOffset At, bool ByCenter);

/// <summary>Una cita propia de la familia (CIT-03, FAM-14). CanCancel y CanReschedule ya tienen en cuenta el estado, la antelación mínima y el modo
/// vigente del centro. Nunca lleva un profesional.</summary>
public sealed record FamilyAppointment(
    Guid Id, AppointmentTeam Team, AppointmentModality Modality, DateTimeOffset StartsAt, DateTimeOffset EndsAt, AppointmentStatus Status,
    bool CanCancel, bool CanReschedule, IReadOnlyList<FamilyAppointmentChange> Changes);

/// <summary>La pantalla de citas de un residente: el servicio del centro (el modo vigente y las modalidades de cada equipo) y las citas que reservó la
/// cuenta, las próximas primero.</summary>
public sealed record FamilyAppointmentsHome(
    FamilyPortalResident Resident, AppointmentServiceSettings Settings, IReadOnlyList<AppointmentModalitySetting> Modalities,
    IReadOnlyList<FamilyAppointment> Appointments);

/// <summary>
/// Citas del Portal Familiar con reserva directa (script 0054; CIT-D01 a CIT-D05). El acceso a cada residente es el del resto del portal (vínculo y
/// autorización Activa y vigente: <c>AccessibleLinks</c>); sin él, null o AccessDenied sin distinguir un vínculo ajeno de uno inexistente. Cada cita
/// la ve y la cambia solo la cuenta que la reservó. Errores de negocio como excepciones de dominio: APPOINTMENT_SERVICE_DISABLED,
/// APPOINTMENT_MODALITY_UNAVAILABLE, APPOINTMENT_SLOT_UNAVAILABLE, APPOINTMENT_LIMIT_REACHED, APPOINTMENT_CHANGE_TOO_LATE,
/// APPOINTMENT_NOT_CHANGEABLE.
/// </summary>
public interface IFamilyAppointments
{
    Task<FamilyAppointmentsHome?> ReadHomeAsync(
        FamilyPortalAccess access, Guid linkId, DateOnly today, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Los huecos libres del equipo y la modalidad; con <paramref name="reschedulingId"/>, los de una cita propia que se quiere mover (su
    /// propio hueco cuenta como libre). Null si el vínculo no da acceso; vacío si el servicio no está activo o la modalidad no se admite.</summary>
    Task<IReadOnlyList<AppointmentSlot>?> ListSlotsAsync(
        FamilyPortalAccess access, Guid linkId, AppointmentTeam team, AppointmentModality modality, Guid? reschedulingId, DateOnly today, DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>Reserva un hueco. La misma operación de la misma cuenta devuelve la cita ya creada. Null si el vínculo no da acceso.</summary>
    Task<FamilyAppointment?> BookAsync(
        FamilyPortalAccess access, Guid linkId, AppointmentTeam team, AppointmentModality modality, DateTimeOffset startsAt, Guid operationId,
        DateOnly today, DateTimeOffset now, CancellationToken ct = default);

    Task<FamilyAppointment?> RescheduleAsync(
        FamilyPortalAccess access, Guid linkId, Guid appointmentId, DateTimeOffset newStartsAt, DateOnly today, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Cancela una cita propia. Devuelve false si el vínculo no da acceso o la cita no es de esta cuenta.</summary>
    Task<bool> CancelAsync(
        FamilyPortalAccess access, Guid linkId, Guid appointmentId, DateOnly today, DateTimeOffset now, CancellationToken ct = default);

    Task<FamilyAppointment?> ReadAppointmentAsync(
        FamilyPortalAccess access, Guid linkId, Guid appointmentId, DateOnly today, DateTimeOffset now, CancellationToken ct = default);
}
