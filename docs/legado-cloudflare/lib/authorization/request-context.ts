import "server-only";

import {
  authorizationQuery, loadAuthorizationEvidence,
  type AuthorizationEvidence, type AuthorizationSelection, type AuthorizationTarget,
} from "../../db/repositories/authorization-subject-repository.ts";
import { bindAuthorizedDatabase } from "../../db/repositories/authorized-d1.ts";
import { readBaselineAsClinicalDirection } from "../../db/repositories/audit-repository.ts";
import { signBaselineDraft } from "../../db/repositories/baseline-repository.ts";
import type { D1DatabaseLike } from "../../db/repositories/d1.ts";
import { createResidentWithInitialLocation, type CreateResidentInput } from "../../db/repositories/resident-repository.ts";
import { AccessDeniedError } from "../application/errors.ts";
import { SYSTEM_PROFILES, type SystemProfile } from "../domain/access/profiles.ts";
import {
  isOpaqueEntityId, type AccountId, type CenterId, type ResidentId, type UnitId,
} from "../domain/shared/identifiers.ts";
import type { SessionIdentityProvider } from "../session/session-provider.ts";
import {
  authorizeResidentBaseline, RESIDENT_BASELINE_PERMISSIONS,
  type AuthorizationDecision, type AuthorizationSubject, type ResidentBaselineAuthorizationRequest, type ResidentBaselinePermission,
} from "./server.ts";

type OperationKind = AuthorizationTarget["kind"];
declare const capabilityBrand: unique symbol;

/** La marca no se materializa: la autoridad existe únicamente en el WeakMap privado. */
export type RequestAuthorizationContext<Kind extends OperationKind = OperationKind> = Readonly<{
  [capabilityBrand]: Kind;
}>;

type AuthorizedOperation = Readonly<{
  database: D1DatabaseLike;
  target: AuthorizationTarget;
  action: ResidentBaselineAuthorizationRequest["action"];
  profileScopeId: string;
  subject: AuthorizationSubject & Readonly<{ accountId: AccountId; activeProfile: SystemProfile }>;
  resource: Readonly<{ centerId: CenterId; unitId: UnitId; residentId?: ResidentId }>;
  decision: Extract<AuthorizationDecision, { allowed: true }>;
}>;

const operations = new WeakMap<RequestAuthorizationContext, AuthorizedOperation>();

/** Una resolución nueva por caso de uso; no se cachea la autorización de una sesión. */
export async function resolveRequestContext<Kind extends OperationKind>(input: Readonly<{
  database: D1DatabaseLike;
  session: SessionIdentityProvider;
  selection: AuthorizationSelection;
  target: AuthorizationTarget & Readonly<{ kind: Kind }>;
  resourceType?: "BASELINE_CURRENT" | "BASELINE_HISTORY";
  purpose?: string;
}>): Promise<RequestAuthorizationContext<Kind>> {
  const { database, session } = input;
  // Copia antes del primer await: una selección mutable no puede cambiar la operación.
  const selection = Object.freeze({ ...input.selection });
  const target: AuthorizationTarget = Object.freeze({ ...input.target });
  const purpose = input.purpose;
  const resourceType = input.resourceType;
  const ids = [selection.profileScopeId, selection.centerId,
    target.kind === "CREATE" ? target.unitId : target.residentId,
    ...(target.kind === "SIGN" ? [target.draftId] : [])];
  if (ids.some((id) => !isOpaqueEntityId(id)) || !["CREATE", "SIGN", "READ"].includes(target.kind)) {
    throw new AccessDeniedError();
  }
  const identity = await session.getVerifiedIdentity();
  if (!identity || typeof identity.externalSubject !== "string" || !identity.externalSubject.trim()) {
    throw new AccessDeniedError();
  }
  const query = authorizationQuery(identity.externalSubject, selection, target);
  const evidence = await loadAuthorizationEvidence(database, query);
  if (!evidence) throw new AccessDeniedError();
  const row = JSON.parse(evidence) as AuthorizationEvidence;
  if (!SYSTEM_PROFILES.includes(row.profile as SystemProfile) ||
      [row.accountId, row.centerId, row.unitId].some((id) => !isOpaqueEntityId(id))) {
    throw new AccessDeniedError();
  }
  const profile = row.profile as SystemProfile;
  const permissions = Object.freeze(row.permissions
    .map((permission) => permission.code)
    .filter((code): code is ResidentBaselinePermission =>
      (RESIDENT_BASELINE_PERMISSIONS as readonly string[]).includes(code)));
  const resource = Object.freeze({
    centerId: row.centerId as CenterId, unitId: row.unitId as UnitId,
    ...(row.residentId ? { residentId: row.residentId as ResidentId } : {}),
  });
  const subject = Object.freeze({
    authenticated: true, accountActive: true,
    accountId: row.accountId as AccountId, activeProfile: profile,
    assignedProfiles: Object.freeze([profile]),
    profileScopes: Object.freeze([Object.freeze({
      profile, centerId: resource.centerId, unitId: resource.unitId,
      residentIds: Object.freeze(resource.residentId ? [resource.residentId] : []),
      activeFamilyAuthorizationResidentIds: Object.freeze([]), permissions,
    })]),
  });
  if (target.kind === "READ" && (profile !== "DIRECCION_CLINICA" ||
      (resourceType !== "BASELINE_CURRENT" && resourceType !== "BASELINE_HISTORY"))) {
    throw new AccessDeniedError();
  }
  if (target.kind === "SIGN" && !["ALTA", "REVISION_PROGRAMADA", "CAMBIO_FUNCIONAL_CONSOLIDADO"].includes(row.draftReason ?? "")) {
    throw new AccessDeniedError();
  }
  const action = target.kind === "CREATE" ? "RESIDENT_IDENTITY_CREATE" :
    target.kind === "SIGN" ? (row.draftReason === "ALTA" ? "BASELINE_INITIAL_COMPLETE" : "BASELINE_REEVALUATE") :
    resourceType === "BASELINE_CURRENT" ? "BASELINE_CURRENT_READ" : "BASELINE_HISTORY_READ";
  const decision = authorizeResidentBaseline({ action, subject, resource, purpose });
  if (!decision.allowed) throw new AccessDeniedError();
  const context = Object.freeze(Object.create(null)) as RequestAuthorizationContext<Kind>;
  operations.set(context, Object.freeze({
    database: bindAuthorizedDatabase(database, query, evidence), target, action,
    profileScopeId: row.profileScopeId, subject, resource, decision,
  }));
  return context;
}

function operationFor<Kind extends OperationKind>(context: RequestAuthorizationContext<Kind>, kind: Kind): AuthorizedOperation {
  const operation = operations.get(context);
  if (!operation || operation.target.kind !== kind) throw new AccessDeniedError();
  return operation;
}

type ResidentCreatePayload = Omit<CreateResidentInput, "accountId" | "activeProfile" | "centerId" | "unitId">;

/** Ejecutores cerrados: nunca reciben SQL, callbacks, repositorios ni un ámbito reemplazable. */
export async function executeResidentCreate(context: RequestAuthorizationContext<"CREATE">, payload: ResidentCreatePayload) {
  const { database, subject, resource } = operationFor(context, "CREATE");
  const activeProfile = subject.activeProfile;
  if (activeProfile !== "ADMINISTRACION" && activeProfile !== "ENFERMERIA") throw new AccessDeniedError();
  return createResidentWithInitialLocation(database, {
    accountId: subject.accountId, activeProfile, centerId: resource.centerId, unitId: resource.unitId,
    displayName: payload.displayName, birthDate: payload.birthDate, documentedSexCode: payload.documentedSexCode,
    internalReference: payload.internalReference, buildingId: payload.buildingId, floorId: payload.floorId,
    roomId: payload.roomId, placeId: payload.placeId, operationId: payload.operationId,
  });
}

export async function executeBaselineSign(context: RequestAuthorizationContext<"SIGN">,
  payload: Readonly<{ expectedDraftRevision: number; operationId: string }>) {
  const { database, subject, resource, target } = operationFor(context, "SIGN");
  const activeProfile = subject.activeProfile;
  if (target.kind !== "SIGN" || (activeProfile !== "ENFERMERIA" && activeProfile !== "MEDICINA")) throw new AccessDeniedError();
  return signBaselineDraft(database, {
    accountId: subject.accountId, activeProfile, centerId: resource.centerId, unitId: resource.unitId,
    residentId: target.residentId, draftId: target.draftId,
    expectedDraftRevision: payload.expectedDraftRevision, operationId: payload.operationId,
  });
}

export async function executeDirectionBaselineRead(context: RequestAuthorizationContext<"READ">) {
  const { database, decision } = operationFor(context, "READ");
  const obligation = decision.obligations[0];
  if (!obligation || obligation.type !== "AUDIT_CLINICAL_DETAIL_ACCESS") throw new AccessDeniedError();
  return readBaselineAsClinicalDirection(database, {
    accountId: obligation.accountId, centerId: obligation.centerId, unitId: obligation.unitId,
    residentId: obligation.residentId, resourceType: obligation.resourceType, purposeCode: obligation.purpose,
  });
}
