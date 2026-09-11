import "server-only";

export { authorizeResidentBaseline, denyByDefault, RESIDENT_BASELINE_PERMISSIONS } from "./policy.ts";
export type {
  AuthorizationDecision,
  AuthorizationDenialReason,
  AuthorizationObligation,
  AuthorizationSubject,
  ClinicalDetailAccessPurpose,
  ClinicalDetailAuditObligation,
  ResidentBaselineProfileScope,
  ResidentBaselineAuthorizationRequest,
  ResidentBaselinePermission,
} from "./policy.ts";
