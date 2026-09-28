using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

public sealed record StartNursingAssessmentInput(AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision);

public sealed record SaveNursingAssessmentInput(
    AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision, NursingAssessmentContent Content);

public sealed record CloseClinicalEventInput(
    AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision, Guid OperationId,
    FamilyCommunicationChoice Communication);

public sealed record StartFollowUpInput(
    AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision, FollowUpPlan Plan, string? ContinuityNotes);

public sealed record EscalateClinicalEventInput(
    AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision, EscalationReason Reason);

public sealed record RecordFollowUpActionInput(
    AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision, FollowUpAction Action);

/// <summary>ENF-03 a ENF-05: empezar y guardar la valoración de un evento; ENF-06/ENF-07A: cerrarlo con la
/// decisión de comunicación familiar. Las tres operaciones exigen la revisión con la que se abrió el evento
/// y la avanzan en 1; si otro profesional lo modificó entretanto lanzan CLINICAL_EVENT_REVISION_CONFLICT y
/// hay que recargar. Devuelven la nueva revisión. Cerrar exige una valoración guardada
/// (NURSING_ASSESSMENT_REQUIRED) y es idempotente por OperationId: repetir el mismo cierre devuelve el mismo
/// resultado sin cerrar dos veces. ENF-07B a ENF-09: iniciar un seguimiento (también exige la valoración
/// guardada) y registrar acciones sobre él; confirmar una recepción que ya no está pendiente lanza
/// FOLLOW_UP_TRANSFER_NOT_PENDING. ENF-09/ENF-10: escalar a Medicina desde la valoración o el seguimiento,
/// cerrando la valoración (también exige que esté guardada).</summary>
public interface INursingAssessmentRepository
{
    Task<int> EscalateAsync(EscalateClinicalEventInput input, CancellationToken ct = default);

    Task<int> StartFollowUpAsync(StartFollowUpInput input, CancellationToken ct = default);

    Task<int> RecordFollowUpActionAsync(RecordFollowUpActionInput input, CancellationToken ct = default);

    Task<int> StartAsync(StartNursingAssessmentInput input, CancellationToken ct = default);

    Task<int> SaveAsync(SaveNursingAssessmentInput input, CancellationToken ct = default);

    Task<int> CloseAsync(CloseClinicalEventInput input, CancellationToken ct = default);
}
