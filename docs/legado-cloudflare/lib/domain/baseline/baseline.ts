import type { ClinicalProfessionalProfile } from "../access/profiles.ts";
import type {
  AccountId,
  BaselineVersionId,
  CenterId,
  ResidentId,
  UnitId,
} from "../shared/identifiers.ts";
import { isOpaqueEntityId } from "../shared/identifiers.ts";

export const BASELINE_AREAS = [
  "MOVILIDAD",
  "ALIMENTACION",
  "CONTINENCIA",
  "ASEO_HIGIENE",
  "COGNICION",
  "COMUNICACION",
  "CONDUCTA",
  "SUENO",
  "AYUDAS_HABITUALES",
] as const;

export type BaselineArea = (typeof BASELINE_AREAS)[number];

export const COGNITION_CATEGORIES = [
  "SIN_DETERIORO_CONOCIDO_O_DOCUMENTADO",
  "DETERIORO_COGNITIVO_LEVE_DOCUMENTADO",
  "DEMENCIA_DOCUMENTADA",
  "SITUACION_NO_DETERMINADA",
] as const;

export type CognitionCategory = (typeof COGNITION_CATEGORIES)[number];

export const BASELINE_REASONS = [
  "ALTA",
  "REVISION_PROGRAMADA",
  "CAMBIO_FUNCIONAL_CONSOLIDADO",
] as const;

export type BaselineReason = (typeof BASELINE_REASONS)[number];

export type BaselineAuthorship = Readonly<{
  accountId: AccountId;
  activeProfile: ClinicalProfessionalProfile;
  centerId: CenterId;
  unitId: UnitId;
}>;

type BaselineVersionBase = Readonly<{
  id: BaselineVersionId;
  residentId: ResidentId;
  versionNumber: number;
  reason: BaselineReason;
  createdBy: BaselineAuthorship;
  createdAt: string;
}>;

export type BaselineDraft = BaselineVersionBase &
  Readonly<{
    status: "BORRADOR";
  }>;

export type BaselineSignature = BaselineAuthorship &
  Readonly<{
    signedAt: string;
  }>;

export type CurrentBaselineVersion = BaselineVersionBase &
  Readonly<{
    status: "FIRMADO_VIGENTE";
    signature: BaselineSignature;
  }>;

export type HistoricalBaselineVersion = BaselineVersionBase &
  Readonly<{
    status: "HISTORICO";
    signature: BaselineSignature;
    supersededByVersionId: BaselineVersionId;
    supersededAt: string;
  }>;

export type BaselineVersion = BaselineDraft | CurrentBaselineVersion | HistoricalBaselineVersion;

export type BaselineActivation = Readonly<{
  current: CurrentBaselineVersion;
  previous?: HistoricalBaselineVersion;
}>;

/**
 * Prepara el cambio de versión que el futuro repositorio deberá guardar de forma atómica.
 * La autorización se valida antes, en la política server-side.
 */
export function activateBaselineVersion(
  draft: BaselineDraft,
  signature: BaselineSignature,
  previous?: CurrentBaselineVersion,
): BaselineActivation {
  assertDraftCanBeActivated(draft, signature, previous);

  const current = Object.freeze({
    ...draft,
    createdBy: freezeAuthorship(draft.createdBy),
    status: "FIRMADO_VIGENTE" as const,
    signature: freezeSignature(signature),
  });

  if (!previous) {
    return Object.freeze({ current });
  }

  const historical = Object.freeze({
    ...previous,
    createdBy: freezeAuthorship(previous.createdBy),
    signature: freezeSignature(previous.signature),
    status: "HISTORICO" as const,
    supersededByVersionId: current.id,
    supersededAt: signature.signedAt,
  });

  return Object.freeze({ current, previous: historical });
}

function assertDraftCanBeActivated(
  draft: BaselineDraft,
  signature: BaselineSignature,
  previous?: CurrentBaselineVersion,
): void {
  if (draft.status !== "BORRADOR") {
    throw new Error("BASELINE_TRANSITION_INVALID");
  }

  if (!Number.isSafeInteger(draft.versionNumber) || draft.versionNumber < 1) {
    throw new Error("BASELINE_VERSION_NUMBER_INVALID");
  }

  assertReason(draft.reason);
  assertVersionIdentity(draft);
  assertAuthorship(draft.createdBy);
  assertAuthorship(signature);

  const createdAt = parseTimestamp(draft.createdAt, "BASELINE_CREATED_AT_INVALID");
  const signedAt = parseTimestamp(signature.signedAt, "BASELINE_SIGNED_AT_INVALID");

  if (signedAt < createdAt) {
    throw new Error("BASELINE_SIGNATURE_BEFORE_CREATION");
  }

  if (
    signature.accountId !== draft.createdBy.accountId ||
    signature.activeProfile !== draft.createdBy.activeProfile
  ) {
    throw new Error("BASELINE_SIGNATURE_AUTHOR_MISMATCH");
  }

  if (
    signature.centerId !== draft.createdBy.centerId ||
    signature.unitId !== draft.createdBy.unitId
  ) {
    throw new Error("BASELINE_SIGNATURE_SCOPE_MISMATCH");
  }

  if (previous) {
    if (previous.status !== "FIRMADO_VIGENTE") {
      throw new Error("BASELINE_PREVIOUS_STATUS_INVALID");
    }

    if (!Number.isSafeInteger(previous.versionNumber) || previous.versionNumber < 1) {
      throw new Error("BASELINE_PREVIOUS_VERSION_NUMBER_INVALID");
    }

    assertReason(previous.reason);
    assertVersionIdentity(previous);
    assertAuthorship(previous.createdBy);
    assertAuthorship(previous.signature);

    const previousCreatedAt = parseTimestamp(
      previous.createdAt,
      "BASELINE_PREVIOUS_CREATED_AT_INVALID",
    );
    const previousSignedAt = parseTimestamp(
      previous.signature.signedAt,
      "BASELINE_PREVIOUS_SIGNED_AT_INVALID",
    );

    if (previousSignedAt < previousCreatedAt) {
      throw new Error("BASELINE_PREVIOUS_SIGNATURE_BEFORE_CREATION");
    }

    if (
      previous.signature.centerId !== previous.createdBy.centerId ||
      previous.signature.unitId !== previous.createdBy.unitId
    ) {
      throw new Error("BASELINE_PREVIOUS_SIGNATURE_SCOPE_MISMATCH");
    }

    if (previous.residentId !== draft.residentId) {
      throw new Error("BASELINE_RESIDENT_MISMATCH");
    }

    if (previous.id === draft.id) {
      throw new Error("BASELINE_VERSION_ID_REUSED");
    }

    if (draft.versionNumber <= previous.versionNumber) {
      throw new Error("BASELINE_VERSION_NOT_NEWER");
    }

    if (signedAt <= previousSignedAt) {
      throw new Error("BASELINE_SIGNATURE_NOT_AFTER_PREVIOUS");
    }
  }
}

function assertReason(reason: unknown): asserts reason is BaselineReason {
  if (!(BASELINE_REASONS as readonly unknown[]).includes(reason)) {
    throw new Error("BASELINE_REASON_INVALID");
  }
}

function assertVersionIdentity(version: BaselineVersionBase): void {
  if (!isOpaqueEntityId(version.id) || !isOpaqueEntityId(version.residentId)) {
    throw new Error("BASELINE_IDENTIFIER_INVALID");
  }
}

function assertAuthorship(authorship: BaselineAuthorship): void {
  if (
    !isOpaqueEntityId(authorship.accountId) ||
    !isOpaqueEntityId(authorship.centerId) ||
    !isOpaqueEntityId(authorship.unitId) ||
    (authorship.activeProfile !== "ENFERMERIA" && authorship.activeProfile !== "MEDICINA")
  ) {
    throw new Error("BASELINE_AUTHORSHIP_INVALID");
  }
}

function parseTimestamp(value: string, errorCode: string): number {
  const timestamp = Date.parse(value);

  if (!Number.isFinite(timestamp)) {
    throw new Error(errorCode);
  }

  return timestamp;
}

function freezeAuthorship(authorship: BaselineAuthorship): BaselineAuthorship {
  return Object.freeze({ ...authorship });
}

function freezeSignature(signature: BaselineSignature): BaselineSignature {
  return Object.freeze({ ...signature });
}
