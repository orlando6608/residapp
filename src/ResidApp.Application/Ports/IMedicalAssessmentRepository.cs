using ResidApp.Domain.Medicina;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

public sealed record StartMedicalAssessmentInput(AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision);

public sealed record SaveMedicalAssessmentInput(
    AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision, MedicalAssessmentContent Content);

public sealed record RegisterMedicalIndicationInput(
    AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision, MedicalIndication Indication);

/// <summary>MED-04/MED-05: empezar y guardar la valoración médica de un evento escalado; MED-06/MED-07:
/// registrar una indicación a Enfermería (exige la valoración médica guardada, MEDICAL_ASSESSMENT_REQUIRED).
/// Igual que INursingAssessmentRepository: cada operación exige la revisión del evento, la avanza en 1 y
/// devuelve la nueva; si otro profesional lo cambió entretanto, CLINICAL_EVENT_REVISION_CONFLICT.</summary>
public interface IMedicalAssessmentRepository
{
    Task<int> StartAsync(StartMedicalAssessmentInput input, CancellationToken ct = default);

    Task<int> SaveAsync(SaveMedicalAssessmentInput input, CancellationToken ct = default);

    Task<int> RegisterIndicationAsync(RegisterMedicalIndicationInput input, CancellationToken ct = default);
}
