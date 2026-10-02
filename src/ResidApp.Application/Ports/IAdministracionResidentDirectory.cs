using ResidApp.Domain.Families;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>ADM-02: un residente del ámbito de Administración. Solo identidad administrativa, unidad y fecha de alta:
/// nada de basal, Barthel ni contenido clínico (historia 1).</summary>
public sealed record AdministrativeResidentSummary(
    ResidentId ResidentId, string DisplayName, DateOnly BirthDate, DocumentedSexCode DocumentedSex, UnitId UnitId,
    string UnitName, DateTimeOffset AdmittedAt);

/// <summary>RES-04: un intervalo del historial de ubicación. Until es null en el vigente.</summary>
/// <summary>RoomName y PlaceName son la habitación y la plaza de la ubicación (historia 2, script 0029), si las tiene.</summary>
public sealed record ResidentLocationInterval(
    string UnitName, DateTimeOffset From, DateTimeOffset? Until, SystemProfile RecordedBy, string? RoomName = null, string? PlaceName = null);

/// <summary>ADM-03 (0021): una corrección de identidad, con los valores anteriores y los nuevos.</summary>
public sealed record ResidentIdentityCorrectionEntry(
    int Number, ResidentIdentity Before, ResidentIdentity After, string Reason, DateTimeOffset CorrectedAt);

/// <summary>ADM-10 (0022): un cambio de la autorización de un familiar, tal como se guardó (nunca Caducada).</summary>
public sealed record FamilyAuthorizationChangeEntry(
    int Number, FamilyAuthorizationStatus Status, DateOnly? ValidUntil, string? Reason, DateTimeOffset At);

/// <summary>ADM-08 (0022): un familiar vinculado al residente. LinkId identifica el vínculo; AuthorizationChanges va del
/// primero al último y está vacía si la autorización no se ha abierto.</summary>
/// <summary>OtherResidentLinks es a cuántos otros residentes del centro está vinculado también el familiar (sus datos de contacto
/// son compartidos); no dice cuáles.</summary>
public sealed record ResidentFamilyMember(
    Guid LinkId, string DisplayName, string Relationship, string Phone, string? Email,
    IReadOnlyList<FamilyAuthorizationChangeEntry> AuthorizationChanges, int OtherResidentLinks = 0)
{
    public FamilyAuthorizationChangeEntry? CurrentAuthorization => AuthorizationChanges.Count == 0 ? null : AuthorizationChanges[^1];
}

/// <summary>Un familiar que se puede vincular a otro residente: ya está vinculado a algún residente del ámbito de quien gestiona
/// y todavía no al residente de la pantalla. Nombre y teléfono bastan para distinguir a dos personas con el mismo nombre.</summary>
public sealed record LinkableFamilyMember(Guid FamilyId, string DisplayName, string Phone);

/// <summary>ADM-08 (0022): una designación de contacto urgente. LinkId null es «sin contacto urgente»; DisplayName es el
/// nombre vigente del familiar.</summary>
public sealed record EmergencyContactDesignation(int Number, Guid? LinkId, string? DisplayName, DateTimeOffset At);

/// <summary>ADM-03: la ficha administrativa. Corrections y EmergencyContacts van de la más antigua a la más reciente;
/// Family, por nombre.</summary>
public sealed record AdministrativeResidentDetail(
    AdministrativeResidentSummary Resident, IReadOnlyList<ResidentLocationInterval> Locations,
    IReadOnlyList<ResidentIdentityCorrectionEntry> Corrections, IReadOnlyList<ResidentFamilyMember> Family,
    IReadOnlyList<EmergencyContactDesignation> EmergencyContacts)
{
    /// <summary>El vínculo del contacto urgente vigente, o null si no hay.</summary>
    public Guid? CurrentEmergencyContact => EmergencyContacts.Count == 0 ? null : EmergencyContacts[^1].LinkId;
}

/// <summary>ADM-02/ADM-03: lectura de Administración. Aplica en la propia consulta la regla de ámbito de
/// SqlEnfermeriaResidentDirectory (ámbito activo de ADMINISTRACION, sus unidades y, si los restringe, sus residentes),
/// así que un residente ajeno devuelve null, sin distinguir si existe.</summary>
public interface IAdministracionResidentDirectory
{
    Task<IReadOnlyList<AdministrativeResidentSummary>> ListAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default);

    Task<AdministrativeResidentDetail?> FindAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default);

    /// <summary>Los familiares vinculables al residente (ver LinkableFamilyMember), por nombre. El residente ya está comprobado en el ámbito.</summary>
    Task<IReadOnlyList<LinkableFamilyMember>> ListLinkableFamilyAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default);
}

public sealed record CorrectResidentIdentityInput(
    AccountId AccountId, CenterId CenterId, UnitId UnitId, ResidentId ResidentId, ResidentIdentity Identity, string Reason,
    int ExpectedCorrections);

/// <summary>ADM-03 (0021): guarda una corrección de identidad y devuelve cuántas tiene ya el residente.</summary>
public interface IResidentIdentityRepository
{
    Task<int> CorrectAsync(CorrectResidentIdentityInput input, CancellationToken ct = default);
}

/// <summary>Un residente sobre el que Administración ya está autorizada (RequestAuthorizationContextResolver
/// .RequireResidentAdministration), con la cuenta que firma el cambio.</summary>
public sealed record AdministrativeResidentTarget(AccountId AccountId, CenterId CenterId, UnitId UnitId, ResidentId ResidentId);

/// <summary>
/// ADM-08 a ADM-11 (0022): familiares, autorizaciones y contacto urgente de un residente. Cada escritura va en una
/// transacción con su auditoría. Un vínculo que no es del residente da acceso denegado, sin distinguir si existe.
/// </summary>
public interface IResidentFamilyRepository
{
    /// <summary>Crea el familiar con el identificador operationId y lo vincula al residente, sin autorización. Un reenvío
    /// con el mismo operationId no duplica nada: devuelve el vínculo ya creado.</summary>
    Task<Guid> AddAsync(AdministrativeResidentTarget target, Guid operationId, FamilyMemberData data, CancellationToken ct = default);

    /// <summary>Vincula al residente un familiar que ya existe y que es vinculable (ver LinkableFamilyMember; si no, acceso denegado,
    /// sin distinguir el motivo), con la relación propia de este residente y sin abrir su autorización. operationId es el identificador del
    /// vínculo: un reenvío no lo duplica. Si ya estaba vinculado al residente, FAMILY_MEMBER_CONFLICT. Devuelve el vínculo.</summary>
    Task<Guid> LinkExistingAsync(
        AdministrativeResidentTarget target, Guid profileScopeId, Guid operationId, Guid familyId, string relationship,
        CancellationToken ct = default);

    /// <summary>expectedVersion es FamilyMemberData.Version de los datos que vio quien edita; si ya no coincide con los actuales,
    /// FAMILY_MEMBER_CONFLICT.</summary>
    Task UpdateAsync(
        AdministrativeResidentTarget target, Guid linkId, FamilyMemberData data, string expectedVersion, CancellationToken ct = default);

    /// <summary>Registra el cambio si el vínculo sigue teniendo expectedChanges cambios (si no, conflicto) y es válido desde
    /// su estado efectivo de hoy. Devuelve cuántos cambios tiene ya.</summary>
    Task<int> ChangeAuthorizationAsync(
        AdministrativeResidentTarget target, Guid linkId, FamilyAuthorizationChange change, DateOnly? validUntil, string? reason,
        int expectedChanges, DateOnly today, CancellationToken ct = default);

    /// <summary>Designa el contacto urgente (linkId null lo quita) si el residente sigue teniendo expectedDesignations
    /// designaciones (si no, conflicto) y cambia algo. Devuelve cuántas tiene ya.</summary>
    Task<int> DesignateEmergencyContactAsync(
        AdministrativeResidentTarget target, Guid? linkId, int expectedDesignations, CancellationToken ct = default);
}
