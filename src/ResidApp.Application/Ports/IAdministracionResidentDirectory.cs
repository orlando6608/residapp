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
/// son compartidos); no dice cuáles. PortalAccount es el usuario con el que entra al Portal Familiar (script 0052), null si aún no tiene acceso.</summary>
public sealed record ResidentFamilyMember(
    Guid LinkId, string DisplayName, string Relationship, string Phone, string? Email,
    IReadOnlyList<FamilyAuthorizationChangeEntry> AuthorizationChanges, int OtherResidentLinks = 0, bool IsReferent = false,
    bool IsLegalGuardian = false, string? PortalAccount = null)
{
    public FamilyAuthorizationChangeEntry? CurrentAuthorization => AuthorizationChanges.Count == 0 ? null : AuthorizationChanges[^1];
}

/// <summary>Un familiar que dejó de estar vinculado al residente (CJ, 2026-10-07): el vínculo pasado se sigue viendo en el historial.</summary>
public sealed record FormerFamilyMember(
    string DisplayName, string Relationship, DateTimeOffset LinkedAt, DateTimeOffset UnlinkedAt, string Reason);

/// <summary>Un familiar que se puede vincular a otro residente (CJ, 2026-10-10): cualquier familiar del centro que todavía no esté vinculado al residente
/// de la pantalla, buscado por nombre o teléfono. Nombre y teléfono bastan para distinguir a dos personas con el mismo nombre; ActiveLinks es a
/// cuántos residentes está vinculado ya, sin decir a cuáles (quien vincula no descubre qué residentes tiene cada familiar fuera de su ámbito).</summary>
public sealed record LinkableFamilyMember(Guid FamilyId, string DisplayName, string Phone, int ActiveLinks);

/// <summary>La búsqueda de familiares vinculables: no se lista el centro entero. Hace falta un mínimo de letras (o de cifras del teléfono) y
/// salen como mucho MaxResults.</summary>
public static class LinkableFamilySearch
{
    public const int MinLength = 3;

    public const int MaxResults = 30;

    /// <summary>El texto sin espacios sobrantes, o null si no llega al mínimo (no se busca).</summary>
    public static string? Normalize(string? search)
    {
        var term = search?.Trim();
        return term is { Length: >= MinLength } ? term : null;
    }

    /// <summary>Las cifras del texto, si hay al menos el mínimo: se busca también por teléfono, que se guarda con espacios, guiones o prefijo.</summary>
    public static string? PhoneDigits(string term)
    {
        var digits = new string(term.Where(char.IsAsciiDigit).ToArray());
        return digits.Length >= MinLength ? digits : null;
    }
}

/// <summary>ADM-08 (0022, 0044): una designación de contacto urgente (Action: ver EmergencyContactSet). LinkId null es «sin contacto urgente»; DisplayName es el
/// nombre vigente del familiar.</summary>
public sealed record EmergencyContactDesignation(int Number, string Action, Guid? LinkId, string? DisplayName, DateTimeOffset At, string? Reason = null);

/// <summary>ADM-03: la ficha administrativa. Corrections y EmergencyContacts van de la más antigua a la más reciente;
/// Family, por nombre.</summary>
public sealed record AdministrativeResidentDetail(
    AdministrativeResidentSummary Resident, IReadOnlyList<ResidentLocationInterval> Locations,
    IReadOnlyList<ResidentIdentityCorrectionEntry> Corrections, IReadOnlyList<ResidentFamilyMember> Family,
    IReadOnlyList<EmergencyContactDesignation> EmergencyContacts, IReadOnlyList<FormerFamilyMember>? FormerFamily = null)
{
    /// <summary>Los familiares que ya no están vinculados, del desvínculo más reciente al más antiguo.</summary>
    public IReadOnlyList<FormerFamilyMember> Former => FormerFamily ?? [];

    /// <summary>Los vínculos de los contactos urgentes vigentes, en el orden en que se designaron.</summary>
    public IReadOnlyList<Guid> CurrentEmergencyContacts => EmergencyContactSet.Current(EmergencyContacts.Select(d => (d.Action, d.LinkId)));

    /// <summary>El motivo de que no tenga contacto urgente (0050), si la última designación es «sin contacto».</summary>
    public string? NoContactReason => EmergencyContacts is [.., { Action: EmergencyContactSet.NoContact } last] ? last.Reason : null;
}

/// <summary>Por qué un residente del ámbito requiere atención (inicio de Administración, ADM-01): las mismas cuatro cosas que cuenta el inicio, ahora por residente.</summary>
public enum ResidentAttentionKind
{
    SinHabitacion,
    AutorizacionPendiente,
    AutorizacionCaducada,
    AutorizacionPorCaducar,
}

/// <summary>Los motivos de atención de un residente: sin habitación, o algún familiar con la autorización pendiente, caducada o que caduca en <see cref="AdministrationOverviewRules.ExpiringSoonDays"/> días.</summary>
public sealed record ResidentAttention(
    ResidentId ResidentId, bool WithoutRoom, bool AuthorizationPending, bool AuthorizationExpired, bool AuthorizationExpiringSoon)
{
    public bool Matches(ResidentAttentionKind kind) => kind switch
    {
        ResidentAttentionKind.SinHabitacion => WithoutRoom,
        ResidentAttentionKind.AutorizacionPendiente => AuthorizationPending,
        ResidentAttentionKind.AutorizacionCaducada => AuthorizationExpired,
        ResidentAttentionKind.AutorizacionPorCaducar => AuthorizationExpiringSoon,
        _ => false,
    };
}

/// <summary>ADM-02/ADM-03: lectura de Administración. Aplica en la propia consulta la regla de ámbito de
/// SqlEnfermeriaResidentDirectory (ámbito activo de ADMINISTRACION, sus unidades y, si los restringe, sus residentes),
/// así que un residente ajeno devuelve null, sin distinguir si existe.</summary>
public interface IAdministracionResidentDirectory
{
    Task<IReadOnlyList<AdministrativeResidentSummary>> ListAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default);

    /// <summary>Qué residentes del ámbito requieren atención (sin habitación, autorización pendiente, caducada o a punto de caducar), con la misma regla que las cifras
    /// del inicio de Administración. <paramref name="today"/> es el día natural del centro.</summary>
    Task<IReadOnlyList<ResidentAttention>> ListAttentionAsync(Guid profileScopeId, CenterId centerId, DateOnly today, CancellationToken ct = default);

    Task<AdministrativeResidentDetail?> FindAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default);

    /// <summary>Los familiares del centro vinculables al residente (ver LinkableFamilyMember) cuyo nombre o teléfono contiene el texto, por nombre.
    /// El texto ya viene normalizado (LinkableFamilySearch.Normalize). El residente ya está comprobado en el ámbito.</summary>
    Task<IReadOnlyList<LinkableFamilyMember>> ListLinkableFamilyAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, string search, CancellationToken ct = default);
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

    /// <summary>Deja como contactos urgentes exactamente a linkIds (vacío: ninguno, con su motivo noContactReason: CJ, 2026-10-10) si el
    /// residente sigue teniendo expectedDesignations designaciones (si no, conflicto) y cambia algo. Devuelve cuántas tiene ya (una por cada
    /// familiar añadido o quitado, o una si se queda sin contacto). Dejarlo sin contacto sin motivo es EMERGENCY_CONTACT_INVALID.</summary>
    Task<int> DesignateEmergencyContactAsync(
        AdministrativeResidentTarget target, IReadOnlyList<Guid> linkIds, int expectedDesignations, string? noContactReason = null,
        CancellationToken ct = default);

    /// <summary>Desvincula al familiar del residente con un motivo (CJ, 2026-10-07): el vínculo se conserva como pasado y el familiar sigue con
    /// los demás residentes. Si su autorización seguía abierta se revoca, y si era uno de los contactos urgentes se quita de ellos; si era el
    /// único, el residente se queda sin contacto urgente (CJ, 2026-10-10) y el motivo de la desvinculación consta también como motivo de ello.
    /// Un vínculo que ya está desvinculado: FAMILY_MEMBER_CONFLICT.</summary>
    Task UnlinkAsync(AdministrativeResidentTarget target, Guid linkId, string reason, CancellationToken ct = default);

    /// <summary>Da al familiar del vínculo acceso al Portal Familiar (script 0052): crea su cuenta con el usuario subject, con un ámbito FAMILIAR en
    /// el centro y sin unidades, y la enlaza al familiar. No toca la autorización, que se abre y se activa aparte. Si el familiar ya tiene cuenta,
    /// FAMILY_PORTAL_ACCESS_CONFLICT; si el usuario ya existe, ACCOUNT_SUBJECT_TAKEN; un vínculo que no es del residente, acceso denegado.</summary>
    Task GrantPortalAccessAsync(AdministrativeResidentTarget target, Guid linkId, string subject, CancellationToken ct = default);
}

/// <summary>Traslado del residente (script 0040). ExpectedUnitId es la unidad en la que estaba al abrir el formulario; OperationId
/// es el identificador del traslado, así que un reenvío no lo repite. Sin habitación ni plaza, queda sin ellas.</summary>
public sealed record TransferResidentInput(
    AdministrativeResidentTarget Target, Guid OperationId, UnitId ExpectedUnitId, UnitId DestinationUnitId, Guid? RoomId, Guid? PlaceId);

/// <summary>Qué hizo el traslado: cuántos eventos abiertos pasaron a la unidad de destino y si se canceló un borrador de basal.</summary>
public sealed record TransferResidentResult(int OpenEventsMoved, bool BaselineDraftCancelled);

/// <summary>
/// Traslado del residente a otra unidad del centro, o a otra habitación o plaza de la misma. En una transacción cierra la ubicación
/// vigente y abre la nueva, pasa a la unidad de destino los eventos no cerrados, cancela el borrador de basal si cambia de unidad y
/// deja RESIDENT_TRANSFER en la auditoría. Si el residente ya no está en ExpectedUnitId, RESIDENT_TRANSFER_CONFLICT; si no cambia
/// nada, RESIDENT_TRANSFER_INVALID.
/// </summary>
public interface IResidentTransferRepository
{
    Task<TransferResidentResult> TransferAsync(TransferResidentInput input, CancellationToken ct = default);
}
