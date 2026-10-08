using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;

namespace ResidApp.Web.Models;

/// <summary>MED-01: contadores de escalados recibidos, de indicaciones emitidas (con cuántas siguen sin leer
/// o tienen incidencia) y de seguimientos médicos abiertos (con cuántos están vencidos). Comunicaciones e
/// Historial de Medicina llegarán con el resto del vertical. Protocolos: protocolos urgentes activos.</summary>
public sealed record MedicinaInicioViewModel(
    int Escalados, int Indicaciones, int IndicacionesSinLeer, int IndicacionesConIncidencia, int Seguimientos, int SeguimientosVencidos,
    int Protocolos, IReadOnlyList<MilestoneEntry>? Avisos = null);

/// <summary>MED-03: detalle del escalado con su información reunida (solo lectura), el basal vigente del
/// residente (null si no tiene) y la parte médica (valoración e indicaciones). CorrectionWindow decide si se
/// ofrece corregir o rectificar la valoración médica (COR-01/COR-02).</summary>
public sealed record MedicinaEscaladoViewModel(PendingChangeDetail Event, CurrentBaselineSummary? Baseline, TimeSpan CorrectionWindow);

/// <summary>MED-05 "valoración médica": hallazgos y exploración, valoración, actuaciones y constantes
/// opcionales. Revision es la del evento al abrir la pantalla (concurrencia optimista); el dominio
/// (MedicalAssessmentContent, VitalSigns) es quien decide si el contenido es válido.</summary>
public sealed class ValoracionMedicaFormModel : ConstantesFormModel
{
    [Required]
    public Guid EventoId { get; set; }

    [Required]
    public int Revision { get; set; }

    [StringLength(MedicalAssessmentContent.MaxTextLength)]
    [Display(Name = "Hallazgos y exploración")]
    public string? HallazgosExploracion { get; set; }

    [StringLength(MedicalAssessmentContent.MaxTextLength)]
    [Display(Name = "Valoración")]
    public string? Valoracion { get; set; }

    [StringLength(MedicalAssessmentContent.MaxTextLength)]
    [Display(Name = "Actuaciones")]
    public string? Actuaciones { get; set; }

    public static ValoracionMedicaFormModel From(PendingChangeDetail detail)
    {
        var form = new ValoracionMedicaFormModel { EventoId = detail.EventId, Revision = detail.Revision };
        if (detail.Medical.Assessment is not { Content: var content })
        {
            return form;
        }
        form.HallazgosExploracion = content.FindingsAndExamination;
        form.Valoracion = content.Assessment;
        form.Actuaciones = content.Actions;
        form.FillVitals(content.Vitals);
        return form;
    }
}

public sealed record ValoracionMedicaViewModel(PendingChangeDetail Event, ValoracionMedicaFormModel Form);

/// <summary>MED-07 "indicación a Enfermería": texto, fecha prevista o criterio (al menos uno) e información
/// adicional. No hay selector de prioridad.</summary>
public sealed class IndicacionFormModel
{
    [Required]
    public Guid EventoId { get; set; }

    [Required]
    public int Revision { get; set; }

    [Required(ErrorMessage = "Escribe la indicación.")]
    [StringLength(MedicalIndication.MaxTextLength)]
    [Display(Name = "Indicación")]
    public string? Texto { get; set; }

    [Display(Name = "Fecha prevista")]
    public DateOnly? FechaPrevista { get; set; }

    [StringLength(FollowUpPlan.MaxCriterionLength)]
    [Display(Name = "Criterio")]
    public string? Criterio { get; set; }

    [StringLength(MedicalIndication.MaxTextLength)]
    [Display(Name = "Información adicional")]
    public string? InformacionAdicional { get; set; }
}

public sealed record IndicacionViewModel(PendingChangeDetail Event, IndicacionFormModel Form);

/// <summary>MED-10 "crear seguimiento médico": fecha prevista o criterio (al menos uno) y objetivo
/// obligatorio; lo decide el dominio (FollowUpPlan, MedicalFollowUp). El equipo responsable es Medicina de la
/// unidad.</summary>
public sealed class IniciarSeguimientoMedicoFormModel
{
    [Required]
    public Guid EventoId { get; set; }

    [Required]
    public int Revision { get; set; }

    [Display(Name = "Fecha prevista de revisión")]
    public DateOnly? FechaPrevista { get; set; }

    [StringLength(FollowUpPlan.MaxCriterionLength)]
    [Display(Name = "Criterio de revisión")]
    public string? Criterio { get; set; }

    [Required(ErrorMessage = "Escribe el objetivo del seguimiento.")]
    [StringLength(MedicalFollowUp.MaxObjectiveLength)]
    [Display(Name = "Objetivo del seguimiento")]
    public string? Objetivo { get; set; }
}

public sealed record IniciarSeguimientoMedicoViewModel(PendingChangeDetail Event, IniciarSeguimientoMedicoFormModel Form);

/// <summary>MED-11: textos del seguimiento médico, y quién lo lleva según la última decisión de continuidad al
/// terminar un turno. El equipo responsable es siempre Medicina de la unidad.</summary>
public static class MedicalFollowUpDisplay
{
    /// <summary>En Medicina la actuación del seguimiento es "registrar revisión" (MED-11).</summary>
    public static string Label(FollowUpActionType type) =>
        type == FollowUpActionType.Actuacion ? "Revisión" : FollowUpDisplay.Label(type);

    public static string Continuity(FollowUpActionType? lastContinuity, string? team, bool byCurrentAccount) => lastContinuity switch
    {
        FollowUpActionType.Transferencia => $"Transferido a «{team}»",
        FollowUpActionType.Conservacion => byCurrentAccount ? "Lo conservas tú para tu próxima revisión" : "Lo conserva otro profesional de Medicina",
        _ => "Sin decisión de continuidad todavía",
    };
}

/// <summary>Estado de una indicación (MED-08, ENF-10), siempre en texto. "Pendiente de lectura" usa el
/// amarillo de aviso con texto oscuro; "No realizada" el rojo, porque tiene incidencia.</summary>
public static class MedicalIndicationDisplay
{
    public static string Label(MedicalIndicationStatus status) => status switch
    {
        MedicalIndicationStatus.PendienteLectura => "Pendiente de lectura",
        MedicalIndicationStatus.Leida => "Leída, pendiente de realizar",
        MedicalIndicationStatus.Realizada => "Realizada",
        MedicalIndicationStatus.NoRealizada => "No realizada (con incidencia)",
        _ => status.ToString(),
    };

    public static string BadgeClass(MedicalIndicationStatus status) => status switch
    {
        MedicalIndicationStatus.PendienteLectura => "text-bg-warning",
        MedicalIndicationStatus.Leida => "text-bg-info",
        MedicalIndicationStatus.Realizada => "text-bg-success",
        _ => "text-bg-danger",
    };

    /// <summary>Vencida: tiene fecha prevista ya pasada y sigue sin realizarse.</summary>
    public static bool IsOverdue(MedicalIndicationSummary indication) =>
        indication.Status is MedicalIndicationStatus.PendienteLectura or MedicalIndicationStatus.Leida
        && FollowUpDisplay.IsOverdue(indication.DueDate);
}
