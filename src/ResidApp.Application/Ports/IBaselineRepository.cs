using ResidApp.Application.Authorization;
using ResidApp.Domain.Baseline;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Traduce SignBaselineDraftInput de db/repositories/baseline-repository.ts. ExpectedDraftRevision
/// implementa la concurrencia optimista equivalente a expectedDraftRevision del TS original.</summary>
public sealed record SignBaselineDraftInput(
    AccountId AccountId, SystemProfile ActiveProfile, CenterId CenterId, UnitId UnitId,
    ResidentId ResidentId, BaselineDraftId DraftId, int ExpectedDraftRevision, Guid OperationId);

/// <summary>Traduce SignBaselineDraftResult de baseline-repository.ts.</summary>
public sealed record SignBaselineDraftResult(BaselineVersionId BaselineVersionId, int VersionNumber);

/// <summary>Traduce ClinicalDirectionReadInput de db/repositories/audit-repository.ts.</summary>
public sealed record ClinicalDirectionReadInput(
    AccountId AccountId, CenterId CenterId, UnitId UnitId, ResidentId ResidentId,
    ClinicalResourceType ResourceType, ClinicalDetailAccessPurpose Purpose);

/// <summary>Traduce AuditedBaselineHeader de audit-repository.ts.</summary>
public sealed record AuditedBaselineHeader(BaselineVersionId Id, int VersionNumber, BaselineReason ReasonCode, DateTimeOffset SignedAt);

/// <summary>Traduce signBaselineDraft y readBaselineAsClinicalDirection (baseline-repository.ts y
/// audit-repository.ts): ambas operaciones combinan lectura autorizada, validación de dominio y escritura
/// atómica dentro de la misma transacción SQL Server.</summary>
public interface IBaselineRepository
{
    Task<SignBaselineDraftResult> SignDraftAsync(SignBaselineDraftInput input, CancellationToken ct = default);

    Task<IReadOnlyList<AuditedBaselineHeader>> ReadAsClinicalDirectionAsync(
        ClinicalDirectionReadInput input, CancellationToken ct = default);
}
