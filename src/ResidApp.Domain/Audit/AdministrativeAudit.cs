namespace ResidApp.Domain.Audit;

/// <summary>ADM-28: agrupación de las acciones administrativas en la pantalla de auditoría.</summary>
public enum AuditCategory
{
    Cuentas,
    PerfilesYPermisos,
    Estructura,
    ResidentesYFamilias,
    TurnosYEquipos,
}

/// <summary>
/// ADM-28 / AUD-01 a AUD-03: las únicas acciones de dbo.eventos_auditoria que ve Administración, por su código y sea cual sea el
/// perfil que las hizo (el alta de un residente también la hace Enfermería con permiso). Es una lista cerrada: una acción nueva
/// no sale hasta que se añada aquí, con su etiqueta. Quedan fuera las acciones clínicas, CLINICAL_DETAIL_READ (la lectura de
/// Dirección lleva contexto clínico) y REFERENCE_RANGES_UPDATE (un umbral clínico que Administración no toca, ADM-29).
/// </summary>
public static class AdministrativeAudit
{
    public static readonly IReadOnlyList<(string Code, AuditCategory Category)> Actions =
    [
        ("ACCOUNT_CREATE", AuditCategory.Cuentas),
        ("ACCOUNT_RENAME", AuditCategory.Cuentas),
        ("ACCOUNT_SUSPEND", AuditCategory.Cuentas),
        ("ACCOUNT_ACTIVATE", AuditCategory.Cuentas),
        ("PROFILE_SCOPE_GRANT", AuditCategory.PerfilesYPermisos),
        ("PROFILE_SCOPE_REVOKE", AuditCategory.PerfilesYPermisos),
        ("PROFILE_UNIT_GRANT", AuditCategory.PerfilesYPermisos),
        ("PROFILE_UNIT_REVOKE", AuditCategory.PerfilesYPermisos),
        ("PROFILE_RESIDENT_GRANT", AuditCategory.PerfilesYPermisos),
        ("PROFILE_RESIDENT_REVOKE", AuditCategory.PerfilesYPermisos),
        ("PROFILE_PERMISSION_GRANT", AuditCategory.PerfilesYPermisos),
        ("PROFILE_PERMISSION_REVOKE", AuditCategory.PerfilesYPermisos),
        ("CENTER_CREATE", AuditCategory.Estructura),
        ("UNIT_CREATE", AuditCategory.Estructura),
        ("UNIT_RENAME", AuditCategory.Estructura),
        ("UNIT_ACTIVATE", AuditCategory.Estructura),
        ("UNIT_DEACTIVATE", AuditCategory.Estructura),
        ("UNIT_LOCATE", AuditCategory.Estructura),
        ("BUILDING_CREATE", AuditCategory.Estructura),
        ("BUILDING_RENAME", AuditCategory.Estructura),
        ("BUILDING_ACTIVATE", AuditCategory.Estructura),
        ("BUILDING_DEACTIVATE", AuditCategory.Estructura),
        ("FLOOR_CREATE", AuditCategory.Estructura),
        ("FLOOR_RENAME", AuditCategory.Estructura),
        ("FLOOR_ACTIVATE", AuditCategory.Estructura),
        ("FLOOR_DEACTIVATE", AuditCategory.Estructura),
        ("RESIDENT_CREATE", AuditCategory.ResidentesYFamilias),
        ("RESIDENT_IDENTITY_CORRECT", AuditCategory.ResidentesYFamilias),
        ("FAMILY_MEMBER_CREATE", AuditCategory.ResidentesYFamilias),
        ("FAMILY_MEMBER_UPDATE", AuditCategory.ResidentesYFamilias),
        ("FAMILY_AUTHORIZATION_CHANGE", AuditCategory.ResidentesYFamilias),
        ("EMERGENCY_CONTACT_DESIGNATE", AuditCategory.ResidentesYFamilias),
        ("SHIFT_CREATE", AuditCategory.TurnosYEquipos),
        ("SHIFT_RENAME", AuditCategory.TurnosYEquipos),
        ("SHIFT_ACTIVATE", AuditCategory.TurnosYEquipos),
        ("SHIFT_DEACTIVATE", AuditCategory.TurnosYEquipos),
        ("TEAM_CREATE", AuditCategory.TurnosYEquipos),
        ("TEAM_RENAME", AuditCategory.TurnosYEquipos),
        ("TEAM_ACTIVATE", AuditCategory.TurnosYEquipos),
        ("TEAM_DEACTIVATE", AuditCategory.TurnosYEquipos),
        ("TEAM_MEMBER_ADD", AuditCategory.TurnosYEquipos),
        ("TEAM_MEMBER_REMOVE", AuditCategory.TurnosYEquipos),
        ("SCHEDULE_CREATE", AuditCategory.TurnosYEquipos),
        ("SCHEDULE_RETIRE", AuditCategory.TurnosYEquipos),
        ("SCHEDULE_SERIES_RETIRE", AuditCategory.TurnosYEquipos),
    ];

    public static bool IsAdministrative(string? code) => code is not null && Actions.Any(a => a.Code == code);
}
