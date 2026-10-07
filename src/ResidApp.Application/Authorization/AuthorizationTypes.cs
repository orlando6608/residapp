using System.ComponentModel.DataAnnotations;
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

/// <summary>Finalidades que puede declarar Dirección para leer contenido clínico (CJ, 2026-10-06). Lista cerrada, y la
/// elegida queda en la auditoría de cada acceso. «Revisión de calidad asistencial» no está: CJ la quiere en Coordinación
/// Clínica, que hoy es el mismo perfil que Dirección. La antigua «Supervisión clínica» ya no se ofrece (solo existe en las
/// filas de auditoría anteriores).</summary>
public enum ClinicalDetailAccessPurpose
{
    [Code("CONTINUIDAD_ASISTENCIAL")] [Display(Name = "Revisión de continuidad asistencial")] ContinuidadAsistencial,
    [Code("INCIDENCIA_RECLAMACION")] [Display(Name = "Revisión de una incidencia o reclamación asistencial")] IncidenciaReclamacion,
    [Code("TRAZABILIDAD_DOCUMENTAL")] [Display(Name = "Verificación de trazabilidad documental")] TrazabilidadDocumental,
}

/// <summary>Traduce el literal "BASELINE_CURRENT" | "BASELINE_HISTORY" de ClinicalDetailAuditObligation.</summary>
public enum ClinicalResourceType
{
    [Code("BASELINE_CURRENT")] [Display(Name = "Basal vigente")] BaselineCurrent,
    [Code("BASELINE_HISTORY")] [Display(Name = "Historial de basal")] BaselineHistory,
    // DIR-06 y DIR-07: recursos del residente entero (se audita el residente, no cada fila). Solo los lee Dirección Clínica.
    [Code("RESIDENT_TIMELINE")] [Display(Name = "Línea temporal")] ResidentTimeline,
    [Code("CLOSED_EVENTS_HISTORY")] [Display(Name = "Historial de eventos cerrados")] ClosedEventsHistory,
    // DIR-15: correcciones y rectificaciones de las valoraciones del residente, con los basales firmados como versiones vinculadas.
    [Code("ASSESSMENT_AMENDMENTS")] [Display(Name = "Correcciones y rectificaciones")] AssessmentAmendments,
    // DIR-14: quién hizo qué sobre el residente (la auditoría de sus acciones clínicas), con el mismo requisito de finalidad y justificación.
    [Code("CLINICAL_TRACEABILITY")] [Display(Name = "Trazabilidad clínica")] ClinicalTraceability,
    // DIR-12: los informes de derivación firmados del residente (lista); cada PDF se descarga después con su propia auditoría.
    [Code("REFERRAL_REPORTS")] [Display(Name = "Informes de derivación firmados")] ReferralReports,
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
    ResidentTimelineRead,
    ClosedEventsHistoryRead,
    AssessmentAmendmentsRead,
    ClinicalTraceabilityRead,
    ReferralReportsRead,
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
