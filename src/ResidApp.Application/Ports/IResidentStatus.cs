using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Un residente dado de baja (script 0041). RetainUntil es hasta cuándo se conservan sus datos; la lista y la ficha son de solo lectura.</summary>
public sealed record DischargedResident(
    ResidentId ResidentId, string DisplayName, DateOnly BirthDate, DocumentedSexCode DocumentedSex, string LastUnitName,
    ResidentDischargeReason Reason, string? ReasonText, DateTimeOffset DischargedAt, DateOnly RetainUntil);

/// <summary>Suspensión abierta de un residente por un ingreso hospitalario prolongado: sigue activo pero no se puede actuar sobre él.</summary>
public sealed record ResidentSuspension(Guid Id, DateTimeOffset Since, string? Note);

/// <summary>Lo que hizo una baja además de darla (CJ, portal 1.1 B; scripts 0042 y 0078): eventos abiertos cerrados por fallecimiento, citas futuras canceladas
/// y solicitudes de cita abiertas cerradas.</summary>
public sealed record DischargeOutcome(int EventsClosed, int AppointmentsCancelled, int AppointmentRequestsClosed);

/// <summary>Las citas futuras y las solicitudes de cita abiertas del residente: lo que una baja cancelaría y cerraría ahora mismo.</summary>
public sealed record ResidentAppointmentLoad(int FutureAppointments, int OpenRequests);

public sealed record DischargeResidentInput(
    AdministrativeResidentTarget Target, Guid OperationId, ResidentDischargeReason Reason, string? ReasonText);

/// <summary>OperationId es el identificador del episodio nuevo, así que un reenvío no reactiva dos veces.</summary>
public sealed record ReactivateResidentInput(
    AccountId AccountId, CenterId CenterId, ResidentId ResidentId, Guid OperationId, UnitId UnitId, Guid? RoomId, Guid? PlaceId);

public sealed record SuspendResidentInput(AdministrativeResidentTarget Target, Guid OperationId, string? Note);

/// <summary>
/// Baja, reactivación y suspensión del residente (script 0041). Cada una es una transacción con su auditoría y su identificador de
/// operación: un reenvío no repite nada. Estado que ya no cuadra (el residente ya no está activo, ya estaba suspendido, no hay baja que
/// reactivar...): RESIDENT_STATUS_CONFLICT.
/// </summary>
public interface IResidentStatusRepository
{
    /// <summary>Cierra el episodio y la ubicación vigentes, pone al residente inactivo, termina su suspensión si la tenía y guarda la baja
    /// (los datos no se borran). Si el motivo es fallecimiento, cierra además los eventos abiertos con una anotación de sistema (script 0042).
    /// Cancela además las citas futuras y cierra las solicitudes de cita abiertas del residente (script 0078; la suspensión no las toca). Devuelve las tres cifras.</summary>
    Task<DischargeOutcome> DischargeAsync(DischargeResidentInput input, CancellationToken ct = default);

    /// <summary>Abre un episodio y una ubicación nuevos en la unidad indicada, pone al residente activo y marca la baja como reactivada.</summary>
    Task ReactivateAsync(ReactivateResidentInput input, CancellationToken ct = default);

    Task SuspendAsync(SuspendResidentInput input, CancellationToken ct = default);

    Task ResumeAsync(AdministrativeResidentTarget target, CancellationToken ct = default);
}

/// <summary>Lecturas de Administración sobre bajas y suspensiones. Los dados de baja se ven con la regla de ámbito de la ficha: unidades
/// concedidas (la última unidad del residente) y, si el ámbito restringe residentes, solo esos.</summary>
public interface IResidentStatusDirectory
{
    Task<IReadOnlyList<DischargedResident>> ListDischargedAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default);

    Task<DischargedResident?> FindDischargedAsync(Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default);

    Task<ResidentSuspension?> FindSuspensionAsync(CenterId centerId, ResidentId residentId, CancellationToken ct = default);

    /// <summary>Cuántas citas futuras y solicitudes abiertas tiene el residente (lo que la baja cancelaría y cerraría), con la hora indicada como «ahora».</summary>
    Task<ResidentAppointmentLoad> CountAppointmentLoadAsync(CenterId centerId, ResidentId residentId, DateTimeOffset now, CancellationToken ct = default);
}
