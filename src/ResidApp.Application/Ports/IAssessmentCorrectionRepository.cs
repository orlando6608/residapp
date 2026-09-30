using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>ExpectedCorrections es el número de correcciones de la valoración que había al abrir el
/// formulario: si alguien corrigió entretanto, ASSESSMENT_CORRECTION_CONFLICT.</summary>
public sealed record CorrectNursingAssessmentInput(
    AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedCorrections, NursingAssessmentContent Content,
    AssessmentCorrectionReason Reason, TimeSpan Window);

public sealed record CorrectMedicalAssessmentInput(
    AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedCorrections, MedicalAssessmentContent Content,
    AssessmentCorrectionReason Reason, TimeSpan Window);

/// <summary>Profile dice qué valoración se rectifica. ExpectedRectifications, igual que ExpectedCorrections,
/// evita duplicar la rectificación al reenviar el formulario.</summary>
public sealed record RectifyAssessmentInput(
    AccountId AccountId, CenterId CenterId, Guid EventId, SystemProfile Profile, int ExpectedRectifications,
    AssessmentRectification Rectification, TimeSpan Window);

/// <summary>
/// COR-01/COR-02 sobre las valoraciones de Enfermería y médica. Las tres operaciones exigen:
/// <list type="bullet">
/// <item>que la valoración exista y ya no se pueda guardar de forma normal, porque el evento salió de su estado
/// de valoración (ASSESSMENT_CORRECTION_NOT_AVAILABLE);</item>
/// <item>que la cuenta sea la que guardó la última versión ordinaria (acceso denegado si no).</item>
/// </list>
/// Corregir exige la ventana abierta (ASSESSMENT_CORRECTION_WINDOW_EXPIRED) y rectificar, cerrada
/// (ASSESSMENT_RECTIFICATION_WINDOW_OPEN). Ninguna toca la revisión del evento.
/// </summary>
public interface IAssessmentCorrectionRepository
{
    Task CorrectNursingAsync(CorrectNursingAssessmentInput input, CancellationToken ct = default);

    Task CorrectMedicalAsync(CorrectMedicalAssessmentInput input, CancellationToken ct = default);

    Task RectifyAsync(RectifyAssessmentInput input, CancellationToken ct = default);
}
