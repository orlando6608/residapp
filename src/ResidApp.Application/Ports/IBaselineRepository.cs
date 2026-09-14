using ResidApp.Application.Authorization;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Baseline.Answers;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Traduce SignBaselineDraftInput de db/repositories/baseline-repository.ts. ExpectedDraftRevision
/// implementa la concurrencia optimista equivalente a expectedDraftRevision del TS original.</summary>
public sealed record SignBaselineDraftInput(
    AccountId AccountId, SystemProfile ActiveProfile, CenterId CenterId, UnitId UnitId,
    ResidentId ResidentId, BaselineDraftId DraftId, int ExpectedDraftRevision, Guid OperationId);

/// <summary>Traduce SignBaselineDraftResult de baseline-repository.ts.</summary>
public sealed record SignBaselineDraftResult(BaselineVersionId BaselineVersionId, int VersionNumber);

/// <summary>Traduce ClinicalDirectionReadInput de db/repositories/audit-repository.ts. OperationId es una
/// desviación deliberada del original (que no lo tenía): el esquema SQL ya reservaba 'CLINICAL_DETAIL_READ'
/// como action_code válido en idempotency_operations sin que ningún código lo usara — un reintento desde
/// una tablet con cobertura inestable duplicaba la fila de auditoría. Ver
/// docs/decisiones-arquitectura/directrices-pwa-movil.md, punto 4.</summary>
public sealed record ClinicalDirectionReadInput(
    AccountId AccountId, CenterId CenterId, UnitId UnitId, ResidentId ResidentId,
    ClinicalResourceType ResourceType, ClinicalDetailAccessPurpose Purpose, Guid OperationId);

/// <summary>Traduce AuditedBaselineHeader de audit-repository.ts.</summary>
public sealed record AuditedBaselineHeader(BaselineVersionId Id, int VersionNumber, BaselineReason ReasonCode, DateTimeOffset SignedAt);

/// <summary>Entrada de la lectura resumida del basal vigente para el cuidado cotidiano (AUX-03/ENF-20/MED-21):
/// no lleva OperationId porque, a diferencia de ClinicalDirectionReadInput, no escribe ningún evento de
/// auditoría — ResidentBaselinePolicy.AuthorizeBaselineCurrentRead permite esta lectura a Auxiliar,
/// Enfermería y Medicina sin esa obligación.</summary>
public sealed record ReadCurrentBaselineSummaryInput(CenterId CenterId, ResidentId ResidentId);

/// <summary>Una de las nueve áreas del basal vigente, ya deserializada a su tipo de dominio (nunca el
/// Barthel detallado por ítem: eso está fuera del alcance de esta lectura, ver AUX-03).</summary>
public sealed record BaselineAreaSummary(BaselineArea AreaCode, IBaselineAreaAnswer Answer, string? Observation);

/// <summary>Resumen del basal vigente: las nueve áreas y el total de Barthel, sin versiones históricas,
/// borradores, respuestas detalladas de Barthel, aportaciones, firma ni corrección (AUX-03).</summary>
public sealed record CurrentBaselineSummary(
    BaselineVersionId Id, int VersionNumber, BaselineReason ReasonCode, DateTimeOffset SignedAt,
    IReadOnlyList<BaselineAreaSummary> Areas, int BarthelTotal);

/// <summary>Traduce signBaselineDraft y readBaselineAsClinicalDirection (baseline-repository.ts y
/// audit-repository.ts): ambas operaciones combinan lectura autorizada, validación de dominio y escritura
/// atómica dentro de la misma transacción SQL Server.</summary>
public interface IBaselineRepository
{
    Task<SignBaselineDraftResult> SignDraftAsync(SignBaselineDraftInput input, CancellationToken ct = default);

    Task<IReadOnlyList<AuditedBaselineHeader>> ReadAsClinicalDirectionAsync(
        ClinicalDirectionReadInput input, CancellationToken ct = default);

    Task<CurrentBaselineSummary?> ReadCurrentSummaryAsync(ReadCurrentBaselineSummaryInput input, CancellationToken ct = default);
}
