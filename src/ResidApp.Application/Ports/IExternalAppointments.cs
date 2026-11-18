using ResidApp.Domain.Appointments;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Quien actúa sobre las citas externas, ya comprobado su ámbito activo por el caso de uso: Administración (todo el centro), Enfermería (sus unidades; escribe solo con el permiso
/// EXTERNAL_APPOINTMENT_MANAGE) o Medicina (sus unidades; solo ve). La base solo separa por centro: la unidad la aplica el repositorio con las unidades concedidas al ámbito.</summary>
public sealed record ExternalAppointmentActor(AccountId AccountId, Guid ProfileScopeId, SystemProfile Profile, CenterId CenterId);

/// <summary>Una cita externa tal como se lee, con su residente y su número de versión (bloqueo optimista). UnitId es la unidad vigente del residente, que no se guarda en la cita;
/// ResidentSuspended es la marca «residente suspendido» para el personal (la cita sigue viva, diseño valor 13).</summary>
public sealed record ExternalAppointmentRecord(
    Guid Id, ResidentId ResidentId, string ResidentName, UnitId? UnitId, ExternalAppointment Appointment, ExternalAppointmentStatus Status,
    int Version, bool ResidentSuspended);

/// <summary>
/// Citas externas del residente (script 0101). Cada escritura va en una transacción que bloquea al residente activo, vuelve a comprobar el ámbito y, en Enfermería, el permiso
/// EXTERNAL_APPOINTMENT_MANAGE, comprueba la versión vista (si otra persona guardó antes, EXTERNAL_APPOINTMENT_REVISION_CONFLICT en vez de pisarla), anota su fila en
/// dbo.citas_externas_cambios y deja el evento en dbo.eventos_auditoria con solo identificadores, nunca el contenido de un campo. Una cita realizada, no acudida o cancelada no
/// vuelve a cambiar (EXTERNAL_APPOINTMENT_CLOSED). Lo que el actor no ve, no existe para él: EXTERNAL_APPOINTMENT_NOT_FOUND.
/// </summary>
public interface IExternalAppointmentRepository
{
    /// <summary>Registra una cita programada de un residente activo y devuelve su identificador. Con operationId (el del formulario, script 0102), un reenvío del mismo formulario no crea otra cita:
    /// devuelve la ya creada, sin repetir historial ni auditoría.</summary>
    Task<Guid> CreateAsync(ExternalAppointmentActor actor, ResidentId residentId, ExternalAppointment appointment, Guid? operationId = null, CancellationToken ct = default);

    /// <summary>Cambia la ficha de una cita abierta y devuelve la versión nueva. Si cambian la fecha o la hora es una reprogramación y exige un motivo de lista cerrada; si no, es una
    /// modificación y no lleva motivo.</summary>
    Task<int> UpdateAsync(
        ExternalAppointmentActor actor, Guid appointmentId, int expectedVersion, ExternalAppointment appointment,
        ExternalAppointmentReasonCode? reasonCode, string? reasonLine, CancellationToken ct = default);

    /// <summary>Cambia el estado (programada, confirmada por el hospital, realizada, no acudida) y devuelve la versión nueva. Cancelar tiene su propio método porque exige motivo.</summary>
    Task<int> ChangeStatusAsync(
        ExternalAppointmentActor actor, Guid appointmentId, int expectedVersion, ExternalAppointmentStatus newStatus, CancellationToken ct = default);

    /// <summary>Cancela una cita abierta con un motivo de lista cerrada y devuelve la versión nueva.</summary>
    Task<int> CancelAsync(
        ExternalAppointmentActor actor, Guid appointmentId, int expectedVersion, ExternalAppointmentReasonCode reasonCode, string? reasonLine, CancellationToken ct = default);

    /// <summary>Una cita visible para el actor, o null si no existe o no está en su ámbito.</summary>
    Task<ExternalAppointmentRecord?> FindAsync(ExternalAppointmentActor actor, Guid appointmentId, CancellationToken ct = default);

    /// <summary>Las citas visibles para el actor con fecha entre from y to (ambas incluidas), por fecha y hora; sin hora, al final del día. Con residentId, solo las de ese residente.</summary>
    Task<IReadOnlyList<ExternalAppointmentRecord>> ListAsync(ExternalAppointmentActor actor, DateOnly from, DateOnly to, ResidentId? residentId = null, CancellationToken ct = default);
}
