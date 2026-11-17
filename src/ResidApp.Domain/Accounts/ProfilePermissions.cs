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

    /// <summary>Enfermería pone, cambia y quita la foto del residente solo con este permiso (CJ, foto 1.3; script 0071).</summary>
    public const string ResidentPhotoManage = "RESIDENT_PHOTO_MANAGE";

    /// <summary>Dirección Clínica abre Indicadores y Calidad de proceso solo con este permiso (CJ, 2.1 de la tercera tanda; script 0076).</summary>
    public const string QualityReview = "QUALITY_REVIEW";

    /// <summary>Enfermería y Medicina cambian la ficha de antecedentes de salud (alergias; script 0094) solo con este permiso; verla no lo necesita.</summary>
    public const string HealthBackgroundEdit = "HEALTH_BACKGROUND_EDIT";

    /// <summary>Enfermería registra y cambia las citas externas de los residentes solo con este permiso (CJ, 3.1 C; script 0101); Administración siempre puede y Medicina solo las ve.
    /// Todavía NO está en <see cref="For"/>: la base y el repositorio ya lo admiten, pero Administración no lo puede conceder desde la pantalla de permisos hasta que existan las pantallas de las
    /// citas externas (PR 2), donde se añade a <see cref="For"/> junto con su ficha de la Guía por perfil (<c>GuiaDerivaTests</c> lo exige: todo permiso concedible lleva una ficha con rutas que existan).</summary>
    public const string ExternalAppointmentManage = "EXTERNAL_APPOINTMENT_MANAGE";

    public static IReadOnlyList<string> For(SystemProfile profile) => profile switch
    {
        SystemProfile.Enfermeria => [ResidentIdentityCreate, BaselineInitialComplete, BaselineReevaluate, ResidentPhotoManage, HealthBackgroundEdit],
        SystemProfile.Medicina => [BaselineInitialComplete, BaselineReevaluate, HealthBackgroundEdit],
        SystemProfile.DireccionClinica => [ClinicalDetailRead, ReferenceRangesManage, ProcessDeadlinesManage, QualityReview],
        _ => [],
    };
}
