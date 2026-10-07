using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
using ResidApp.Shared;

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

/// <summary>Lista de eventos cerrados (parcial _EventosCerradosLista) de Enfermería, Medicina y Dirección. DetailAction null: sin
/// enlace al detalle (la lectura auditada de Dirección no ofrece las pantallas de otros perfiles).</summary>
public sealed record ClosedEventsListModel(IReadOnlyList<ClosedEventSummary> Events, string? DetailAction);

/// <summary>Hitos de la línea temporal (parcial _LineaTemporalLista), con la misma convención de DetailAction.</summary>
public sealed record TimelineListModel(IReadOnlyList<TimelineEntry> Entries, string? DetailAction);

/// <summary>ENF-24 (historia 11 de Enfermería, 9 de Medicina): una versión firmada del basal, vista común a
/// Enfermería y Medicina.</summary>
public sealed record BaselineVersionViewModel(ScopeResidentSummary Resident, BaselineVersionDetail Version);

public static class BaselineVersionDisplay
{
    /// <summary>La etiqueta de la opción elegida en un ítem del Barthel (BarthelCatalog); si el catálogo ya no la tiene,
    /// su código.</summary>
    public static string BarthelOption(BarthelItem item) =>
        BarthelCatalog.Options[item.ItemCode].FirstOrDefault(o => o.OptionCode == item.SelectedOptionCode)?.Label
        ?? item.SelectedOptionCode;
}

public static class ResidentHistoryDisplay
{
    /// <summary>El mismo resumen que las bandejas: la observación del evento propio o las áreas del cambio de
    /// Auxiliar.</summary>
    public static string Summary(ClosedEventSummary item) => Summary(item.Origin, item.Areas, item.Observation);

    /// <summary>ENF-18/MED-20: el mismo resumen para un evento abierto de la ficha del residente.</summary>
    public static string Summary(OpenEventSummary item) => Summary(item.Origin, item.Areas, item.Observation);

    private static string Summary(ClinicalEventOrigin origin, IReadOnlyList<DailyChangeAreaCode> areas, string? observation) => origin switch
    {
        ClinicalEventOrigin.CambioAuxiliar => string.Join(", ", areas.Select(DailyChangeAreaDisplay.Label)),
        _ => observation is null ? "—" : observation.Length <= 120 ? observation : observation[..120] + "…",
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

/// <summary>Línea temporal del residente (HIS-02, MED-24), vista común a Enfermería y Medicina. Entries null
/// significa que no se pudo cargar.</summary>
public sealed record ResidentTimelineViewModel(ScopeResidentSummary Resident, IReadOnlyList<TimelineEntry>? Entries, string DetailAction);

/// <summary>Textos en español de cada hito de la línea temporal: su título y sus campos, sin los vacíos.</summary>
public static class TimelineDisplay
{
    public static string Title(TimelineEntry entry) => entry switch
    {
        TimelineEntry.EventRegistered e => e.Origin == ClinicalEventOrigin.CambioAuxiliar ? "Cambio registrado por Auxiliar" : "Evento registrado",
        TimelineEntry.NursingAssessmentSaved => "Valoración de Enfermería guardada",
        TimelineEntry.MedicalAssessmentSaved => "Valoración médica guardada",
        TimelineEntry.NursingAssessmentCorrected => "Valoración de Enfermería corregida",
        TimelineEntry.MedicalAssessmentCorrected => "Valoración médica corregida",
        TimelineEntry.AssessmentRectified r => r.Profile == SystemProfile.Medicina
            ? "Rectificación de la valoración médica"
            : "Rectificación de la valoración de Enfermería",
        TimelineEntry.Escalated => "Escalado a Medicina",
        TimelineEntry.IndicationIssued => "Indicación de Medicina",
        TimelineEntry.IndicationRead => "Indicación leída",
        TimelineEntry.IndicationResolved r => r.Status == MedicalIndicationStatus.Realizada ? "Indicación realizada" : "Indicación no realizada",
        TimelineEntry.FollowUpStarted f => f.Profile == SystemProfile.Medicina ? "Seguimiento médico iniciado" : "Seguimiento iniciado",
        TimelineEntry.FollowUpActionRecorded a => a.Profile == SystemProfile.Medicina
            ? $"Seguimiento médico: {MedicalFollowUpDisplay.Label(a.Type)}"
            : $"Seguimiento: {FollowUpDisplay.Label(a.Type)}",
        TimelineEntry.UrgentProtocolActivated => "Protocolo urgente activado",
        TimelineEntry.UrgentProtocolEntryRecorded p => $"Protocolo urgente: {UrgentProtocolDisplay.Label(p.Type)}",
        TimelineEntry.ReferralSigned => "Informe de derivación a Urgencias firmado",
        TimelineEntry.FamilyCallAttempted => "Llamada a la familia",
        TimelineEntry.FamilyCommunicationPrepared => "Comunicación familiar preparada",
        TimelineEntry.EventClosed => "Evento cerrado",
        TimelineEntry.BaselineSigned b => $"Basal firmado (versión {b.VersionNumber})",
        TimelineEntry.LocationStarted => "Ubicación",
        _ => entry.GetType().Name,
    };

    public static IReadOnlyList<(string Label, string Value)> Fields(TimelineEntry entry)
    {
        var fields = new List<(string Label, string? Value)>();
        switch (entry)
        {
            case TimelineEntry.EventRegistered e:
                fields.Add(("Clasificación", e.Classification == DailyChangeClassification.Prioritario ? "Prioritario" : "Ordinario"));
                fields.Add(e.Origin == ClinicalEventOrigin.CambioAuxiliar
                    ? ("Áreas", string.Join(", ", e.Areas.Select(DailyChangeAreaDisplay.Label)))
                    : ("Observación", e.Observation));
                break;
            case TimelineEntry.NursingAssessmentSaved n:
                fields.Add(("Hallazgos", n.Content.Findings));
                fields.Add(("Valoración", n.Content.Assessment));
                fields.Add(("Actuaciones", n.Content.Actions));
                fields.Add(("Comunicaciones", n.Content.Communications));
                fields.Add(("Resultado", n.Content.Outcome));
                fields.Add(("Constantes", VitalSignsDisplay.Summary(n.Content.Vitals)));
                break;
            case TimelineEntry.MedicalAssessmentSaved m:
                fields.Add(("Hallazgos y exploración", m.Content.FindingsAndExamination));
                fields.Add(("Valoración", m.Content.Assessment));
                fields.Add(("Actuaciones", m.Content.Actions));
                fields.Add(("Constantes", VitalSignsDisplay.Summary(m.Content.Vitals)));
                break;
            case TimelineEntry.NursingAssessmentCorrected n:
                fields.Add(("Motivo de la corrección", n.Reason));
                fields.Add(("Hallazgos", n.Content.Findings));
                fields.Add(("Valoración", n.Content.Assessment));
                fields.Add(("Actuaciones", n.Content.Actions));
                fields.Add(("Comunicaciones", n.Content.Communications));
                fields.Add(("Resultado", n.Content.Outcome));
                fields.Add(("Constantes", VitalSignsDisplay.Summary(n.Content.Vitals)));
                break;
            case TimelineEntry.MedicalAssessmentCorrected m:
                fields.Add(("Motivo de la corrección", m.Reason));
                fields.Add(("Hallazgos y exploración", m.Content.FindingsAndExamination));
                fields.Add(("Valoración", m.Content.Assessment));
                fields.Add(("Actuaciones", m.Content.Actions));
                fields.Add(("Constantes", VitalSignsDisplay.Summary(m.Content.Vitals)));
                break;
            case TimelineEntry.AssessmentRectified r:
                fields.Add(("Rectificación", r.Text));
                fields.Add(("Motivo", r.Reason));
                break;
            case TimelineEntry.Escalated s:
                fields.Add(("Motivo", s.Reason));
                break;
            case TimelineEntry.IndicationIssued i:
                fields.Add(("Indicación", i.Text));
                fields.Add(("Fecha prevista o criterio", FollowUpDisplay.Plan(i.DueDate, i.Criterion)));
                fields.Add(("Información adicional", i.AdditionalInformation));
                break;
            case TimelineEntry.IndicationRead r:
                fields.Add(("Indicación", r.Text));
                break;
            case TimelineEntry.IndicationResolved r:
                fields.Add(("Indicación", r.Text));
                fields.Add(("Incidencia", r.Incident));
                break;
            case TimelineEntry.FollowUpStarted f:
                fields.Add(("Fecha prevista o criterio", FollowUpDisplay.Plan(f.DueDate, f.Criterion)));
                fields.Add((f.Profile == SystemProfile.Medicina ? "Objetivo" : "Indicaciones de continuidad", f.Notes));
                break;
            case TimelineEntry.FollowUpActionRecorded a:
                fields.Add(("Texto", a.Text));
                fields.Add(("Nueva fecha o criterio", FollowUpDisplay.Plan(a.DueDate, a.Criterion)));
                fields.Add(("Equipo o turno entrante", a.IncomingTeam));
                break;
            case TimelineEntry.UrgentProtocolActivated p:
                fields.Add(("Nota", p.Note));
                break;
            case TimelineEntry.UrgentProtocolEntryRecorded p:
                fields.Add(("Servicio", p.Service));
                fields.Add(("Hora del contacto", p.ContactedAt?.ToLocalTime().ToString("g")));
                fields.Add(("Texto", p.Text));
                break;
            case TimelineEntry.ReferralSigned r:
                fields.Add(("Motivo", r.Reason));
                break;
            case TimelineEntry.FamilyCallAttempted c:
                fields.Add(("A quién", c.Contact));
                fields.Add(("Hora de la llamada", c.CalledAt.ToLocalTime().ToString("g")));
                fields.Add(("Resultado", ReferralDisplay.Label(c.Result)));
                fields.Add(("Nota", c.Note));
                break;
            case TimelineEntry.FamilyCommunicationPrepared c:
                fields.Add(("Tipo", FamilyCommunicationDisplay.Label(c.Type)));
                fields.Add(("Texto", c.Text));
                break;
            case TimelineEntry.EventClosed c:
                fields.Add(("Comunicación familiar", FamilyCommunicationDisplay.Label(c.Decision)));
                break;
            case TimelineEntry.BaselineSigned b:
                fields.Add(("Motivo", BaselineReasonDisplay.Label(b.Reason)));
                fields.Add(("Barthel", $"{b.BarthelTotal} / 100"));
                break;
            case TimelineEntry.LocationStarted l:
                fields.Add(("Unidad", l.UnitName ?? "Sin unidad"));
                break;
        }
        return fields.Where(f => !string.IsNullOrWhiteSpace(f.Value)).Select(f => (f.Label, f.Value!)).ToList();
    }
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
