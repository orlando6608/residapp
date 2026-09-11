import "server-only";

import type { D1DatabaseLike, D1Primitive } from "./d1.ts";

export type AuthorizationSelection = Readonly<{ profileScopeId: string; centerId: string }>;
export type AuthorizationTarget =
  | Readonly<{ kind: "CREATE"; unitId: string }>
  | Readonly<{ kind: "READ"; residentId: string }>
  | Readonly<{ kind: "SIGN"; residentId: string; draftId: string }>;

export type AuthorizationEvidence = Readonly<{
  accountId: string;
  profileScopeId: string;
  profile: string;
  centerId: string;
  unitId: string;
  unitScopeId: string;
  residentId: string | null;
  locationId: string | null;
  residentScopeId: string | null;
  permissions: readonly Readonly<{ id: string; code: string }>[];
  draftReason: string | null;
}>;

export type AuthorizationQuery = Readonly<{ sql: string; values: readonly D1Primitive[] }>;

/** Una proyección relacional por petición. Solo metadatos; no recupera texto clínico. */
export function authorizationQuery(
  externalSubject: string,
  selection: AuthorizationSelection,
  target: AuthorizationTarget,
): AuthorizationQuery {
  const hasResident = target.kind !== "CREATE";
  return Object.freeze({
    sql: `select json_object(
        'accountId', account.id, 'profileScopeId', profile.id,
        'profile', profile.profile_code, 'centerId', profile.center_id,
        'unitId', unit.id, 'unitScopeId', unit_scope.id,
        'residentId', ${hasResident ? "resident.id" : "null"},
        'locationId', ${hasResident ? "location.id" : "null"},
        'residentScopeId', ${hasResident ? "resident_scope.id" : "null"},
        'permissions', json((select json_group_array(json_object('id', id, 'code', permission_code))
          from (select id, permission_code from profile_permissions
            where profile_scope_id = profile.id and center_id = profile.center_id
              and revoked_at is null order by permission_code, id))),
        'draftReason', ${target.kind === "SIGN" ? "draft.reason_code" : "null"}
      ) as evidence
      from accounts account
      join profile_scopes profile on profile.account_id = account.id
        and profile.id = ? and profile.center_id = ?
        and profile.status = 'ACTIVE' and profile.revoked_at is null
      join centers center on center.id = profile.center_id and center.status = 'ACTIVE'
      join profile_unit_scopes unit_scope on unit_scope.profile_scope_id = profile.id
        and unit_scope.center_id = profile.center_id and unit_scope.revoked_at is null
      join units unit on unit.id = unit_scope.unit_id and unit.center_id = profile.center_id
        and unit.status = 'ACTIVE'
      ${hasResident ? `join residents resident on resident.id = ?
        and resident.center_id = profile.center_id and resident.status = 'ACTIVE'
      join resident_location_intervals location on location.resident_id = resident.id
        and location.center_id = profile.center_id and location.unit_id = unit.id
        and location.valid_until is null
      join resident_center_episodes episode on episode.id = location.episode_id
        and episode.center_id = profile.center_id and episode.resident_id = resident.id
        and episode.valid_until is null
      left join profile_resident_scopes resident_scope on resident_scope.profile_scope_id = profile.id
        and resident_scope.center_id = profile.center_id and resident_scope.resident_id = resident.id
        and resident_scope.revoked_at is null` : ""}
      ${target.kind === "SIGN" ? `join baseline_drafts draft on draft.id = ?
        and draft.resident_id = resident.id and draft.center_id = profile.center_id
        and draft.created_in_unit_id = unit.id
        and draft.created_by_account_id = account.id and draft.created_by_profile = profile.profile_code` : ""}
      where account.external_subject = ? and account.status = 'ACTIVE'
        ${hasResident ? `and (resident_scope.id is not null or (
          profile.profile_code in ('ADMINISTRACION', 'ENFERMERIA', 'MEDICINA', 'DIRECCION_CLINICA')
          and not exists (select 1 from profile_resident_scopes restriction
            where restriction.profile_scope_id = profile.id and restriction.center_id = profile.center_id)
        ))` : "and unit.id = ?"}`,
    values: Object.freeze([
      selection.profileScopeId, selection.centerId,
      ...(hasResident ? [target.residentId] : []),
      ...(target.kind === "SIGN" ? [target.draftId] : []),
      externalSubject,
      ...(target.kind === "CREATE" ? [target.unitId] : []),
    ]),
  });
}

export async function loadAuthorizationEvidence(
  database: D1DatabaseLike,
  query: AuthorizationQuery,
): Promise<string | null> {
  const row = await database.prepare(query.sql).bind(...query.values).first<{ evidence: string }>();
  return row?.evidence ?? null;
}
