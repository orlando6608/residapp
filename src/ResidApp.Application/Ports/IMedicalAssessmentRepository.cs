using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

public sealed record StartMedicalAssessmentInput(AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision);

public sealed record SaveMedicalAssessmentInput(
    AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision, MedicalAssessmentContent Content);

public sealed record RegisterMedicalIndicationInput(
    AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision, MedicalIndication Indication);

public sealed record StartMedicalFollowUpInput(
    AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision, MedicalFollowUp FollowUp);

public sealed record RecordMedicalFollowUpActionInput(
    AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision, FollowUpAction Action);

/// <summary>MED-04/MED-05: empezar y guardar la valoración médica de un evento escalado; MED-06/MED-07:
/// registrar una indicación a Enfermería (exige la valoración médica guardada, MEDICAL_ASSESSMENT_REQUIRED);
/// MED-10 a MED-12: iniciar el seguimiento médico (uno por evento, MEDICAL_FOLLOW_UP_ALREADY_STARTED) y
/// registrar sus acciones; MED-15: cerrar el evento, idempotente como el cierre de Enfermería (CloseClinicalEventInput).
/// Igual que INursingAssessmentRepository: cada operación exige la revisión del evento, la avanza en 1 y
/// devuelve la nueva; si otro profesional lo cambió entretanto, CLINICAL_EVENT_REVISION_CONFLICT.</summary>
public interface IMedicalAssessmentRepository
{
    Task<int> StartAsync(StartMedicalAssessmentInput input, CancellationToken ct = default);

    Task<int> SaveAsync(SaveMedicalAssessmentInput input, CancellationToken ct = default);

    Task<int> RegisterIndicationAsync(RegisterMedicalIndicationInput input, CancellationToken ct = default);

    Task<int> StartFollowUpAsync(StartMedicalFollowUpInput input, CancellationToken ct = default);

    Task<int> RecordFollowUpActionAsync(RecordMedicalFollowUpActionInput input, CancellationToken ct = default);

    Task<int> CloseAsync(CloseClinicalEventInput input, CancellationToken ct = default);
}
