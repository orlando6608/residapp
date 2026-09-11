using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Domain.Baseline;

/// <summary>Traduce BaselineAuthorship de lib/domain/baseline/baseline.ts.</summary>
public sealed record BaselineAuthorship(AccountId AccountId, ClinicalProfessionalProfile ActiveProfile, CenterId CenterId, UnitId UnitId);

/// <summary>Traduce BaselineSignature (BaselineAuthorship & {signedAt}) de baseline.ts. Se aplana en vez de
/// componer para reflejar fielmente la intersección de tipos TypeScript.</summary>
public sealed record BaselineSignature(
    AccountId AccountId, ClinicalProfessionalProfile ActiveProfile, CenterId CenterId, UnitId UnitId, DateTimeOffset SignedAt);

/// <summary>Traduce BaselineDraft (estado "BORRADOR") de baseline.ts.</summary>
public sealed record BaselineDraft(
    BaselineVersionId Id, ResidentId ResidentId, int VersionNumber, BaselineReason Reason,
    BaselineAuthorship CreatedBy, DateTimeOffset CreatedAt);

/// <summary>Traduce CurrentBaselineVersion (estado "FIRMADO_VIGENTE") de baseline.ts.</summary>
public sealed record CurrentBaselineVersion(
    BaselineVersionId Id, ResidentId ResidentId, int VersionNumber, BaselineReason Reason,
    BaselineAuthorship CreatedBy, DateTimeOffset CreatedAt, BaselineSignature Signature);

/// <summary>Traduce HistoricalBaselineVersion (estado "HISTORICO") de baseline.ts.</summary>
public sealed record HistoricalBaselineVersion(
    BaselineVersionId Id, ResidentId ResidentId, int VersionNumber, BaselineReason Reason,
    BaselineAuthorship CreatedBy, DateTimeOffset CreatedAt, BaselineSignature Signature,
    BaselineVersionId SupersededByVersionId, DateTimeOffset SupersededAt);

/// <summary>Traduce BaselineActivation de baseline.ts: el cambio de versión que el repositorio debe
/// persistir de forma atómica.</summary>
public sealed record BaselineActivation(CurrentBaselineVersion Current, HistoricalBaselineVersion? Previous);

/// <summary>
/// Traduce activateBaselineVersion/assertDraftCanBeActivated de baseline.ts con total fidelidad,
/// incluidas las invariantes de orden temporal, coincidencia de autoría/ámbito entre borrador y firma, y
/// versión estrictamente creciente. Las comprobaciones que en TS son de formato/estructura (IDs opacos,
/// perfil clínico válido, status del borrador) no se traducen porque el sistema de tipos de C# ya las
/// hace estructuralmente imposibles de violar (ClinicalProfessionalProfile, los readonly record struct de
/// ID, y el propio hecho de que BaselineDraft es un tipo distinto de CurrentBaselineVersion).
/// <para>
/// Nota de fidelidad: en el TS original, lib/domain/baseline/baseline.ts existe como motor puro pero
/// db/repositories/baseline-repository.ts NO lo invoca — calcula version_number y persiste de forma
/// inline, sin pasar por activateBaselineVersion. Este puerto conserva esa misma situación: el motor
/// queda disponible como dominio correcto, pero no se fuerza su uso en Infrastructure para no introducir
/// comportamiento nuevo que el prototipo original no tenía cableado.
/// </para>
/// </summary>
public static class BaselineActivator
{
    public static BaselineActivation Activate(BaselineDraft draft, BaselineSignature signature, CurrentBaselineVersion? previous)
    {
        AssertCanActivate(draft, signature, previous);

        var current = new CurrentBaselineVersion(
            draft.Id, draft.ResidentId, draft.VersionNumber, draft.Reason, draft.CreatedBy, draft.CreatedAt, signature);

        if (previous is null)
        {
            return new BaselineActivation(current, null);
        }

        var historical = new HistoricalBaselineVersion(
            previous.Id, previous.ResidentId, previous.VersionNumber, previous.Reason, previous.CreatedBy,
            previous.CreatedAt, previous.Signature, current.Id, signature.SignedAt);

        return new BaselineActivation(current, historical);
    }

    private static void AssertCanActivate(BaselineDraft draft, BaselineSignature signature, CurrentBaselineVersion? previous)
    {
        if (draft.VersionNumber < 1)
        {
            throw new DomainValidationException("BASELINE_VERSION_NUMBER_INVALID");
        }
        if (signature.SignedAt < draft.CreatedAt)
        {
            throw new DomainStateException("BASELINE_SIGNATURE_BEFORE_CREATION");
        }
        if (signature.AccountId != draft.CreatedBy.AccountId || signature.ActiveProfile != draft.CreatedBy.ActiveProfile)
        {
            throw new DomainStateException("BASELINE_SIGNATURE_AUTHOR_MISMATCH");
        }
        if (signature.CenterId != draft.CreatedBy.CenterId || signature.UnitId != draft.CreatedBy.UnitId)
        {
            throw new DomainStateException("BASELINE_SIGNATURE_SCOPE_MISMATCH");
        }
        if (previous is null)
        {
            return;
        }
        if (previous.VersionNumber < 1)
        {
            throw new DomainValidationException("BASELINE_PREVIOUS_VERSION_NUMBER_INVALID");
        }
        if (previous.Signature.SignedAt < previous.CreatedAt)
        {
            throw new DomainStateException("BASELINE_PREVIOUS_SIGNATURE_BEFORE_CREATION");
        }
        if (previous.Signature.CenterId != previous.CreatedBy.CenterId || previous.Signature.UnitId != previous.CreatedBy.UnitId)
        {
            throw new DomainStateException("BASELINE_PREVIOUS_SIGNATURE_SCOPE_MISMATCH");
        }
        if (previous.ResidentId != draft.ResidentId)
        {
            throw new DomainStateException("BASELINE_RESIDENT_MISMATCH");
        }
        if (previous.Id == draft.Id)
        {
            throw new DomainStateException("BASELINE_VERSION_ID_REUSED");
        }
        if (draft.VersionNumber <= previous.VersionNumber)
        {
            throw new DomainStateException("BASELINE_VERSION_NOT_NEWER");
        }
        if (signature.SignedAt <= previous.Signature.SignedAt)
        {
            throw new DomainStateException("BASELINE_SIGNATURE_NOT_AFTER_PREVIOUS");
        }
    }
}
