using ResidApp.Domain.Baseline;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.Authorization;

/// <summary>Traduce RESIDENT_BASELINE_PERMISSIONS de lib/authorization/policy.ts.</summary>
public enum ResidentBaselinePermission
{
    [Code("RESIDENT_IDENTITY_CREATE")] ResidentIdentityCreate,
    [Code("BASELINE_INITIAL_COMPLETE")] BaselineInitialComplete,
    [Code("BASELINE_REEVALUATE")] BaselineReevaluate,
    [Code("CLINICAL_DETAIL_READ")] ClinicalDetailRead,
}

/// <summary>Traduce CLINICAL_DETAIL_ACCESS_PURPOSES de policy.ts.</summary>
public enum ClinicalDetailAccessPurpose
{
    [Code("SUPERVISION_CLINICA")] SupervisionClinica,
}

/// <summary>Traduce el literal "BASELINE_CURRENT" | "BASELINE_HISTORY" de ClinicalDetailAuditObligation.</summary>
public enum ClinicalResourceType
{
    [Code("BASELINE_CURRENT")] BaselineCurrent,
    [Code("BASELINE_HISTORY")] BaselineHistory,
}

/// <summary>Traduce AuthorizationDenialReason de policy.ts.</summary>
public enum AuthorizationDenialReason
{
    DenyByDefault,
    NotAuthenticated,
    AccountIdRequired,
    AccountInactive,
    ActiveProfileRequired,
    ProfileNotAssigned,
    CenterOutOfScope,
    UnitOutOfScope,
    ResidentOutOfScope,
    FamilyAuthorizationRequired,
    PermissionRequired,
    AccessPurposeRequired,
    ActionNotAllowed,
}

/// <summary>Traduce ClinicalDetailAuditObligation de policy.ts: la obligación de auditar que acompaña a
/// una lectura clínica autorizada de Dirección Clínica.</summary>
public sealed record ClinicalDetailAuditObligation(
    ClinicalResourceType ResourceType, AccountId AccountId, CenterId CenterId, UnitId UnitId,
    ResidentId ResidentId, ClinicalDetailAccessPurpose Purpose);

/// <summary>
/// Traduce AuthorizationDecision de policy.ts. TS distingue en tiempo de compilación allowed:true/false;
/// aquí se modela como jerarquía sellada equivalente.
/// </summary>
public abstract record AuthorizationDecision
{
    public sealed record Allowed(IReadOnlyList<ClinicalDetailAuditObligation> Obligations) : AuthorizationDecision;
    public sealed record Denied(AuthorizationDenialReason Reason) : AuthorizationDecision;

    public static AuthorizationDecision Allow(params ReadOnlySpan<ClinicalDetailAuditObligation> obligations) =>
        new Allowed(obligations.ToArray());

    public static AuthorizationDecision Deny(AuthorizationDenialReason reason) => new Denied(reason);
}

/// <summary>Traduce ResidentBaselineProfileScope de policy.ts: un grant relacional para un único perfil,
/// centro y unidad.</summary>
public sealed record ResidentBaselineProfileScope(
    SystemProfile Profile, CenterId CenterId, UnitId UnitId,
    IReadOnlyList<ResidentId> ResidentIds, IReadOnlyList<ResidentId> ActiveFamilyAuthorizationResidentIds,
    IReadOnlyList<ResidentBaselinePermission> Permissions);

/// <summary>
/// Traduce AuthorizationSubject de policy.ts: instantánea que el servidor debe resolver desde sesión y
/// repositorio en cada petición. Nunca debe aceptarse este objeto desde un formulario o payload cliente.
/// </summary>
public sealed record AuthorizationSubject(
    AccountId? AccountId, bool Authenticated, bool AccountActive, SystemProfile? ActiveProfile,
    IReadOnlyList<SystemProfile> AssignedProfiles, IReadOnlyList<ResidentBaselineProfileScope> ProfileScopes);

/// <summary>Traduce el literal de 7 acciones de RESIDENT_BASELINE_ACTIONS en policy.ts.</summary>
public enum ResidentBaselineAction
{
    ResidentIdentityCreate,
    ResidentIdentityRead,
    ResidentIdentityUpdate,
    BaselineCurrentRead,
    BaselineInitialComplete,
    BaselineReevaluate,
    BaselineHistoryRead,
}

/// <summary>
/// Traduce ResidentBaselineAuthorizationRequest de policy.ts. En TS es una unión discriminada con
/// validación estructural en runtime porque `authorizeResidentBaseline` acepta `unknown`; aquí el
/// llamador siempre es RequestAuthorizationContext (código propio, no payload externo), así que el
/// sistema de tipos de C# ya impide construir una forma inválida — no se traduce la validación de forma
/// (isAuthorizationRequest/isAuthorizationSubject/isProfileScope), solo la lógica de negocio.
/// </summary>
public sealed record ResidentBaselineAuthorizationRequest(
    ResidentBaselineAction Action, AuthorizationSubject Subject, CenterId CenterId, UnitId UnitId,
    ResidentId? ResidentId, ClinicalDetailAccessPurpose? Purpose = null)
{
    /// <summary>Traduce la comprobación TS `"residentId" in resource`.</summary>
    public bool RequiresResident => Action != ResidentBaselineAction.ResidentIdentityCreate;
}
