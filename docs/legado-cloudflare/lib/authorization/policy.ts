import "server-only";

import { SYSTEM_PROFILES, type SystemProfile } from "../domain/access/profiles.ts";
import {
  isOpaqueEntityId,
  type AccountId,
  type CenterId,
  type ResidentId,
  type UnitId,
} from "../domain/shared/identifiers.ts";

export const RESIDENT_BASELINE_PERMISSIONS = [
  "RESIDENT_IDENTITY_CREATE",
  "BASELINE_INITIAL_COMPLETE",
  "BASELINE_REEVALUATE",
  "CLINICAL_DETAIL_READ",
] as const;

export type ResidentBaselinePermission = (typeof RESIDENT_BASELINE_PERMISSIONS)[number];

export const CLINICAL_DETAIL_ACCESS_PURPOSES = ["SUPERVISION_CLINICA"] as const;

export type ClinicalDetailAccessPurpose = (typeof CLINICAL_DETAIL_ACCESS_PURPOSES)[number];

export type ClinicalDetailAuditObligation = Readonly<{
  type: "AUDIT_CLINICAL_DETAIL_ACCESS";
  resourceType: "BASELINE_CURRENT" | "BASELINE_HISTORY";
  accountId: AccountId;
  activeProfile: "DIRECCION_CLINICA";
  centerId: CenterId;
  unitId: UnitId;
  residentId: ResidentId;
  purpose: ClinicalDetailAccessPurpose;
}>;

export type AuthorizationObligation = ClinicalDetailAuditObligation;

export type AuthorizationDenialReason =
  | "DENY_BY_DEFAULT"
  | "NOT_AUTHENTICATED"
  | "ACCOUNT_ID_REQUIRED"
  | "ACCOUNT_INACTIVE"
  | "ACTIVE_PROFILE_REQUIRED"
  | "ACTIVE_PROFILE_INVALID"
  | "PROFILE_NOT_ASSIGNED"
  | "CENTER_OUT_OF_SCOPE"
  | "UNIT_OUT_OF_SCOPE"
  | "RESIDENT_OUT_OF_SCOPE"
  | "FAMILY_AUTHORIZATION_REQUIRED"
  | "PERMISSION_REQUIRED"
  | "ACCESS_PURPOSE_REQUIRED"
  | "ACTION_NOT_ALLOWED";

export type AuthorizationDecision =
  | Readonly<{
      allowed: true;
      obligations: readonly AuthorizationObligation[];
    }>
  | Readonly<{
      allowed: false;
      reason: AuthorizationDenialReason;
    }>;

/**
 * Grant relacional para un único perfil, centro y unidad. Mantener residentes y permisos dentro del
 * mismo grant evita formar productos cartesianos o reutilizar permisos de otro perfil/centro.
 */
export type ResidentBaselineProfileScope = Readonly<{
  profile: SystemProfile;
  centerId: CenterId;
  unitId: UnitId;
  residentIds: readonly ResidentId[];
  activeFamilyAuthorizationResidentIds: readonly ResidentId[];
  permissions: readonly ResidentBaselinePermission[];
}>;

/**
 * Instantánea que deberá resolver el servidor desde sesión y repositorio en cada petición.
 * Nunca debe aceptarse este objeto desde un formulario o payload cliente.
 */
export type AuthorizationSubject = Readonly<{
  accountId: AccountId | null;
  authenticated: boolean;
  accountActive: boolean;
  activeProfile: SystemProfile | null;
  assignedProfiles: readonly SystemProfile[];
  profileScopes: readonly ResidentBaselineProfileScope[];
}>;

type CenterUnitResource = Readonly<{
  centerId: CenterId;
  unitId: UnitId;
}>;

type ResidentResource = CenterUnitResource &
  Readonly<{
    residentId: ResidentId;
  }>;

type ResidentBaselineResidentRequest = Readonly<{
  action:
    | "RESIDENT_IDENTITY_READ"
    | "RESIDENT_IDENTITY_UPDATE"
    | "BASELINE_CURRENT_READ"
    | "BASELINE_INITIAL_COMPLETE"
    | "BASELINE_REEVALUATE"
    | "BASELINE_HISTORY_READ";
  subject: AuthorizationSubject;
  resource: ResidentResource;
  purpose?: string;
}>;

export type ResidentBaselineAuthorizationRequest =
  | Readonly<{
      action: "RESIDENT_IDENTITY_CREATE";
      subject: AuthorizationSubject;
      resource: CenterUnitResource;
      purpose?: string;
    }>
  | ResidentBaselineResidentRequest;

const RESIDENT_BASELINE_ACTIONS = [
  "RESIDENT_IDENTITY_CREATE",
  "RESIDENT_IDENTITY_READ",
  "RESIDENT_IDENTITY_UPDATE",
  "BASELINE_CURRENT_READ",
  "BASELINE_INITIAL_COMPLETE",
  "BASELINE_REEVALUATE",
  "BASELINE_HISTORY_READ",
] as const;

const DEFAULT_DENIAL: AuthorizationDecision = Object.freeze({
  allowed: false,
  reason: "DENY_BY_DEFAULT",
});

const NO_OBLIGATIONS = Object.freeze([]) as readonly AuthorizationObligation[];

type AuthorizedContext = Readonly<{
  accountId: AccountId;
  activeProfile: SystemProfile;
  scopes: readonly ResidentBaselineProfileScope[];
}>;

type ContextDecision =
  | Readonly<{ allowed: true; context: AuthorizedContext }>
  | Readonly<{ allowed: false; decision: AuthorizationDecision }>;

/** Punto de partida seguro para cualquier operación sin política funcional específica. */
export function denyByDefault(): AuthorizationDecision {
  return DEFAULT_DENIAL;
}

/**
 * Motor puro de decisión. Acepta `unknown` para que entradas runtime mal formadas fallen cerradas.
 * La confianza en la identidad y los grants debe establecerla una capa server-side anterior.
 */
export function authorizeResidentBaseline(request: unknown): AuthorizationDecision {
  const invalidProfile = invalidActiveProfileDecision(request);

  if (invalidProfile) {
    return invalidProfile;
  }

  if (!isAuthorizationRequest(request)) {
    return denyByDefault();
  }

  const contextDecision = authorizeContext(request);

  if (!contextDecision.allowed) {
    return contextDecision.decision;
  }

  const context = contextDecision.context;
  const profile = context.activeProfile;

  switch (request.action) {
    case "RESIDENT_IDENTITY_CREATE":
      if (profile === "ADMINISTRACION") {
        return allow();
      }

      return profile === "ENFERMERIA" && hasPermission(context, "RESIDENT_IDENTITY_CREATE")
        ? allow()
        : profile === "ENFERMERIA"
          ? deny("PERMISSION_REQUIRED")
          : deny("ACTION_NOT_ALLOWED");

    case "RESIDENT_IDENTITY_READ":
      if (
        profile === "FAMILIAR" &&
        !context.scopes.some((scope) =>
          scope.activeFamilyAuthorizationResidentIds.includes(request.resource.residentId),
        )
      ) {
        return deny("FAMILY_AUTHORIZATION_REQUIRED");
      }

      return allow();

    case "RESIDENT_IDENTITY_UPDATE":
      return profile === "ADMINISTRACION" ? allow() : deny("ACTION_NOT_ALLOWED");

    case "BASELINE_CURRENT_READ":
      if (profile === "AUXILIAR" || profile === "ENFERMERIA" || profile === "MEDICINA") {
        return allow();
      }

      return authorizeClinicalDirectionRead(request, context);

    case "BASELINE_INITIAL_COMPLETE":
      return authorizeProfessionalWrite(context, "BASELINE_INITIAL_COMPLETE");

    case "BASELINE_REEVALUATE":
      return authorizeProfessionalWrite(context, "BASELINE_REEVALUATE");

    case "BASELINE_HISTORY_READ":
      if (profile === "ENFERMERIA" || profile === "MEDICINA") {
        return allow();
      }

      return authorizeClinicalDirectionRead(request, context);

    default:
      return denyByDefault();
  }
}

function authorizeContext(request: ResidentBaselineAuthorizationRequest): ContextDecision {
  const { subject, resource } = request;

  if (!subject.authenticated) {
    return contextDenied("NOT_AUTHENTICATED");
  }

  if (!subject.accountId) {
    return contextDenied("ACCOUNT_ID_REQUIRED");
  }

  if (!subject.accountActive) {
    return contextDenied("ACCOUNT_INACTIVE");
  }

  if (!subject.activeProfile) {
    return contextDenied("ACTIVE_PROFILE_REQUIRED");
  }

  if (!isSystemProfile(subject.activeProfile)) {
    return contextDenied("ACTIVE_PROFILE_INVALID");
  }

  if (!subject.assignedProfiles.includes(subject.activeProfile)) {
    return contextDenied("PROFILE_NOT_ASSIGNED");
  }

  const profileScopes = subject.profileScopes.filter(
    (scope) => scope.profile === subject.activeProfile,
  );
  const centerScopes = profileScopes.filter((scope) => scope.centerId === resource.centerId);

  if (centerScopes.length === 0) {
    return contextDenied("CENTER_OUT_OF_SCOPE");
  }

  const unitScopes = centerScopes.filter((scope) => scope.unitId === resource.unitId);

  if (unitScopes.length === 0) {
    return contextDenied("UNIT_OUT_OF_SCOPE");
  }

  const scopes =
    "residentId" in resource
      ? unitScopes.filter((scope) => scope.residentIds.includes(resource.residentId))
      : unitScopes;

  if ("residentId" in resource && scopes.length === 0) {
    return contextDenied("RESIDENT_OUT_OF_SCOPE");
  }

  return {
    allowed: true,
    context: Object.freeze({
      accountId: subject.accountId,
      activeProfile: subject.activeProfile,
      scopes: Object.freeze(scopes),
    }),
  };
}

function authorizeProfessionalWrite(
  context: AuthorizedContext,
  permission: Extract<
    ResidentBaselinePermission,
    "BASELINE_INITIAL_COMPLETE" | "BASELINE_REEVALUATE"
  >,
): AuthorizationDecision {
  if (context.activeProfile !== "ENFERMERIA" && context.activeProfile !== "MEDICINA") {
    return deny("ACTION_NOT_ALLOWED");
  }

  return hasPermission(context, permission) ? allow() : deny("PERMISSION_REQUIRED");
}

function authorizeClinicalDirectionRead(
  request: ResidentBaselineResidentRequest,
  context: AuthorizedContext,
): AuthorizationDecision {
  if (context.activeProfile !== "DIRECCION_CLINICA") {
    return deny("ACTION_NOT_ALLOWED");
  }

  if (!hasPermission(context, "CLINICAL_DETAIL_READ")) {
    return deny("PERMISSION_REQUIRED");
  }

  if (request.purpose !== "SUPERVISION_CLINICA") {
    return deny("ACCESS_PURPOSE_REQUIRED");
  }

  return allow(
    Object.freeze({
      type: "AUDIT_CLINICAL_DETAIL_ACCESS",
      resourceType:
        request.action === "BASELINE_CURRENT_READ" ? "BASELINE_CURRENT" : "BASELINE_HISTORY",
      accountId: context.accountId,
      activeProfile: context.activeProfile,
      centerId: request.resource.centerId,
      unitId: request.resource.unitId,
      residentId: request.resource.residentId,
      purpose: request.purpose,
    }),
  );
}

function hasPermission(
  context: AuthorizedContext,
  permission: ResidentBaselinePermission,
): boolean {
  return context.scopes.some((scope) => scope.permissions.includes(permission));
}

function allow(...obligations: AuthorizationObligation[]): AuthorizationDecision {
  return Object.freeze({
    allowed: true,
    obligations: obligations.length === 0 ? NO_OBLIGATIONS : Object.freeze(obligations),
  });
}

function deny(reason: AuthorizationDenialReason): AuthorizationDecision {
  return Object.freeze({ allowed: false, reason });
}

function contextDenied(reason: AuthorizationDenialReason): ContextDecision {
  return Object.freeze({ allowed: false, decision: deny(reason) });
}

function invalidActiveProfileDecision(request: unknown): AuthorizationDecision | undefined {
  if (!isRecord(request) || !isRecord(request.subject)) {
    return undefined;
  }

  const activeProfile = request.subject.activeProfile;

  return activeProfile !== null && !isSystemProfile(activeProfile)
    ? deny("ACTIVE_PROFILE_INVALID")
    : undefined;
}

function isAuthorizationRequest(value: unknown): value is ResidentBaselineAuthorizationRequest {
  if (!isRecord(value) || !isResidentBaselineAction(value.action)) {
    return false;
  }

  if (!isAuthorizationSubject(value.subject) || !isRecord(value.resource)) {
    return false;
  }

  if (
    !isOpaqueEntityId(value.resource.centerId) ||
    !isOpaqueEntityId(value.resource.unitId) ||
    (value.purpose !== undefined && typeof value.purpose !== "string")
  ) {
    return false;
  }

  return value.action === "RESIDENT_IDENTITY_CREATE"
    ? true
    : isOpaqueEntityId(value.resource.residentId);
}

function isAuthorizationSubject(value: unknown): value is AuthorizationSubject {
  if (!isRecord(value)) {
    return false;
  }

  if (
    (value.accountId !== null && !isOpaqueEntityId(value.accountId)) ||
    typeof value.authenticated !== "boolean" ||
    typeof value.accountActive !== "boolean" ||
    (value.activeProfile !== null && typeof value.activeProfile !== "string") ||
    !Array.isArray(value.assignedProfiles) ||
    !value.assignedProfiles.every(isSystemProfile) ||
    !Array.isArray(value.profileScopes) ||
    !value.profileScopes.every(isProfileScope)
  ) {
    return false;
  }

  return true;
}

function isProfileScope(value: unknown): value is ResidentBaselineProfileScope {
  if (!isRecord(value)) {
    return false;
  }

  return (
    isSystemProfile(value.profile) &&
    isOpaqueEntityId(value.centerId) &&
    isOpaqueEntityId(value.unitId) &&
    Array.isArray(value.residentIds) &&
    value.residentIds.every(isOpaqueEntityId) &&
    Array.isArray(value.activeFamilyAuthorizationResidentIds) &&
    value.activeFamilyAuthorizationResidentIds.every(isOpaqueEntityId) &&
    Array.isArray(value.permissions) &&
    value.permissions.every(isResidentBaselinePermission)
  );
}

function isSystemProfile(value: unknown): value is SystemProfile {
  return typeof value === "string" && (SYSTEM_PROFILES as readonly string[]).includes(value);
}

function isResidentBaselinePermission(value: unknown): value is ResidentBaselinePermission {
  return (
    typeof value === "string" &&
    (RESIDENT_BASELINE_PERMISSIONS as readonly string[]).includes(value)
  );
}

function isResidentBaselineAction(
  value: unknown,
): value is (typeof RESIDENT_BASELINE_ACTIONS)[number] {
  return (
    typeof value === "string" && (RESIDENT_BASELINE_ACTIONS as readonly string[]).includes(value)
  );
}

function isRecord(value: unknown): value is Record<PropertyKey, unknown> {
  return typeof value === "object" && value !== null;
}
