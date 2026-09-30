using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;

namespace ResidApp.Web.Models;

/// <summary>COR-01/COR-02: lo que quien mira puede hacer con una valoración ya no editable de forma normal.
/// Solo su autor la corrige (dentro de la ventana) o la rectifica (fuera de ella). El servidor vuelve a
/// comprobarlo todo al guardar.</summary>
public enum AssessmentAmendmentAction
{
    Ninguna,
    Corregir,
    Rectificar,
}

public static class AssessmentAmendmentDisplay
{
    public static AssessmentAmendmentAction Action(AssessmentAmendments? amendments, bool editable, TimeSpan window) =>
        amendments is not { AuthoredByCurrentAccount: true } || editable
            ? AssessmentAmendmentAction.Ninguna
            : AssessmentCorrectionWindow.IsOpen(amendments.LastSavedAt, DateTimeOffset.UtcNow, window)
                ? AssessmentAmendmentAction.Corregir
                : AssessmentAmendmentAction.Rectificar;
}

/// <summary>Parcial _CorreccionesValoracion: correcciones y rectificaciones de una valoración y, si procede, el
/// botón para corregirla o rectificarla. CorrectionDeadline es hasta cuándo se puede corregir.</summary>
public sealed record CorreccionesValoracionViewModel(
    AssessmentAmendments? Amendments, AssessmentAmendmentAction Action, Guid EventoId, DateTimeOffset? CorrectionDeadline);

/// <summary>COR-01: corrección de la valoración de Enfermería. Correcciones es el número de correcciones al
/// abrir el formulario (concurrencia optimista).</summary>
public sealed class CorreccionValoracionFormModel : ConstantesFormModel
{
    [Required]
    public Guid EventoId { get; set; }

    [Required]
    public int Correcciones { get; set; }

    [Required(ErrorMessage = "Indica el motivo de la corrección.")]
    [StringLength(AssessmentCorrectionReason.MaxLength)]
    [Display(Name = "Motivo de la corrección")]
    public string? Motivo { get; set; }

    [StringLength(NursingAssessmentContent.MaxTextLength)]
    [Display(Name = "Hallazgos")]
    public string? Hallazgos { get; set; }

    [StringLength(NursingAssessmentContent.MaxTextLength)]
    [Display(Name = "Valoración")]
    public string? Valoracion { get; set; }

    [StringLength(NursingAssessmentContent.MaxTextLength)]
    [Display(Name = "Actuaciones")]
    public string? Actuaciones { get; set; }

    [StringLength(NursingAssessmentContent.MaxTextLength)]
    [Display(Name = "Comunicaciones")]
    public string? Comunicaciones { get; set; }

    [StringLength(NursingAssessmentContent.MaxTextLength)]
    [Display(Name = "Resultado")]
    public string? Resultado { get; set; }

    public static CorreccionValoracionFormModel From(PendingChangeDetail detail, NursingAssessmentDraft assessment)
    {
        var content = assessment.Content;
        var form = new CorreccionValoracionFormModel
        {
            EventoId = detail.EventId, Correcciones = assessment.Amendments?.Corrections.Count ?? 0,
            Hallazgos = content.Findings, Valoracion = content.Assessment, Actuaciones = content.Actions,
            Comunicaciones = content.Communications, Resultado = content.Outcome,
        };
        form.FillVitals(content.Vitals);
        return form;
    }
}

public sealed record CorreccionValoracionViewModel(PendingChangeDetail Event, CorreccionValoracionFormModel Form);

/// <summary>COR-01: corrección de la valoración médica.</summary>
public sealed class CorreccionValoracionMedicaFormModel : ConstantesFormModel
{
    [Required]
    public Guid EventoId { get; set; }

    [Required]
    public int Correcciones { get; set; }

    [Required(ErrorMessage = "Indica el motivo de la corrección.")]
    [StringLength(AssessmentCorrectionReason.MaxLength)]
    [Display(Name = "Motivo de la corrección")]
    public string? Motivo { get; set; }

    [StringLength(MedicalAssessmentContent.MaxTextLength)]
    [Display(Name = "Hallazgos y exploración")]
    public string? HallazgosExploracion { get; set; }

    [StringLength(MedicalAssessmentContent.MaxTextLength)]
    [Display(Name = "Valoración")]
    public string? Valoracion { get; set; }

    [StringLength(MedicalAssessmentContent.MaxTextLength)]
    [Display(Name = "Actuaciones")]
    public string? Actuaciones { get; set; }

    public static CorreccionValoracionMedicaFormModel From(PendingChangeDetail detail, MedicalAssessmentDraft assessment)
    {
        var content = assessment.Content;
        var form = new CorreccionValoracionMedicaFormModel
        {
            EventoId = detail.EventId, Correcciones = assessment.Amendments?.Corrections.Count ?? 0,
            HallazgosExploracion = content.FindingsAndExamination, Valoracion = content.Assessment, Actuaciones = content.Actions,
        };
        form.FillVitals(content.Vitals);
        return form;
    }
}

public sealed record CorreccionValoracionMedicaViewModel(PendingChangeDetail Event, CorreccionValoracionMedicaFormModel Form);

/// <summary>COR-02: rectificación añadida, común a Enfermería y Medicina. Rectificaciones es el número de
/// rectificaciones al abrir el formulario, para no duplicarla al reenviarlo.</summary>
public sealed class RectificacionFormModel
{
    [Required]
    public Guid EventoId { get; set; }

    [Required]
    public int Rectificaciones { get; set; }

    [Required(ErrorMessage = "Escribe la rectificación.")]
    [StringLength(AssessmentRectification.MaxTextLength)]
    [Display(Name = "Rectificación")]
    public string? Texto { get; set; }

    [Required(ErrorMessage = "Indica el motivo de la rectificación.")]
    [StringLength(AssessmentCorrectionReason.MaxLength)]
    [Display(Name = "Motivo")]
    public string? Motivo { get; set; }
}

/// <summary>AssessmentTitle es «valoración de Enfermería» o «valoración médica»; DetailAction, la acción de
/// detalle del controlador en curso.</summary>
public sealed record RectificacionViewModel(
    PendingChangeDetail Event, string AssessmentTitle, string DetailAction, RectificacionFormModel Form);
