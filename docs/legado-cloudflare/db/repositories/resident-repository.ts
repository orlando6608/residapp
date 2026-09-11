import "server-only";

import { isDocumentedSexCode } from "../../lib/domain/residents/resident.ts";
import { isOpaqueEntityId } from "../../lib/domain/shared/identifiers.ts";
import type { D1DatabaseLike, D1PreparedStatementLike } from "./d1.ts";

type ResidentCreatorProfile = "ADMINISTRACION" | "ENFERMERIA";

export type CreateResidentInput = Readonly<{
  accountId: string;
  activeProfile: ResidentCreatorProfile;
  centerId: string;
  unitId: string;
  displayName: string;
  birthDate: string;
  documentedSexCode: "male" | "female" | "other" | "unknown";
  internalReference?: string;
  buildingId?: string;
  floorId?: string;
  roomId?: string;
  placeId?: string;
  operationId: string;
}>;

export type CreateResidentResult = Readonly<{
  residentId: string;
  episodeId: string;
  locationIntervalId: string;
}>;

type IdempotencyRow = Readonly<{
  request_hash: string;
  status: string;
  result_json: string | null;
}>;

export async function createResidentWithInitialLocation(
  database: D1DatabaseLike,
  input: CreateResidentInput,
): Promise<CreateResidentResult> {
  assertInput(input);
  const requestHash = await hashRequest(input);
  const previous = await findIdempotency(database, input, requestHash);
  if (previous) {
    return previous;
  }

  const allowed = await database
    .prepare(
      `select 1 as allowed
         from accounts account
         join profile_scopes profile
           on profile.account_id = account.id and profile.center_id = ?
          and profile.profile_code = ? and profile.status = 'ACTIVE'
         join profile_unit_scopes unit_scope
           on unit_scope.profile_scope_id = profile.id
          and unit_scope.center_id = profile.center_id
          and unit_scope.unit_id = ? and unit_scope.revoked_at is null
        where account.id = ? and account.status = 'ACTIVE'
          and (profile.profile_code = 'ADMINISTRACION'
            or (profile.profile_code = 'ENFERMERIA' and exists (
              select 1 from profile_permissions permission
              where permission.profile_scope_id = profile.id
                and permission.center_id = profile.center_id
                and permission.permission_code = 'RESIDENT_IDENTITY_CREATE'
                and permission.revoked_at is null
            )))`,
    )
    .bind(input.centerId, input.activeProfile, input.unitId, input.accountId)
    .first<{ allowed: number }>();
  if (!allowed) {
    throw new Error("RESIDENT_CREATE_NOT_AUTHORIZED");
  }

  const residentId = crypto.randomUUID();
  const episodeId = crypto.randomUUID();
  const locationIntervalId = crypto.randomUUID();
  const occurredAt = new Date().toISOString();
  const result = Object.freeze({ residentId, episodeId, locationIntervalId });
  const statements: D1PreparedStatementLike[] = [
    database
      .prepare(
        `insert into idempotency_operations
          (id, account_id, action_code, operation_id, request_hash, status, created_at)
         values (?, ?, 'RESIDENT_CREATE', ?, ?, 'IN_PROGRESS', ?)`,
      )
      .bind(crypto.randomUUID(), input.accountId, input.operationId, requestHash, occurredAt),
    database
      .prepare(
        `insert into residents
          (id, center_id, display_name, birth_date, documented_sex_code, status,
           created_at, created_by_account_id, created_by_profile)
         values (?, ?, ?, ?, ?, 'ACTIVE', ?, ?, ?)`,
      )
      .bind(
        residentId,
        input.centerId,
        input.displayName.trim(),
        input.birthDate,
        input.documentedSexCode,
        occurredAt,
        input.accountId,
        input.activeProfile,
      ),
    database
      .prepare(
        `insert into resident_center_episodes
          (id, resident_id, center_id, internal_reference, valid_from,
           created_at, created_by_account_id, created_by_profile)
         values (?, ?, ?, ?, ?, ?, ?, ?)`,
      )
      .bind(
        episodeId,
        residentId,
        input.centerId,
        input.internalReference ?? null,
        occurredAt,
        occurredAt,
        input.accountId,
        input.activeProfile,
      ),
    database
      .prepare(
        `insert into resident_location_intervals
          (id, resident_id, center_id, episode_id, unit_id, building_id, floor_id,
           room_id, place_id, valid_from, changed_at, changed_by_account_id, changed_by_profile)
         values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
      )
      .bind(
        locationIntervalId,
        residentId,
        input.centerId,
        episodeId,
        input.unitId,
        input.buildingId ?? null,
        input.floorId ?? null,
        input.roomId ?? null,
        input.placeId ?? null,
        occurredAt,
        occurredAt,
        input.accountId,
        input.activeProfile,
      ),
    database
      .prepare(
        `insert into audit_events
          (id, account_id, active_profile, center_id, unit_id, resident_id,
           resource_type, resource_id, action_code, occurred_at)
         values (?, ?, ?, ?, ?, ?, 'RESIDENT', ?, 'RESIDENT_CREATE', ?)`,
      )
      .bind(
        crypto.randomUUID(),
        input.accountId,
        input.activeProfile,
        input.centerId,
        input.unitId,
        residentId,
        residentId,
        occurredAt,
      ),
    database
      .prepare(
        `update idempotency_operations
            set status = 'SUCCEEDED', result_resource_id = ?, result_json = ?, completed_at = ?
          where account_id = ? and action_code = 'RESIDENT_CREATE'
            and operation_id = ? and request_hash = ? and status = 'IN_PROGRESS'`,
      )
      .bind(
        residentId,
        JSON.stringify(result),
        occurredAt,
        input.accountId,
        input.operationId,
        requestHash,
      ),
  ];

  try {
    await database.batch(statements);
    return result;
  } catch (error) {
    const recovered = await findIdempotency(database, input, requestHash);
    if (recovered) {
      return recovered;
    }
    throw error;
  }
}

async function findIdempotency(
  database: D1DatabaseLike,
  input: CreateResidentInput,
  requestHash: string,
): Promise<CreateResidentResult | undefined> {
  const row = await database
    .prepare(
      `select request_hash, status, result_json from idempotency_operations
        where account_id = ? and action_code = 'RESIDENT_CREATE' and operation_id = ?`,
    )
    .bind(input.accountId, input.operationId)
    .first<IdempotencyRow>();
  if (!row) {
    return undefined;
  }
  if (row.request_hash !== requestHash) {
    throw new Error("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
  }
  if (row.status === "SUCCEEDED" && row.result_json) {
    return JSON.parse(row.result_json) as CreateResidentResult;
  }
  throw new Error("IDEMPOTENCY_OPERATION_IN_PROGRESS");
}

function assertInput(input: CreateResidentInput): void {
  const optionalIds = [input.buildingId, input.floorId, input.roomId, input.placeId];
  if (
    !isOpaqueEntityId(input.accountId) ||
    !isOpaqueEntityId(input.centerId) ||
    !isOpaqueEntityId(input.unitId) ||
    !isOpaqueEntityId(input.operationId) ||
    optionalIds.some((id) => id !== undefined && !isOpaqueEntityId(id)) ||
    (input.activeProfile !== "ADMINISTRACION" && input.activeProfile !== "ENFERMERIA") ||
    !isDocumentedSexCode(input.documentedSexCode) ||
    input.displayName.trim().length === 0 ||
    !/^\d{4}-\d{2}-\d{2}$/u.test(input.birthDate) ||
    Date.parse(`${input.birthDate}T00:00:00.000Z`) > Date.now()
  ) {
    throw new Error("RESIDENT_CREATE_INPUT_INVALID");
  }
}

async function hashRequest(input: CreateResidentInput): Promise<string> {
  const digest = await crypto.subtle.digest(
    "SHA-256",
    new TextEncoder().encode(JSON.stringify(input)),
  );
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, "0")).join("");
}
