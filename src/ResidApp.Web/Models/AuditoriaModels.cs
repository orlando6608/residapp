using ResidApp.Application.Ports;
using ResidApp.Domain.Audit;

namespace ResidApp.Web.Models;

/// <summary>ADM-28: filtros de la auditoría por GET (?desde=&amp;hasta=&amp;accion=&amp;cuenta=). Un valor mal formado se ignora. No hay
/// filtro por quien actuó: la auditoría no es un ranking de trabajadores (AUD-02).</summary>
public sealed class AuditFilter
{
    public DateOnly? Desde { get; set; }

    public DateOnly? Hasta { get; set; }

    public string? Accion { get; set; }

    public Guid? Cuenta { get; set; }

    /// <summary>El periodo con las mismas reglas que los indicadores de Dirección (por defecto 30 días, máximo 366).</summary>
    public IndicatorPeriodFilter Period() => new() { Desde = Desde, Hasta = Hasta };
}

/// <summary>ADM-28: Page es null si el periodo no es válido o falló la lectura. Accounts son las cuentas del centro, para el
/// filtro de cuenta afectada.</summary>
public sealed record AuditViewModel(
    AuditFilter Filter, DateOnly From, DateOnly To, AuditPage? Page, IReadOnlyList<ProfessionalAccountSummary> Accounts);

public static class AuditActionDisplay
{
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["ACCOUNT_CREATE"] = "Cuenta creada",
        ["ACCOUNT_RENAME"] = "Nombre de la cuenta cambiado",
        ["ACCOUNT_SUSPEND"] = "Cuenta suspendida",
        ["ACCOUNT_ACTIVATE"] = "Cuenta reactivada",
        ["PROFILE_SCOPE_GRANT"] = "Perfil concedido",
        ["PROFILE_SCOPE_REVOKE"] = "Perfil revocado",
        ["PROFILE_UNIT_GRANT"] = "Unidad concedida a un perfil",
        ["ADMIN_SCOPE_UNIT_ADD"] = "Unidad añadida al ámbito de una Administración",
        ["ADMIN_PRINCIPAL_SET"] = "Marca de Administración principal cambiada",
        ["PROFILE_UNIT_REVOKE"] = "Unidad revocada a un perfil",
        ["PROFILE_RESIDENT_GRANT"] = "Residente asignado a un perfil",
        ["PROFILE_RESIDENT_REVOKE"] = "Residente retirado de un perfil",
        ["PROFILE_PERMISSION_GRANT"] = "Permiso concedido",
        ["PROFILE_PERMISSION_REVOKE"] = "Permiso revocado",
        ["CENTER_CREATE"] = "Centro creado",
        ["UNIT_CREATE"] = "Unidad creada",
        ["UNIT_RENAME"] = "Unidad renombrada",
        ["UNIT_ACTIVATE"] = "Unidad reactivada",
        ["UNIT_DEACTIVATE"] = "Unidad inactivada",
        ["UNIT_LOCATE"] = "Edificio y planta de una unidad cambiados",
        ["BUILDING_CREATE"] = "Edificio creado",
        ["BUILDING_RENAME"] = "Edificio renombrado",
        ["BUILDING_ACTIVATE"] = "Edificio reactivado",
        ["BUILDING_DEACTIVATE"] = "Edificio inactivado",
        ["FLOOR_CREATE"] = "Planta creada",
        ["FLOOR_RENAME"] = "Planta renombrada",
        ["FLOOR_ACTIVATE"] = "Planta reactivada",
        ["FLOOR_DEACTIVATE"] = "Planta inactivada",
        ["ROOM_CREATE"] = "Habitación creada",
        ["ROOM_RENAME"] = "Habitación renombrada",
        ["ROOM_ACTIVATE"] = "Habitación reactivada",
        ["ROOM_DEACTIVATE"] = "Habitación inactivada",
        ["PLACE_CREATE"] = "Plaza creada",
        ["PLACE_RENAME"] = "Plaza renombrada",
        ["PLACE_ACTIVATE"] = "Plaza reactivada",
        ["PLACE_DEACTIVATE"] = "Plaza inactivada",
        ["RESIDENT_CREATE"] = "Residente dado de alta",
        ["RESIDENT_IDENTITY_CORRECT"] = "Identidad del residente corregida",
        ["RESIDENT_TRANSFER"] = "Residente trasladado",
        ["RESIDENT_DISCHARGE"] = "Residente dado de baja",
        ["RESIDENT_REACTIVATE"] = "Residente reactivado",
        ["RESIDENT_SUSPEND"] = "Residente suspendido por ingreso hospitalario",
        ["RESIDENT_RESUME"] = "Suspensión del residente terminada",
        ["FAMILY_MEMBER_CREATE"] = "Familiar añadido",
        ["FAMILY_MEMBER_UPDATE"] = "Datos del familiar cambiados",
        ["FAMILY_MEMBER_LINK"] = "Familiar vinculado a otro residente",
        ["FAMILY_MEMBER_UNLINK"] = "Familiar desvinculado del residente",
        ["FAMILY_AUTHORIZATION_CHANGE"] = "Autorización del familiar cambiada",
        ["EMERGENCY_CONTACT_DESIGNATE"] = "Contacto urgente designado",
        ["SHIFT_CREATE"] = "Turno creado",
        ["SHIFT_RENAME"] = "Turno renombrado",
        ["SHIFT_ACTIVATE"] = "Turno reactivado",
        ["SHIFT_DEACTIVATE"] = "Turno inactivado",
        ["TEAM_CREATE"] = "Equipo creado",
        ["TEAM_RENAME"] = "Equipo renombrado",
        ["TEAM_ACTIVATE"] = "Equipo reactivado",
        ["TEAM_DEACTIVATE"] = "Equipo inactivado",
        ["TEAM_MEMBER_ADD"] = "Miembro añadido a un equipo",
        ["TEAM_MEMBER_REMOVE"] = "Miembro dado de baja de un equipo",
        ["SCHEDULE_CREATE"] = "Turno planificado",
        ["SCHEDULE_RETIRE"] = "Planificación retirada",
        ["SCHEDULE_SERIES_RETIRE"] = "Serie de turnos retirada",
    };

    public static string Label(string code) => Labels.TryGetValue(code, out var label) ? label : "Acción administrativa";

    public static string CategoryLabel(AuditCategory category) => category switch
    {
        AuditCategory.Cuentas => "Cuentas",
        AuditCategory.PerfilesYPermisos => "Perfiles y permisos",
        AuditCategory.Estructura => "Estructura del centro",
        AuditCategory.ResidentesYFamilias => "Residentes y familias",
        AuditCategory.TurnosYEquipos => "Turnos y equipos",
        _ => category.ToString(),
    };

    /// <summary>Las acciones agrupadas por categoría, para el desplegable del filtro.</summary>
    public static IEnumerable<(string Category, IEnumerable<(string Code, string Label)> Actions)> Groups() =>
        AdministrativeAudit.Actions.GroupBy(a => a.Category)
            .Select(g => (CategoryLabel(g.Key), g.Select(a => (a.Code, Label(a.Code)))));
}
