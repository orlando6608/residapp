using ResidApp.Domain.Medicina;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

public sealed record AcknowledgeMedicalIndicationInput(AccountId AccountId, CenterId CenterId, Guid IndicationId, int ExpectedRevision);

public sealed record ResolveMedicalIndicationInput(
    AccountId AccountId, CenterId CenterId, Guid IndicationId, int ExpectedRevision, MedicalIndicationOutcome Outcome);

/// <summary>ENF-10: Enfermería confirma la lectura de una indicación y, después, registra si fue realizada o
/// no realizada con incidencia. Cada paso exige la revisión de la indicación y la avanza en 1 (no la del
/// evento); con una revisión desactualizada o un paso fuera de orden, MEDICAL_INDICATION_REVISION_CONFLICT.</summary>
public interface IMedicalIndicationRepository
{
    Task<int> AcknowledgeAsync(AcknowledgeMedicalIndicationInput input, CancellationToken ct = default);

    Task<int> ResolveAsync(ResolveMedicalIndicationInput input, CancellationToken ct = default);
}
