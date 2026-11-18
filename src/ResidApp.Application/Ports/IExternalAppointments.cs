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
    int Version, bool ResidentSuspended, string? UnitName = null);

/// <summary>
/// Citas externas del residente (script 0101). Cada escritura va en una transacción que bloquea al residente activo, vuelve a comprobar el ámbito y, en Enfermería, el permiso
/// EXTERNAL_APPOINTMENT_MANAGE, comprueba la versión vista (si otra persona guardó antes, EXTERNAL_APPOINTMENT_REVISION_CONFLICT en vez de pisarla), anota su fila en
/// dbo.citas_externas_cambios y deja el evento en dbo.eventos_auditoria con solo identificadores, nunca el contenido de un campo. Una cita realizada, no acudida o cancelada no
/// vuelve a cambiar (EXTERNAL_APPOINTMENT_CLOSED). Lo que el actor no ve, no existe para él: EXTERNAL_APPOINTMENT_NOT_FOUND.
/// </summary>
public interface IExternalAppointmentRepository
{
    /// <summary>Registra una cita programada de un residente activo y devuelve su identificador. Con operationId (el del formulario, script 0102), un reenvío del mismo formulario no crea otra cita:
    /// devuelve la ya creada, sin repetir historial ni auditoría. Con originAppointmentId (script 0103) la cita nueva es «la siguiente» de otra del mismo residente, realizada o no acudida, y guarda de cuál viene.</summary>
    Task<Guid> CreateAsync(
        ExternalAppointmentActor actor, ResidentId residentId, ExternalAppointment appointment, Guid? operationId = null, Guid? originAppointmentId = null, CancellationToken ct = default);

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

    /// <summary>Las citas abiertas (programadas o confirmadas por el hospital) con fecha entre from y to, de los residentes asignados al Auxiliar actor y con ubicación vigente en una de sus unidades,
    /// por fecha y hora (sin hora, al final del día). Solo los campos de <see cref="ExternalAppointmentForAuxiliar"/>.</summary>
    Task<IReadOnlyList<ExternalAppointmentForAuxiliar>> ListForAuxiliarAsync(ExternalAppointmentActor actor, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>De una cita visible para el actor: la siguiente cita prevista de la versión vigente de la nota (solo ese dato, no el resultado ni la línea) y la siguiente cita ya registrada, si la hay
    /// (una cita no cancelada que guarda esta como origen). Null si la cita no existe o no está en su ámbito.</summary>
    Task<ExternalAppointmentFollowUp?> FindFollowUpAsync(ExternalAppointmentActor actor, Guid appointmentId, CancellationToken ct = default);

    /// <summary>Solo Enfermería y Medicina (script 0103): la última versión de la nota «al terminar», que puede ser una retirada (Retracted), o null si no hay ninguna. Quien la usa trata una retirada como «sin nota» y usa su número como versión vista al anotar de nuevo. Cita fuera del ámbito: acceso denegado.</summary>
    Task<ExternalNoteVersion?> FindCurrentNoteAsync(ExternalAppointmentActor actor, Guid appointmentId, CancellationToken ct = default);

    /// <summary>Solo Enfermería y Medicina: todas las versiones de la nota, de la más reciente a la más antigua, con su autoría. Cada apertura deja un evento de auditoría (sin contenido).</summary>
    Task<IReadOnlyList<ExternalNoteVersion>> ListNoteVersionsAsync(ExternalAppointmentActor actor, Guid appointmentId, CancellationToken ct = default);

    /// <summary>Solo Enfermería de la unidad actual del residente, sin que haga falta el permiso de gestionar citas: anota una versión nueva de la nota de una cita realizada y devuelve su número. expectedNumber es
    /// el de la versión vigente que vio quien escribe (0 si no había); si otra persona anotó antes, EXTERNAL_APPOINTMENT_REVISION_CONFLICT. Con operationId, un reenvío no crea otra versión.</summary>
    Task<int> SaveNoteAsync(
        ExternalAppointmentActor actor, Guid appointmentId, int expectedNumber, ExternalAppointmentNote note, Guid? operationId = null, CancellationToken ct = default);

    /// <summary>Retira la nota vigente: una versión nueva marcada como retirada, sin contenido; las anteriores se conservan. Mismas reglas de quién escribe y de versión vista que <see cref="SaveNoteAsync"/>.</summary>
    Task<int> RetractNoteAsync(
        ExternalAppointmentActor actor, Guid appointmentId, int expectedNumber, ExternalNoteRetractionReason reason, Guid? operationId = null, CancellationToken ct = default);
}

/// <summary>Lo que Administración, Enfermería y Medicina ven de la nota de una cita: solo la siguiente cita prevista (de la versión vigente) y la siguiente cita ya registrada. Ni el resultado, ni la línea,
/// ni quién la escribió, ni si hay nota (el DPO lo pidió así: el botón de registrar la siguiente cita sale siempre y no delata nada).</summary>
public sealed record ExternalAppointmentFollowUp(ExternalNextVisit Planned, Guid? RegisteredId, DateOnly? RegisteredDate);

/// <summary>Una versión de la nota «al terminar»: lo que anotó Enfermería con su autoría. Solo la leen Enfermería y Medicina; ni Administración, ni el Auxiliar, ni la familia, ni los correos o los avisos.
/// Una versión retirada no lleva resultado, línea ni siguiente cita, solo el motivo.</summary>
public sealed record ExternalNoteVersion(
    Guid Id, int Number, bool Retracted, ExternalNoteResult? Result, string? Text, DateOnly? NextDate, int? NextMonths,
    ExternalNoteRetractionReason? RetractionReason, DateTimeOffset At, string AuthorName);

/// <summary>
/// Una cita externa tal como la ve el Auxiliar (PR 3; DPO, apartados 2 y 6.4): el residente, el día y la hora (o «por confirmar»), qué preparar y quién lo lleva y cómo (el tipo de acompañante y de
/// transporte, sin nombres ni si están confirmados). <b>No tiene</b> tipo de cita, especialidad, lugar, nota ni nombre de familiar: el servidor no los devuelve, no están «ocultos en pantalla». Una
/// prueba por reflexión fija este conjunto de propiedades.
/// </summary>
public sealed record ExternalAppointmentForAuxiliar(
    Guid Id, ResidentId ResidentId, string ResidentName, DateOnly Date, TimeOnly? Time, ExternalPreparation Preparation,
    ExternalTransportMode TransportMode, ExternalCompanionMode CompanionMode);
