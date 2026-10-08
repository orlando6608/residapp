using ResidApp.Shared;

namespace ResidApp.Domain.Accounts;

/// <summary>
/// ADM-13 (historia 4, 0024): los permisos configurables que Administración puede conceder a cada perfil, los que usa hoy
/// la aplicación. Auxiliar, Administración y Familiar no tienen. BASELINE_DRAFT_CONTRIBUTE no se ofrece: no lo usa nada y
/// su granularidad está pendiente (matriz §12). La BD impide el resto de combinaciones (TR_pp_profile_catalog,
/// TR_pp_reference_ranges_profile).
/// </summary>
public static class ProfilePermissions
{
    public const string ResidentIdentityCreate = "RESIDENT_IDENTITY_CREATE";
    public const string BaselineInitialComplete = "BASELINE_INITIAL_COMPLETE";
    public const string BaselineReevaluate = "BASELINE_REEVALUATE";
    public const string ClinicalDetailRead = "CLINICAL_DETAIL_READ";
    public const string ReferenceRangesManage = "REFERENCE_RANGES_MANAGE";
    public const string ProcessDeadlinesManage = "PROCESS_DEADLINES_MANAGE";

    public static IReadOnlyList<string> For(SystemProfile profile) => profile switch
    {
        SystemProfile.Enfermeria => [ResidentIdentityCreate, BaselineInitialComplete, BaselineReevaluate],
        SystemProfile.Medicina => [BaselineInitialComplete, BaselineReevaluate],
        SystemProfile.DireccionClinica => [ClinicalDetailRead, ReferenceRangesManage, ProcessDeadlinesManage],
        _ => [],
    };
}
