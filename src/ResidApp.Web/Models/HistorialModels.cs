using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Enfermeria;

namespace ResidApp.Web.Models;

/// <summary>Historial del residente (ENF-23/ENF-24, MED-22), vista común a Enfermería y Medicina. Una lista null
/// significa que no se pudo cargar, que es un estado distinto de la lista vacía. DetailAction es la acción de
/// detalle del controlador en curso (DetalleCambio en Enfermería, Escalado en Medicina).</summary>
public sealed record ResidentHistoryViewModel(
    ScopeResidentSummary Resident, IReadOnlyList<ClosedEventSummary>? Events, IReadOnlyList<BaselineHistoryEntry>? Baselines,
    string DetailAction)
{
    public static ResidentHistoryViewModel From(
        ScopeResidentSummary resident, ApplicationResult<IReadOnlyList<ClosedEventSummary>> events,
        ApplicationResult<IReadOnlyList<BaselineHistoryEntry>> baselines, string detailAction) =>
        new(resident, events.Ok ? events.Value : null, baselines.Ok ? baselines.Value : null, detailAction);
}

public static class ResidentHistoryDisplay
{
    /// <summary>El mismo resumen que las bandejas: la observación del evento propio o las áreas del cambio de
    /// Auxiliar.</summary>
    public static string Summary(ClosedEventSummary item) => item.Origin switch
    {
        ClinicalEventOrigin.CambioAuxiliar => string.Join(", ", item.Areas.Select(DailyChangeAreaDisplay.Label)),
        _ => item.Observation is null ? "—" : item.Observation.Length <= 120 ? item.Observation : item.Observation[..120] + "…",
    };

    /// <summary>Quién cerró, con la misma deducción que Shared/_CierreEvento: un escalado solo lo cierra Medicina
    /// y un evento propio de Medicina nunca pasa por Enfermería.</summary>
    public static string ClosedBy(ClosedEventSummary item) =>
        item.Escalated || item.Origin == ClinicalEventOrigin.EventoMedicina ? "Medicina" : "Enfermería";

    /// <summary>HIS-03: basal y ubicación de la fecha del evento.</summary>
    public static string Context(ClinicalEventContext? context) => context is null
        ? "Sin datos de contexto"
        : $"{Baseline(context)} · {context.UnitName ?? "sin ubicación registrada"}";

    public static string Baseline(ClinicalEventContext context) => context.BaselineVersionNumber is null
        ? "Sin basal firmado"
        : $"Basal versión {context.BaselineVersionNumber}, firmado el {context.BaselineSignedAt!.Value.ToLocalTime():g}";
}

public static class BaselineReasonDisplay
{
    public static string Label(BaselineReason reason) => reason switch
    {
        BaselineReason.Alta => "Alta",
        BaselineReason.RevisionProgramada => "Revisión programada",
        BaselineReason.CambioFuncionalConsolidado => "Cambio funcional consolidado",
        _ => reason.ToString(),
    };
}
