using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;

namespace ResidApp.Web.Models;

/// <summary>DIR-01/DIR-02: contadores por unidad del ámbito de supervisión.</summary>
public sealed record DireccionInicioViewModel(IReadOnlyList<SupervisionUnitSummary> Units)
{
    public int Open => Units.Sum(u => u.Open);
    public int FollowUpsOverdue => Units.Sum(u => u.FollowUpsOverdue);
}

/// <summary>DIR-03: filtrar los pendientes por tipo y unidad. Llega por GET (?tipo=&amp;unidad=); los campos vacíos no filtran.</summary>
public sealed class SupervisionFilter
{
    public SupervisionPendingType? Tipo { get; set; }

    public Guid? Unidad { get; set; }

    public bool IsEmpty => Tipo is null && Unidad is null;
}

/// <summary>DIR-03: la lista ya filtrada con las unidades para el desplegable (las del ámbito).</summary>
public sealed record SupervisionPendingViewModel(
    SupervisionPendingList List, SupervisionFilter Filter, IReadOnlyList<(Guid Id, string Name)> Units, DateOnly Today);

public static class SupervisionDisplay
{
    public static string Label(SupervisionPendingType type) => type switch
    {
        SupervisionPendingType.SinValorar => "Sin valorar",
        SupervisionPendingType.Escalado => "Escalado a Medicina",
        SupervisionPendingType.Seguimiento => "En seguimiento",
        SupervisionPendingType.SeguimientoVencido => "Seguimiento vencido",
        SupervisionPendingType.IndicacionPendiente => "Con indicación pendiente",
        SupervisionPendingType.IndicacionConIncidencia => "Con indicación no realizada",
        SupervisionPendingType.ProtocoloUrgente => "Protocolo urgente activo",
        _ => type.ToString(),
    };

    public static string Label(SupervisionMilestoneKind kind) => kind switch
    {
        SupervisionMilestoneKind.Registrado => "Registrado",
        SupervisionMilestoneKind.ValoracionIniciada => "Valoración de Enfermería iniciada",
        SupervisionMilestoneKind.ValoracionMedicaIniciada => "Valoración médica iniciada",
        SupervisionMilestoneKind.Escalado => "Escalado a Medicina",
        SupervisionMilestoneKind.IndicacionEmitida => "Indicación emitida",
        SupervisionMilestoneKind.SeguimientoIniciado => "Seguimiento iniciado",
        SupervisionMilestoneKind.ProtocoloUrgenteActivado => "Protocolo urgente activado",
        SupervisionMilestoneKind.InformeDerivacionFirmado => "Informe de derivación firmado",
        _ => kind.ToString(),
    };

    /// <summary>DIR-17: nombre en español de los permisos que puede tener un ámbito de Dirección.</summary>
    public static string PermissionLabel(string code) => code switch
    {
        "CLINICAL_DETAIL_READ" => "Lectura clínica detallada y auditada",
        "REFERENCE_RANGES_MANAGE" => "Gestionar los rangos de referencia de constantes",
        _ => code,
    };
}
