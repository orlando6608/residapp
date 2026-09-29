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

/// <summary>ENF-11/MED-13: activar el protocolo urgente y registrar dentro de él. Comunes a Enfermería y
/// Medicina, como CloseClinicalEventInput.</summary>
public sealed record ActivateUrgentProtocolInput(
    AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision, UrgentProtocolActivation Activation);

public sealed record RecordUrgentProtocolEntryInput(
    AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision, UrgentProtocolEntry Entry);

/// <summary>ENF-12/MED-14: firmar el informe de derivación, ya compuesto, con su huella, el PDF generado y la
/// hora de la firma (la misma que figura en el PDF). Idempotente por OperationId, como el cierre.</summary>
public sealed record SignReferralReportInput(
    AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision, Guid OperationId,
    ReferralReportInput Report, ReferralReportContent Content, string ContentHash, byte[] Pdf, string PdfHash,
    DateTimeOffset SignedAt);

/// <summary>DER-06: registrar un intento de llamada al contacto familiar (exige el informe firmado).</summary>
public sealed record RecordFamilyCallAttemptInput(
    AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision, FamilyCallAttempt Attempt);

/// <summary>ENF-03 a ENF-05: empezar y guardar la valoración de un evento; ENF-06/ENF-07A: cerrarlo con la
/// decisión de comunicación familiar. Las tres operaciones exigen la revisión con la que se abrió el evento
/// y la avanzan en 1; si otro profesional lo modificó entretanto lanzan CLINICAL_EVENT_REVISION_CONFLICT y
/// hay que recargar. Devuelven la nueva revisión. Cerrar exige una valoración guardada
/// (NURSING_ASSESSMENT_REQUIRED) y es idempotente por OperationId: repetir el mismo cierre devuelve el mismo
/// resultado sin cerrar dos veces. ENF-07B a ENF-09: iniciar un seguimiento (también exige la valoración
/// guardada) y registrar acciones sobre él; confirmar una recepción que ya no está pendiente lanza
/// FOLLOW_UP_TRANSFER_NOT_PENDING. ENF-09/ENF-10: escalar a Medicina desde la valoración o el seguimiento,
/// cerrando la valoración (también exige que esté guardada). ENF-11: activar el protocolo urgente (exige la
/// valoración guardada; desde el protocolo solo se deriva o se cierra, así que hay uno por evento) y registrar
/// dentro de él. ENF-12/ENF-14: firmar el informe de derivación (uno por evento; el evento sigue en el
/// protocolo) y registrar los intentos de llamada a la familia (REFERRAL_REPORT_REQUIRED sin informe).</summary>
public interface INursingAssessmentRepository
{
    Task<int> ActivateUrgentProtocolAsync(ActivateUrgentProtocolInput input, CancellationToken ct = default);

    Task<int> RecordUrgentProtocolEntryAsync(RecordUrgentProtocolEntryInput input, CancellationToken ct = default);

    Task<int> SignReferralReportAsync(SignReferralReportInput input, CancellationToken ct = default);

    Task<int> RecordFamilyCallAttemptAsync(RecordFamilyCallAttemptInput input, CancellationToken ct = default);

    Task<int> EscalateAsync(EscalateClinicalEventInput input, CancellationToken ct = default);

    Task<int> StartFollowUpAsync(StartFollowUpInput input, CancellationToken ct = default);

    Task<int> RecordFollowUpActionAsync(RecordFollowUpActionInput input, CancellationToken ct = default);

    Task<int> StartAsync(StartNursingAssessmentInput input, CancellationToken ct = default);

    Task<int> SaveAsync(SaveNursingAssessmentInput input, CancellationToken ct = default);

    Task<int> CloseAsync(CloseClinicalEventInput input, CancellationToken ct = default);
}
