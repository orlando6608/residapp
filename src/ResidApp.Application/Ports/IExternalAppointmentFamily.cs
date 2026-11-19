using ResidApp.Domain.Appointments;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Cuántas citas abiertas (programadas o confirmadas por el hospital) de los próximos <see cref="ExternalFamilyRules.WindowDays"/> días verían las familias: las de nivel «visible» y las de
/// «sin especialidad». Las ocultas no cuentan, ni las de residentes restringidos, de baja o suspendidos.</summary>
public sealed record ExternalFamilyCounts(int Visible, int WithoutSpecialty)
{
    public int Total => Visible + WithoutSpecialty;
}

/// <summary>El interruptor del centro: si las familias ven las citas externas, su número de cambio (la versión vista, 0 si nunca se tocó) y cuántas citas verían si se encendiera (o se ven ahora).</summary>
public sealed record ExternalFamilyCenterState(bool Enabled, int Number, ExternalFamilyCounts Count);

/// <summary>El interruptor de un residente: si Administración o Enfermería restringieron a su familia, su número de cambio, si el centro tiene encendido el interruptor general y cuántas citas
/// de esta persona abrirían a la familia si se quitara la restricción (o se ven ahora).</summary>
public sealed record ExternalFamilyResidentState(string ResidentName, bool Restricted, int Number, bool CenterEnabled, ExternalFamilyCounts Count);

/// <summary>
/// Los dos interruptores de las citas externas para la familia (script 0104). Ambos se cambian insertando una fila nueva (historial de solo añadir) en una transacción que vuelve a comprobar el
/// ámbito y deja la auditoría con solo identificadores; <c>expectedNumber</c> es el número que vio quien cambia, y si otra persona cambió antes, EXTERNAL_APPOINTMENT_REVISION_CONFLICT. Lo que el
/// actor no ve, no existe para él (acceso denegado).
/// </summary>
public interface IExternalAppointmentFamilySettings
{
    /// <summary>Solo Administración (acceso denegado para el resto).</summary>
    Task<ExternalFamilyCenterState> ReadCenterAsync(ExternalAppointmentActor actor, DateOnly todayAtCenter, CancellationToken ct = default);

    /// <summary>Solo Administración. Encender exige las tres confirmaciones; apagar no. Devuelve el número nuevo. Encender un centro ya encendido o apagar uno ya apagado: conflicto de versión.</summary>
    Task<int> SetCenterAsync(ExternalAppointmentActor actor, int expectedNumber, bool enable, ExternalFamilyConfirmations confirmations, CancellationToken ct = default);

    /// <summary>Administración (cualquier residente del centro), Enfermería y Medicina (residentes de sus unidades). Un residente fuera del ámbito del actor o que no está activo: acceso denegado.</summary>
    Task<ExternalFamilyResidentState> ReadResidentAsync(ExternalAppointmentActor actor, ResidentId residentId, DateOnly todayAtCenter, CancellationToken ct = default);

    /// <summary>Administración y Enfermería de la unidad del residente, esta sin el permiso de gestionar citas (es una herramienta de privacidad). Medicina, acceso denegado. Devuelve el número nuevo.</summary>
    Task<int> SetResidentAsync(ExternalAppointmentActor actor, ResidentId residentId, int expectedNumber, bool restrict, CancellationToken ct = default);
}

/// <summary>
/// Una cita externa tal como la ve la familia (PR 5; DPO, apartado 10.2): el día y la hora (o «por confirmar»), el tipo general, la especialidad solo si la cita es de nivel «visible», el lugar,
/// qué preparar, quién lleva y acompaña y si está resuelto, y si está cancelada. <b>No tiene</b> la nota «al terminar», el motivo de cancelar, el nombre de quien se encarga, el historial ni quién la
/// registró: el servidor no los devuelve. Una prueba por reflexión fija este conjunto de propiedades.
/// </summary>
public sealed record ExternalAppointmentForFamily(
    Guid Id, DateOnly Date, TimeOnly? Time, ExternalAppointmentType Type, ExternalAppointmentSpecialty? Specialty, string? Place, ExternalPreparation Preparation,
    ExternalTransportMode TransportMode, ExternalArrangementState TransportState, ExternalCompanionMode CompanionMode, ExternalArrangementState CompanionState, bool Cancelled)
{
    /// <summary>Transporte y acompañante resueltos («no necesita» queda resuelto solo).</summary>
    public bool Covered => TransportState == ExternalArrangementState.Confirmado && CompanionState == ExternalArrangementState.Confirmado;
}

/// <summary>Las citas externas de un residente para su familia, en orden de fecha y hora (sin hora, al final del día), y el residente al que pertenecen.</summary>
public sealed record FamilyExternalAppointments(FamilyPortalResident Resident, DateOnly Today, IReadOnlyList<ExternalAppointmentForFamily> Appointments);

/// <summary>
/// Lectura de las citas externas por la familia (PR 5, solo ver). Toda operación empieza por el criterio de acceso del portal, <c>AccessibleLinks</c> (vínculo, autorización activa y vigente, residente activo y no
/// suspendido) y por la regla única de visibilidad: centro encendido, residente no restringido y cita no oculta. Si algo falla, devuelve null, sin distinguir un vínculo ajeno, un interruptor apagado
/// o una cita que no existe. La redacción por nivel se hace en la consulta: lo que no se debe ver no sale de la base.
/// </summary>
public interface IFamilyExternalAppointments
{
    /// <summary>Si el enlace «Citas fuera del centro» debe salir en la pantalla del residente: el vínculo es accesible, el centro está encendido y el residente no está restringido, haya o no citas.</summary>
    Task<bool> IsAvailableAsync(FamilyPortalAccess access, Guid linkId, DateOnly todayAtCenter, CancellationToken ct = default);

    /// <summary>Las citas visibles de hoy a <see cref="ExternalFamilyRules.WindowDays"/> días, o null si el enlace no existe o la familia no puede ver las citas de este residente.</summary>
    Task<FamilyExternalAppointments?> ListAsync(FamilyPortalAccess access, Guid linkId, DateOnly todayAtCenter, CancellationToken ct = default);

    /// <summary>Una cita visible del residente del enlace, o null por cualquier motivo.</summary>
    Task<(FamilyPortalResident Resident, ExternalAppointmentForFamily Appointment)?> FindAsync(
        FamilyPortalAccess access, Guid linkId, Guid appointmentId, DateOnly todayAtCenter, CancellationToken ct = default);
}
