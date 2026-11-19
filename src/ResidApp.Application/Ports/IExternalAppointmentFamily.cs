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

    // ---- PR 6 (script 0105): los avisos por correo y lo que ofrece la familia ----

    /// <summary>Solo Administración. Si los avisos por correo de citas externas están encendidos, su número de cambio y si las familias ven las citas (sin eso no se puede encender).</summary>
    Task<ExternalFamilyMailState> ReadMailAsync(ExternalAppointmentActor actor, CancellationToken ct = default);

    /// <summary>Solo Administración. Encender exige la confirmación de que se informó a las familias y que las familias vean las citas; apagar no pide nada. Devuelve el número nuevo; conflicto de versión si otra persona cambió antes.</summary>
    Task<int> SetMailAsync(ExternalAppointmentActor actor, int expectedNumber, bool enable, bool informed, CancellationToken ct = default);

    /// <summary>Solo Administración. Las personas de la familia de un residente con su estado de avisos por correo (sin la dirección).</summary>
    Task<IReadOnlyList<ExternalFamilyMember>> ListFamilyMembersAsync(ExternalAppointmentActor actor, ResidentId residentId, CancellationToken ct = default);

    /// <summary>Solo Administración. Da de baja (o vuelve a dar de alta) a una persona de los avisos por correo de citas externas. Devuelve el número nuevo.</summary>
    Task<int> SetFamilyMemberMailAsync(ExternalAppointmentActor actor, ResidentId residentId, Guid familyId, int expectedNumber, bool optOut, CancellationToken ct = default);

    /// <summary>Lo que la familia ha ofrecido en una cita que el actor puede ver (Administración, Enfermería y Medicina de la unidad): qué partes tienen a alguien, con su nombre solo para quien lo puede ver, y cuántas personas dijeron «No puedo». Null si no la ve.</summary>
    Task<ExternalFamilyOffers?> ReadOffersAsync(ExternalAppointmentActor actor, Guid appointmentId, DateOnly todayAtCenter, CancellationToken ct = default);

    /// <summary>De las citas indicadas que el actor puede ver, las que tienen alguna parte ofrecida por la familia y sin confirmar (para marcarlas en la lista de «algo sin cubrir»).</summary>
    Task<IReadOnlySet<Guid>> ListOfferedAsync(ExternalAppointmentActor actor, IReadOnlyCollection<Guid> appointmentIds, DateOnly todayAtCenter, CancellationToken ct = default);
}

/// <summary>El interruptor de los avisos por correo del centro: si está encendido, su número de cambio (0 si nunca se tocó) y si las familias ven las citas externas (sin eso no se pueden encender los correos).</summary>
public sealed record ExternalFamilyMailState(bool Enabled, int Number, bool FamilyEnabled);

/// <summary>Una persona de la familia de un residente con el estado de sus avisos por correo de citas externas: si tiene una dirección confirmada, si está dada de baja de los avisos y su número de cambio (0 si nunca se tocó). Nunca la dirección.</summary>
public sealed record ExternalFamilyMember(Guid FamilyId, string Name, string Relationship, bool HasConfirmedAddress, bool OptedOut, int Number);

/// <summary>Una parte de una cita que ha asumido alguien de la familia. Name solo se rellena para quien puede ver el nombre (Administración y Enfermería con permiso de citas): para el resto, null.</summary>
public sealed record ExternalFamilyOffer(ExternalArrangementScope Scope, string? Name);

/// <summary>Lo que la familia ha ofrecido en una cita: los encargos vigentes (sin confirmar por el personal) y cuántas personas han dicho «No puedo». Nunca quién dijo que no ni un total por persona.</summary>
public sealed record ExternalFamilyOffers(IReadOnlyList<ExternalFamilyOffer> Offers, int RefusalCount, bool NamesVisible);

/// <summary>
/// Una cita externa tal como la ve la familia (PR 5; DPO, apartado 10.2): el día y la hora (o «por confirmar»), el tipo general, la especialidad solo si la cita es de nivel «visible», el lugar,
/// qué preparar, quién lleva y acompaña y si está resuelto, y si está cancelada. <b>No tiene</b> la nota «al terminar», el motivo de cancelar, el nombre de quien se encarga, el historial ni quién la
/// registró: el servidor no los devuelve. Una prueba por reflexión fija este conjunto de propiedades.
/// </summary>
public sealed record ExternalAppointmentForFamily(
    Guid Id, DateOnly Date, TimeOnly? Time, ExternalAppointmentType Type, ExternalAppointmentSpecialty? Specialty, string? Place, ExternalPreparation Preparation,
    ExternalTransportMode TransportMode, ExternalArrangementState TransportState, ExternalCompanionMode CompanionMode, ExternalArrangementState CompanionState, bool Cancelled,
    ExternalFamilyHolder TransportHolder = ExternalFamilyHolder.Nobody, ExternalFamilyHolder CompanionHolder = ExternalFamilyHolder.Nobody, bool Refused = false,
    bool TransportLapsed = false, bool CompanionLapsed = false)
{
    /// <summary>Transporte y acompañante resueltos («no necesita» queda resuelto solo): lo que el personal dejó confirmado, sin contar lo que una familiar ofreció.</summary>
    public bool Covered => TransportState == ExternalArrangementState.Confirmado && CompanionState == ExternalArrangementState.Confirmado;

    /// <summary>Cada parte está confirmada por el personal o la ha asumido una persona de la familia (decisión 6 del PR 6: para la familia, un encargo cuenta como cubierto; el personal lo ve «sin confirmar»).</summary>
    public bool CoveredByFamily =>
        (TransportState == ExternalArrangementState.Confirmado || TransportHolder != ExternalFamilyHolder.Nobody)
        && (CompanionState == ExternalArrangementState.Confirmado || CompanionHolder != ExternalFamilyHolder.Nobody);

    /// <summary>La persona que mira se había encargado de esa parte y el encargo ya no cuenta (la cita cambió de fecha o de modo, o se cancelaron cosas): la pantalla se lo dice con un texto fijo.</summary>
    public bool LapsedOf(ExternalArrangementScope scope) => scope == ExternalArrangementScope.Transporte ? TransportLapsed : CompanionLapsed;

    public ExternalFamilyHolder HolderOf(ExternalArrangementScope scope) => scope == ExternalArrangementScope.Transporte ? TransportHolder : CompanionHolder;

    /// <summary>La familia puede ocuparse de esta parte: la cita está abierta, la parte es de la familia y el personal no la ha dejado confirmada.</summary>
    public bool FamilyCanAct(ExternalArrangementScope scope) => !Cancelled && (scope == ExternalArrangementScope.Transporte
        ? TransportMode == ExternalTransportMode.Familia && TransportState == ExternalArrangementState.Pendiente
        : CompanionMode == ExternalCompanionMode.Familiar && CompanionState == ExternalArrangementState.Pendiente);

    public bool CanTake(ExternalArrangementScope scope) => FamilyCanAct(scope) && HolderOf(scope) == ExternalFamilyHolder.Nobody;

    public bool CanRelease(ExternalArrangementScope scope) => FamilyCanAct(scope) && HolderOf(scope) == ExternalFamilyHolder.You;

    /// <summary>«No puedo» tiene sentido mientras quede algo de la familia sin nadie, la persona no lo haya dicho ya y no se haya encargado de nada (si se encargó, lo que hace es soltar: «Ya no puedo»).</summary>
    public bool CanRefuse => !Refused && TransportHolder != ExternalFamilyHolder.You && CompanionHolder != ExternalFamilyHolder.You && (CanTake(ExternalArrangementScope.Transporte) || CanTake(ExternalArrangementScope.Acompanante));
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

    /// <summary>
    /// PR 6 (script 0105): «Me encargo yo», «Ya no puedo», «No puedo» y deshacer «No puedo» sobre una parte de una cita visible. Va en una transacción que vuelve a comprobar el acceso y la regla de
    /// visibilidad, bloquea la cita y escribe la fila de solo añadir; la asignación es atómica (de dos pulsaciones a la vez gana una y la otra recibe TakenByAnother) y repetir lo ya hecho
    /// (la misma persona que ya la tiene) es Done. Deja la auditoría con solo identificadores. NotFound es el mismo 404 de siempre.
    /// </summary>
    Task<FamilyArrangementOutcome> ArrangeAsync(
        FamilyPortalAccess access, Guid linkId, Guid appointmentId, ExternalArrangementScope scope, FamilyArrangementAction action, DateOnly todayAtCenter, CancellationToken ct = default);
}
