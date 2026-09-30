using ResidApp.Shared;

namespace ResidApp.Domain.Enfermeria;

/// <summary>
/// COR-01/COR-02, política provisional del usuario (2026-09-30): solo se corrigen las valoraciones de Enfermería
/// y médica, y solo su autor. Dentro de la ventana se guarda una versión corregida con motivo; fuera, una
/// rectificación añadida con texto y motivo. La ventana empieza en el último guardado ordinario de la
/// valoración y las correcciones no la amplían.
/// </summary>
public static class AssessmentCorrectionWindow
{
    public static bool IsOpen(DateTimeOffset lastSavedAt, DateTimeOffset now, TimeSpan window) => now < lastSavedAt + window;
}

/// <summary>COR-01/COR-02: motivo obligatorio de una corrección o una rectificación.</summary>
public sealed record AssessmentCorrectionReason
{
    public const int MaxLength = 500;

    public string Text { get; }

    public AssessmentCorrectionReason(string? text)
    {
        Text = VitalSigns.Normalize(text) is { Length: <= MaxLength } normalized
            ? normalized
            : throw new DomainValidationException("ASSESSMENT_CORRECTION_REASON_INVALID");
    }
}

/// <summary>COR-02: rectificación añadida fuera de la ventana. No cambia la valoración: se muestra debajo.</summary>
public sealed record AssessmentRectification
{
    public const int MaxTextLength = 2000;

    public string Text { get; }
    public AssessmentCorrectionReason Reason { get; }

    public AssessmentRectification(string? text, string? reason)
    {
        Text = VitalSigns.Normalize(text) is { Length: <= MaxTextLength } normalized
            ? normalized
            : throw new DomainValidationException("ASSESSMENT_RECTIFICATION_TEXT_INVALID");
        Reason = new AssessmentCorrectionReason(reason);
    }
}
