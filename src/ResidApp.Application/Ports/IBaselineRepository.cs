using ResidApp.Application.Authorization;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Baseline.Answers;
using ResidApp.Domain.Baseline.Catalogs;
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

/// <summary>Entrada de ENF-19/ENF-20 "crear borrador": los campos comunes de versión se exigen desde la
/// creación (no hay un paso posterior para editarlos en este alcance), así que viajan aquí en vez de como
/// una actualización separada del borrador.</summary>
public sealed record CreateBaselineDraftInput(
    AccountId AccountId, SystemProfile ActiveProfile, CenterId CenterId, UnitId UnitId, ResidentId ResidentId,
    BaselineReason ReasonCode, InformationSourceCode CommonInformationSourceCode, string? CommonInformationSourceOtherText,
    DateOnly CommonInformationDate, Guid OperationId);

public sealed record CreateBaselineDraftResult(BaselineDraftId DraftId, int DraftRevision);

/// <summary>Entrada compartida por LoadDraftAsync/SaveAreaAsync/SaveBarthelAsync/CancelDraftAsync: "el
/// borrador activo que yo mismo (esta cuenta, con este perfil) creé para este residente" — la misma
/// comprobación de propiedad y permiso vigente que LoadAuthorizedDraftAsync ya hace para la firma.</summary>
public sealed record OwnedActiveDraftInput(AccountId AccountId, SystemProfile ActiveProfile, CenterId CenterId, ResidentId ResidentId);

public sealed record BaselineDraftAreaDetail(BaselineArea AreaCode, IBaselineAreaAnswer Answer, string? Observation);

public sealed record BaselineDraftBarthelDetail(DateOnly? AssessmentDate, int? TotalScore, IReadOnlyList<BarthelItem> Items);

/// <summary>Estado completo del borrador activo propio (ENF-20/ENF-21/ENF-22): las áreas ya completadas
/// (nunca las 9 rellenas de null — el llamador decide cómo mostrar las que faltan) y el Barthel si ya
/// tiene fecha y algún ítem.</summary>
public sealed record BaselineDraftDetail(
    BaselineDraftId Id, ResidentId ResidentId, int DraftRevision, BaselineReason ReasonCode,
    InformationSourceCode CommonInformationSourceCode, string? CommonInformationSourceOtherText, DateOnly CommonInformationDate,
    DateTimeOffset CreatedAt, IReadOnlyList<BaselineDraftAreaDetail> Areas, BaselineDraftBarthelDetail Barthel);

public sealed record SaveBaselineDraftAreaInput(OwnedActiveDraftInput Owner, BaselineArea AreaCode, IBaselineAreaAnswer Answer, string? Observation);

public sealed record SaveBaselineDraftBarthelInput(OwnedActiveDraftInput Owner, DateOnly AssessmentDate, IReadOnlyList<BarthelItem> Items);

public sealed record CancelBaselineDraftInput(OwnedActiveDraftInput Owner, string Reason);

/// <summary>Traduce signBaselineDraft y readBaselineAsClinicalDirection (baseline-repository.ts y
/// audit-repository.ts): ambas operaciones combinan lectura autorizada, validación de dominio y escritura
/// atómica dentro de la misma transacción SQL Server. CreateDraftAsync/LoadOwnedDraftAsync/SaveAreaAsync/
/// SaveBarthelAsync/CancelDraftAsync (ENF-19 a ENF-22) completan el hueco: crear y editar el contenido de
/// un borrador antes de poder firmarlo.</summary>
public interface IBaselineRepository
{
    Task<CreateBaselineDraftResult> CreateDraftAsync(CreateBaselineDraftInput input, CancellationToken ct = default);

    Task<BaselineDraftDetail?> LoadOwnedDraftAsync(OwnedActiveDraftInput input, CancellationToken ct = default);

    Task SaveAreaAsync(SaveBaselineDraftAreaInput input, CancellationToken ct = default);

    Task SaveBarthelAsync(SaveBaselineDraftBarthelInput input, CancellationToken ct = default);

    Task CancelDraftAsync(CancelBaselineDraftInput input, CancellationToken ct = default);

    Task<SignBaselineDraftResult> SignDraftAsync(SignBaselineDraftInput input, CancellationToken ct = default);

    Task<IReadOnlyList<AuditedBaselineHeader>> ReadAsClinicalDirectionAsync(
        ClinicalDirectionReadInput input, CancellationToken ct = default);

    Task<CurrentBaselineSummary?> ReadCurrentSummaryAsync(ReadCurrentBaselineSummaryInput input, CancellationToken ct = default);
}
