using ResidApp.Domain.Accounts;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>ADM-12/ADM-13: quién gestiona las cuentas. ProfileScopeId es el ámbito activo de Administración de AccountId
/// en CenterId; el repositorio lo vuelve a comprobar dentro de cada transacción.</summary>
public sealed record AccountAdministrationAccess(Guid ProfileScopeId, AccountId AccountId, CenterId CenterId);

/// <summary>Una concesión de unidad o de residente de un perfil. GrantedBy y RevokedBy son el nombre (o el sujeto
/// externo) de la cuenta que la hizo; RevokedAt es null si está vigente.</summary>
public sealed record AccountScopeGrant(
    Guid TargetId, string Name, DateTimeOffset GrantedAt, string GrantedBy, DateTimeOffset? RevokedAt, string? RevokedBy)
{
    public bool Active => RevokedAt is null;
}

/// <summary>ADM-13: un perfil de la cuenta en el centro, vigente o revocado, con sus unidades, sus residentes (estos,
/// solo en Auxiliar) y sus permisos configurables (0024). TargetId es el id de la unidad, del residente o de la fila del
/// permiso; en los permisos, Name es el código.</summary>
public sealed record AccountProfileScope(
    Guid ProfileScopeId, SystemProfile Profile, DateTimeOffset GrantedAt, string GrantedBy, DateTimeOffset? RevokedAt,
    string? RevokedBy, IReadOnlyList<AccountScopeGrant> Units, IReadOnlyList<AccountScopeGrant> Residents,
    IReadOnlyList<AccountScopeGrant> Permissions)
{
    public bool Active => RevokedAt is null;
}

/// <summary>ADM-12: una cuenta con algún perfil (vigente o revocado) en el centro. DisplayName es null en las cuentas
/// anteriores a 0023 a las que nadie ha puesto nombre. Profiles solo trae los de este centro: los vigentes primero.</summary>
public sealed record ProfessionalAccountSummary(
    AccountId AccountId, string Subject, string? DisplayName, AccountStatus Status, IReadOnlyList<AccountProfileScope> Profiles);

/// <summary>ADM-13: la ficha de una cuenta. HasActiveProfilesElsewhere dice si tiene perfiles vigentes en otro centro
/// (entonces no se puede suspender ni reactivar desde aquí), sin decir cuáles. IsOwnAccount es la cuenta de quien la
/// mira, que no puede cambiarla.</summary>
public sealed record ProfessionalAccountDetail(
    ProfessionalAccountSummary Account, bool HasActiveProfilesElsewhere, bool IsOwnAccount);

/// <summary>Un residente que se puede asignar a un perfil Auxiliar.</summary>
public sealed record AssignableResident(ResidentId ResidentId, string DisplayName, string UnitName);

/// <summary>ADM-12/ADM-13: lectura de las cuentas del centro de quien gestiona. Una cuenta sin perfiles en el centro
/// devuelve null, sin distinguir si existe.</summary>
public interface IProfessionalAccountDirectory
{
    Task<IReadOnlyList<ProfessionalAccountSummary>> ListAsync(AccountAdministrationAccess access, CancellationToken ct = default);

    Task<ProfessionalAccountDetail?> FindAsync(AccountAdministrationAccess access, AccountId accountId, CancellationToken ct = default);

    /// <summary>Los residentes activos que están en una unidad vigente del perfil Auxiliar y en el ámbito de quien
    /// gestiona, y que el perfil todavía no tiene asignados.</summary>
    Task<IReadOnlyList<AssignableResident>> ListAssignableResidentsAsync(
        AccountAdministrationAccess access, Guid profileScopeId, CancellationToken ct = default);
}

/// <summary>
/// ADM-12/ADM-13: escrituras sobre cuentas, perfiles y sus concesiones. Cada una va en una transacción que comprueba de
/// nuevo el ámbito de quien gestiona, bloquea la cuenta afectada y escribe su evento en dbo.eventos_auditoria, sin datos.
/// Las concesiones solo se revocan (triggers de 0002). Las unidades concedidas o revocadas deben estar en el ámbito de
/// quien gestiona; los residentes asignados, también.
/// </summary>
public interface IProfessionalAccountRepository
{
    /// <summary>Crea la cuenta (con OperationId como id) y su primer perfil. Reenviar la misma operación devuelve la
    /// cuenta ya creada.</summary>
    Task<AccountId> CreateAsync(
        AccountAdministrationAccess access, Guid operationId, ProfessionalAccountData data, SystemProfile profile,
        IReadOnlyList<UnitId> units, CancellationToken ct = default);

    Task RenameAsync(AccountAdministrationAccess access, AccountId accountId, string displayName, CancellationToken ct = default);

    Task ChangeStatusAsync(AccountAdministrationAccess access, AccountId accountId, AccountStatus status, CancellationToken ct = default);

    /// <summary>Concede un perfil (con OperationId como id del ámbito). Reenviar la misma operación devuelve el mismo.</summary>
    Task<Guid> GrantProfileAsync(
        AccountAdministrationAccess access, AccountId accountId, Guid operationId, SystemProfile profile,
        IReadOnlyList<UnitId> units, CancellationToken ct = default);

    Task RevokeProfileAsync(AccountAdministrationAccess access, AccountId accountId, Guid profileScopeId, CancellationToken ct = default);

    Task GrantUnitAsync(
        AccountAdministrationAccess access, AccountId accountId, Guid profileScopeId, UnitId unitId, CancellationToken ct = default);

    Task RevokeUnitAsync(
        AccountAdministrationAccess access, AccountId accountId, Guid profileScopeId, UnitId unitId, CancellationToken ct = default);

    Task GrantResidentAsync(
        AccountAdministrationAccess access, AccountId accountId, Guid profileScopeId, ResidentId residentId, CancellationToken ct = default);

    Task RevokeResidentAsync(
        AccountAdministrationAccess access, AccountId accountId, Guid profileScopeId, ResidentId residentId, CancellationToken ct = default);

    /// <summary>0024: un permiso del catálogo del perfil (ProfilePermissions.For).</summary>
    Task GrantPermissionAsync(
        AccountAdministrationAccess access, AccountId accountId, Guid profileScopeId, string permissionCode, CancellationToken ct = default);

    Task RevokePermissionAsync(
        AccountAdministrationAccess access, AccountId accountId, Guid profileScopeId, string permissionCode, CancellationToken ct = default);
}
