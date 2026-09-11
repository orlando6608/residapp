import "server-only";

import { assertAllBaselineAreasComplete } from "../../lib/domain/baseline/validation.ts";
import { isOpaqueEntityId } from "../../lib/domain/shared/identifiers.ts";
import type { D1DatabaseLike, D1PreparedStatementLike } from "./d1.ts";

const BARTHEL_ITEMS = [
  "COMER",
  "LAVARSE",
  "VESTIRSE",
  "ARREGLARSE",
  "DEPOSICION",
  "MICCION",
  "USO_RETRETE",
  "TRASLADO_CAMA_SILLON",
  "DEAMBULACION",
  "ESCALERAS",
] as const;

type ClinicalProfile = "ENFERMERIA" | "MEDICINA";

export type SignBaselineDraftInput = Readonly<{
  accountId: string;
  activeProfile: ClinicalProfile;
  centerId: string;
  unitId: string;
  residentId: string;
  draftId: string;
  expectedDraftRevision: number;
  operationId: string;
}>;

export type SignBaselineDraftResult = Readonly<{
  baselineVersionId: string;
  versionNumber: number;
}>;

type IdempotencyRow = Readonly<{
  request_hash: string;
  status: string;
  result_json: string | null;
}>;

type DraftRow = Readonly<{
  id: string;
  resident_id: string;
  center_id: string;
  created_in_unit_id: string;
  reason_code: string;
  common_information_source_code: string;
  common_information_source_other_text: string | null;
  common_information_date: string;
  created_by_account_id: string;
  created_by_profile: ClinicalProfile;
  created_at: string;
  draft_revision: number;
}>;

type AreaRow = Readonly<{
  id: string;
  area_code: string;
  catalog_version_code: string;
  answer_payload: string;
  observation: string | null;
  information_source_override_code: string | null;
  information_source_override_other_text: string | null;
  information_date_override: string | null;
  recorded_by_account_id: string;
  recorded_by_profile: ClinicalProfile;
  recorded_at: string;
}>;

type BarthelRow = Readonly<{
  id: string;
  instrument_version_code: string;
  assessment_date: string;
  total_score: number;
  recorded_by_account_id: string;
  recorded_by_profile: ClinicalProfile;
  recorded_at: string;
}>;

type BarthelItemRow = Readonly<{
  item_code: string;
  selected_option_code: string;
  awarded_score: number;
}>;

type CurrentRow = Readonly<{
  baseline_version_id: string;
  version_number: number;
}>;

/**
 * Firma un borrador en un único D1Database.batch(). La identidad y el grant se resuelven
 * de nuevo desde D1; los campos de autoría de la versión se copian del borrador, no del cliente.
 */
export async function signBaselineDraft(
  database: D1DatabaseLike,
  input: SignBaselineDraftInput,
): Promise<SignBaselineDraftResult> {
  assertSignInput(input);
  const requestHash = await hashSignRequest(input);
  const previousOperation = await findIdempotency(database, input, requestHash);
  if (previousOperation) {
    return previousOperation;
  }

  const draft = await loadAuthorizedDraft(database, input);
  if (!draft) {
    throw new Error("BASELINE_SIGN_NOT_AUTHORIZED");
  }

  const areas = await loadAreas(database, draft.id);
  assertAllBaselineAreasComplete(
    areas.map((area) => ({
      areaCode: area.area_code,
      answerPayload: parseJson(area.answer_payload),
    })),
  );

  const barthel = await loadBarthel(database, draft.id);
  const barthelItems = await loadBarthelItems(database, barthel.id);
  assertBarthelComplete(barthel, barthelItems);

  const current = await database
    .prepare(
      `select cb.baseline_version_id, bv.version_number
         from resident_current_baselines cb
         join baseline_versions bv on bv.id = cb.baseline_version_id
        where cb.center_id = ? and cb.resident_id = ?`,
    )
    .bind(input.centerId, input.residentId)
    .first<CurrentRow>();

  const versionNumber = (current?.version_number ?? 0) + 1;
  const versionId = crypto.randomUUID();
  const barthelVersionId = crypto.randomUUID();
  const occurredAt = new Date().toISOString();
  const result: SignBaselineDraftResult = Object.freeze({
    baselineVersionId: versionId,
    versionNumber,
  });

  const statements: D1PreparedStatementLike[] = [
    database
      .prepare(
        `insert into idempotency_operations
          (id, account_id, action_code, operation_id, request_hash, status, created_at)
         values (?, ?, 'BASELINE_SIGN', ?, ?, 'IN_PROGRESS', ?)`,
      )
      .bind(crypto.randomUUID(), input.accountId, input.operationId, requestHash, occurredAt),
    database
      .prepare(
        `insert into baseline_versions
          (id, source_draft_id, resident_id, center_id, created_in_unit_id,
           version_number, reason_code, common_information_source_code,
           common_information_source_other_text, common_information_date,
           created_by_account_id, created_by_profile, created_at,
           signed_by_account_id, signed_by_profile, signed_at, valid_from,
           activation_operation_id)
         values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
      )
      .bind(
        versionId,
        draft.id,
        draft.resident_id,
        draft.center_id,
        draft.created_in_unit_id,
        versionNumber,
        draft.reason_code,
        draft.common_information_source_code,
        draft.common_information_source_other_text,
        draft.common_information_date,
        draft.created_by_account_id,
        draft.created_by_profile,
        draft.created_at,
        draft.created_by_account_id,
        draft.created_by_profile,
        occurredAt,
        occurredAt,
        input.operationId,
      ),
  ];

  for (const area of areas) {
    statements.push(
      database
        .prepare(
          `insert into baseline_version_areas
            (id, baseline_version_id, resident_id, center_id, area_code,
             catalog_version_code, answer_payload, observation,
             information_source_override_code, information_source_override_other_text,
             information_date_override, recorded_by_account_id, recorded_by_profile, recorded_at)
           values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
        )
        .bind(
          crypto.randomUUID(),
          versionId,
          draft.resident_id,
          draft.center_id,
          area.area_code,
          area.catalog_version_code,
          area.answer_payload,
          area.observation,
          area.information_source_override_code,
          area.information_source_override_other_text,
          area.information_date_override,
          area.recorded_by_account_id,
          area.recorded_by_profile,
          area.recorded_at,
        ),
    );
  }

  statements.push(
    database
      .prepare(
        `insert into baseline_version_barthel
          (id, baseline_version_id, resident_id, center_id, instrument_version_code,
           assessment_date, total_score, recorded_by_account_id, recorded_by_profile, recorded_at)
         values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
      )
      .bind(
        barthelVersionId,
        versionId,
        draft.resident_id,
        draft.center_id,
        barthel.instrument_version_code,
        barthel.assessment_date,
        barthel.total_score,
        barthel.recorded_by_account_id,
        barthel.recorded_by_profile,
        barthel.recorded_at,
      ),
  );

  for (const item of barthelItems) {
    statements.push(
      database
        .prepare(
          `insert into baseline_version_barthel_items
            (id, barthel_id, baseline_version_id, resident_id, center_id,
             instrument_version_code, item_code, selected_option_code, awarded_score)
           values (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
        )
        .bind(
          crypto.randomUUID(),
          barthelVersionId,
          versionId,
          draft.resident_id,
          draft.center_id,
          barthel.instrument_version_code,
          item.item_code,
          item.selected_option_code,
          item.awarded_score,
        ),
    );
  }

  if (current) {
    statements.push(
      database
        .prepare(
          `insert into baseline_supersessions
            (previous_version_id, new_version_id, resident_id, center_id, superseded_at)
           values (?, ?, ?, ?, ?)`,
        )
        .bind(
          current.baseline_version_id,
          versionId,
          input.residentId,
          input.centerId,
          occurredAt,
        ),
      database
        .prepare(
          `update resident_current_baselines
              set baseline_version_id = ?, activated_at = ?
            where center_id = ? and resident_id = ? and baseline_version_id = ?`,
        )
        .bind(
          versionId,
          occurredAt,
          input.centerId,
          input.residentId,
          current.baseline_version_id,
        ),
    );
  } else {
    statements.push(
      database
        .prepare(
          `insert into resident_current_baselines
            (resident_id, center_id, baseline_version_id, activated_at)
           values (?, ?, ?, ?)`,
        )
        .bind(input.residentId, input.centerId, versionId, occurredAt),
    );
  }

  statements.push(
    database
      .prepare(
        `update baseline_drafts
            set status = 'SIGNED', updated_by_account_id = ?, updated_by_profile = ?, updated_at = ?
          where id = ? and status = 'ACTIVE' and draft_revision = ?`,
      )
      .bind(
        input.accountId,
        input.activeProfile,
        occurredAt,
        input.draftId,
        input.expectedDraftRevision,
      ),
    database
      .prepare(
        `insert into audit_events
          (id, account_id, active_profile, center_id, unit_id, resident_id,
           resource_type, resource_id, action_code, occurred_at)
         values (?, ?, ?, ?, ?, ?, 'BASELINE_VERSION', ?, 'BASELINE_SIGN', ?)`,
      )
      .bind(
        crypto.randomUUID(),
        input.accountId,
        input.activeProfile,
        input.centerId,
        input.unitId,
        input.residentId,
        versionId,
        occurredAt,
      ),
    database
      .prepare(
        `update idempotency_operations
            set status = 'SUCCEEDED', result_resource_id = ?, result_json = ?, completed_at = ?
          where account_id = ? and action_code = 'BASELINE_SIGN'
            and operation_id = ? and request_hash = ? and status = 'IN_PROGRESS'`,
      )
      .bind(
        versionId,
        JSON.stringify(result),
        occurredAt,
        input.accountId,
        input.operationId,
        requestHash,
      ),
  );

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

async function loadAuthorizedDraft(
  database: D1DatabaseLike,
  input: SignBaselineDraftInput,
): Promise<DraftRow | null> {
  return database
    .prepare(
      `select d.id, d.resident_id, d.center_id, d.created_in_unit_id,
              d.reason_code, d.common_information_source_code,
              d.common_information_source_other_text, d.common_information_date,
              d.created_by_account_id, d.created_by_profile, d.created_at,
              d.draft_revision
         from baseline_drafts d
         join residents r on r.id = d.resident_id and r.center_id = d.center_id
         join resident_location_intervals li
           on li.resident_id = d.resident_id and li.center_id = d.center_id
          and li.valid_until is null
         join accounts a on a.id = ? and a.status = 'ACTIVE'
        where d.id = ? and d.center_id = ? and d.resident_id = ?
          and d.created_in_unit_id = ? and li.unit_id = ?
          and d.status = 'ACTIVE' and d.draft_revision = ?
          and d.created_by_account_id = ? and d.created_by_profile = ?
          and r.status = 'ACTIVE'
          and d.reason_code is not null
          and d.common_information_source_code is not null
          and d.common_information_date is not null
          and exists (
            select 1
              from profile_scopes ps
              join profile_unit_scopes pus
                on pus.profile_scope_id = ps.id and pus.center_id = ps.center_id
               and pus.unit_id = ? and pus.revoked_at is null
              join profile_permissions pp
                on pp.profile_scope_id = ps.id and pp.center_id = ps.center_id
               and pp.revoked_at is null
             where ps.account_id = ? and ps.center_id = ? and ps.profile_code = ?
               and ps.status = 'ACTIVE'
               and pp.permission_code = case
                 when d.reason_code = 'ALTA' then 'BASELINE_INITIAL_COMPLETE'
                 else 'BASELINE_REEVALUATE'
               end
          )`,
    )
    .bind(
      input.accountId,
      input.draftId,
      input.centerId,
      input.residentId,
      input.unitId,
      input.unitId,
      input.expectedDraftRevision,
      input.accountId,
      input.activeProfile,
      input.unitId,
      input.accountId,
      input.centerId,
      input.activeProfile,
    )
    .first<DraftRow>();
}

async function loadAreas(database: D1DatabaseLike, draftId: string): Promise<readonly AreaRow[]> {
  const result = await database
    .prepare(
      `select id, area_code, catalog_version_code, answer_payload, observation,
              information_source_override_code, information_source_override_other_text,
              information_date_override, recorded_by_account_id, recorded_by_profile, recorded_at
         from baseline_draft_areas where draft_id = ? order by area_code`,
    )
    .bind(draftId)
    .all<AreaRow>();
  return result.results ?? [];
}

async function loadBarthel(database: D1DatabaseLike, draftId: string): Promise<BarthelRow> {
  const row = await database
    .prepare(
      `select id, instrument_version_code, assessment_date, total_score,
              recorded_by_account_id, recorded_by_profile, recorded_at
         from baseline_draft_barthel where draft_id = ?`,
    )
    .bind(draftId)
    .first<BarthelRow>();
  if (!row || row.assessment_date === null || row.total_score === null) {
    throw new Error("BASELINE_BARTHEL_INCOMPLETE");
  }
  return row;
}

async function loadBarthelItems(
  database: D1DatabaseLike,
  barthelId: string,
): Promise<readonly BarthelItemRow[]> {
  const result = await database
    .prepare(
      `select item_code, selected_option_code, awarded_score
         from baseline_draft_barthel_items where barthel_id = ? order by item_code`,
    )
    .bind(barthelId)
    .all<BarthelItemRow>();
  return result.results ?? [];
}

function assertBarthelComplete(barthel: BarthelRow, items: readonly BarthelItemRow[]): void {
  const codes = new Set(items.map((item) => item.item_code));
  if (
    barthel.instrument_version_code !== "BARTHEL_COMUN_V0_1" ||
    items.length !== BARTHEL_ITEMS.length ||
    BARTHEL_ITEMS.some((item) => !codes.has(item)) ||
    items.reduce((total, item) => total + item.awarded_score, 0) !== barthel.total_score
  ) {
    throw new Error("BASELINE_BARTHEL_INCOMPLETE");
  }
}

async function findIdempotency(
  database: D1DatabaseLike,
  input: SignBaselineDraftInput,
  requestHash: string,
): Promise<SignBaselineDraftResult | undefined> {
  const row = await database
    .prepare(
      `select request_hash, status, result_json
         from idempotency_operations
        where account_id = ? and action_code = 'BASELINE_SIGN' and operation_id = ?`,
    )
    .bind(input.accountId, input.operationId)
    .first<IdempotencyRow>();
  if (!row) {
    return undefined;
  }
  if (row.request_hash !== requestHash) {
    throw new Error("IDEMPOTENCY_KEY_REUSED");
  }
  if (row.status !== "SUCCEEDED" || !row.result_json) {
    return undefined;
  }
  return Object.freeze(parseJson(row.result_json) as SignBaselineDraftResult);
}

function assertSignInput(input: SignBaselineDraftInput): void {
  const ids = [
    input.accountId,
    input.centerId,
    input.unitId,
    input.residentId,
    input.draftId,
    input.operationId,
  ];
  if (
    ids.some((id) => !isOpaqueEntityId(id)) ||
    (input.activeProfile !== "ENFERMERIA" && input.activeProfile !== "MEDICINA") ||
    !Number.isSafeInteger(input.expectedDraftRevision) ||
    input.expectedDraftRevision < 1
  ) {
    throw new Error("BASELINE_SIGN_INPUT_INVALID");
  }
}

async function hashSignRequest(input: SignBaselineDraftInput): Promise<string> {
  const payload = new TextEncoder().encode(
    JSON.stringify([
      input.accountId,
      input.activeProfile,
      input.centerId,
      input.unitId,
      input.residentId,
      input.draftId,
      input.expectedDraftRevision,
      input.operationId,
    ]),
  );
  const digest = await crypto.subtle.digest("SHA-256", payload);
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, "0")).join("");
}

function parseJson(value: string): unknown {
  try {
    return JSON.parse(value) as unknown;
  } catch {
    throw new Error("PERSISTED_JSON_INVALID");
  }
}
