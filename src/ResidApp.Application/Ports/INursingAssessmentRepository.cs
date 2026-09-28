using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

public sealed record StartNursingAssessmentInput(AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision);

public sealed record SaveNursingAssessmentInput(
    AccountId AccountId, CenterId CenterId, Guid EventId, int ExpectedRevision, NursingAssessmentContent Content);

/// <summary>ENF-03 a ENF-05: empezar y guardar la valoración de un evento. Ambas operaciones exigen la
/// revisión con la que se abrió el evento y la avanzan en 1; si otro profesional lo modificó entretanto
/// lanzan CLINICAL_EVENT_REVISION_CONFLICT y hay que recargar. Devuelven la nueva revisión.</summary>
public interface INursingAssessmentRepository
{
    Task<int> StartAsync(StartNursingAssessmentInput input, CancellationToken ct = default);

    Task<int> SaveAsync(SaveNursingAssessmentInput input, CancellationToken ct = default);
}
