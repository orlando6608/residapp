using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Domain.Medicina;

/// <summary>
/// Contenido de la valoración médica (MED-05): hallazgos y exploración, valoración, actuaciones y constantes
/// opcionales, con las mismas comprobaciones de formato que la valoración de Enfermería (VitalSigns). Nunca
/// incluye ni modifica la observación original ni la valoración de Enfermería. Un guardado sin ningún dato
/// se rechaza.
/// </summary>
public sealed record MedicalAssessmentContent
{
    public const int MaxTextLength = 2000;

    public string? FindingsAndExamination { get; }
    public string? Assessment { get; }
    public string? Actions { get; }
    public VitalSigns Vitals { get; }

    public MedicalAssessmentContent(string? findingsAndExamination, string? assessment, string? actions, VitalSigns vitals)
    {
        findingsAndExamination = VitalSigns.Normalize(findingsAndExamination);
        assessment = VitalSigns.Normalize(assessment);
        actions = VitalSigns.Normalize(actions);

        var texts = new[] { findingsAndExamination, assessment, actions };
        if (texts.Any(t => t is { Length: > MaxTextLength }))
        {
            throw new DomainValidationException("MEDICAL_ASSESSMENT_TEXT_TOO_LONG");
        }
        if (texts.All(t => t is null) && vitals.IsEmpty)
        {
            throw new DomainValidationException("MEDICAL_ASSESSMENT_EMPTY");
        }

        FindingsAndExamination = findingsAndExamination;
        Assessment = assessment;
        Actions = actions;
        Vitals = vitals;
    }
}
