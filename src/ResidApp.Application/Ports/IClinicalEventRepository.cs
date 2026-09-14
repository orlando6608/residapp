using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Traduce el registro de un evento observado directamente por Enfermería (ENF-16), en su alcance
/// mínimo: solo registrar y guardar, sin bandeja ni valoración todavía (grupos E4/E5, pendientes).
/// Classification reutiliza DailyChangeClassification (Ordinario/Prioritario): mismo catálogo cerrado que
/// ya usa AUX-10 para clasificar un cambio, no un catálogo nuevo.</summary>
public sealed record RegisterClinicalEventInput(
    AccountId AccountId, CenterId CenterId, UnitId UnitId, ResidentId ResidentId,
    string Observation, DailyChangeClassification Classification, string? ClinicalData, Guid OperationId);

public sealed record ClinicalEventResult(Guid EventId, DateTimeOffset OccurredAt);

/// <summary>ENF-16: registro idempotente de un evento clínico propio de Enfermería.</summary>
public interface IClinicalEventRepository
{
    Task<ClinicalEventResult> RegisterAsync(RegisterClinicalEventInput input, CancellationToken ct = default);
}
