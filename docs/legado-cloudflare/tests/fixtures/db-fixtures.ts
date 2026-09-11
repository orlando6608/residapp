import type { DatabaseSync } from "node:sqlite";

import type { D1DatabaseLike } from "../../db/repositories/d1.ts";

export const IDS = Object.freeze({
  centerA: "20000000-0000-4000-8000-000000000001",
  centerB: "20000000-0000-4000-8000-000000000002",
  unitA: "30000000-0000-4000-8000-000000000001",
  unitB: "30000000-0000-4000-8000-000000000002",
  residentA: "40000000-0000-4000-8000-000000000001",
  residentB: "40000000-0000-4000-8000-000000000002",
  episodeA: "41000000-0000-4000-8000-000000000001",
  locationA: "42000000-0000-4000-8000-000000000001",
  nurseA: "10000000-0000-4000-8000-000000000001",
  nurseB: "10000000-0000-4000-8000-000000000002",
  direction: "10000000-0000-4000-8000-000000000003",
  admin: "10000000-0000-4000-8000-000000000004",
  draftA: "50000000-0000-4000-8000-000000000001",
  draftB: "50000000-0000-4000-8000-000000000002",
  draftC: "50000000-0000-4000-8000-000000000003",
});

const NOW = "2026-09-06T08:00:00.000Z";

export const VALID_AREA_PAYLOADS = Object.freeze({
  MOVILIDAD: {
    displacementModeCode: "DEAMBULA_INDEPENDIENTE_SIN_AYUDA",
    technicalAidCode: "NINGUNA",
    transferCode: "INDEPENDIENTE",
  },
  ALIMENTACION: {
    routeCode: "ORAL",
    foodTextureCode: "NORMAL",
    liquidConsistencyCode: "IDDSI_0_FINO_SIN_ESPESAR",
    assistanceCode: "INDEPENDIENTE",
    swallowingPrecautionsCode: "NINGUNA_DOCUMENTADA",
  },
  CONTINENCIA: {
    urinationCode: "CONTINENTE",
    bowelCode: "CONTINENTE",
    managementCodes: ["NINGUNO"],
  },
  ASEO_HIGIENE: {
    personalCareAssistanceCode: "INDEPENDIENTE",
    bathingAssistanceCode: "INDEPENDIENTE",
  },
  COGNICION: {
    categoryCode: "SITUACION_NO_DETERMINADA",
    gdsCode: "NO_DOCUMENTADO",
  },
  COMUNICACION: {
    comprehensionCode: "COMPRENSION_FUNCIONAL",
    expressionCode: "EXPRESA_NECESIDADES_EFICAZMENTE",
    usualFormsCodes: ["LENGUAJE_ORAL"],
  },
  CONDUCTA: {
    statusCode: "SIN_CONDUCTAS_RELEVANTES_CONOCIDAS",
    patternCodes: [],
  },
  SUENO: { patternCodes: ["PATRON_HABITUALMENTE_CONSERVADO"] },
  AYUDAS_HABITUALES: { aidCodes: ["NINGUNO"] },
});

const BARTHEL_ZERO = [
  ["COMER", "DEPENDIENTE", 0],
  ["LAVARSE", "NECESITA_AYUDA", 0],
  ["VESTIRSE", "DEPENDIENTE", 0],
  ["ARREGLARSE", "NECESITA_AYUDA", 0],
  ["DEPOSICION", "INCONTINENTE", 0],
  ["MICCION", "INCONTINENTE", 0],
  ["USO_RETRETE", "DEPENDIENTE", 0],
  ["TRASLADO_CAMA_SILLON", "DEPENDIENTE_GRUA_O_DOS_PERSONAS", 0],
  ["DEAMBULACION", "DEPENDIENTE", 0],
  ["ESCALERAS", "DEPENDIENTE", 0],
] as const;

export function seedFoundation(database: DatabaseSync): void {
  for (const statement of foundationStatements()) {
    database.exec(statement);
  }
}

export async function seedFoundationD1(database: D1DatabaseLike): Promise<void> {
  for (const statement of foundationStatements()) {
    await database.prepare(statement).run();
  }
}

function foundationStatements(): readonly string[] {
  return [
    `insert into centers values ('${IDS.centerA}', 'CENTER-A', 'Centro Sintético A', 'ACTIVE', '${NOW}')`,
    `insert into centers values ('${IDS.centerB}', 'CENTER-B', 'Centro Sintético B', 'ACTIVE', '${NOW}')`,
    `insert into center_location_config_versions values ('21000000-0000-4000-8000-000000000001', '${IDS.centerA}', 1, 0, 0, 0, 0, '${NOW}', null, '${NOW}')`,
    `insert into center_location_config_versions values ('21000000-0000-4000-8000-000000000002', '${IDS.centerB}', 1, 0, 0, 0, 0, '${NOW}', null, '${NOW}')`,
    `insert into units (id, center_id, code, display_name, status, created_at) values ('${IDS.unitA}', '${IDS.centerA}', 'UNIT-A', 'Unidad Sintética A', 'ACTIVE', '${NOW}')`,
    `insert into units (id, center_id, code, display_name, status, created_at) values ('${IDS.unitB}', '${IDS.centerB}', 'UNIT-B', 'Unidad Sintética B', 'ACTIVE', '${NOW}')`,
    `insert into accounts values ('${IDS.nurseA}', 'synthetic-nurse-a', 'ACTIVE', '${NOW}')`,
    `insert into accounts values ('${IDS.nurseB}', 'synthetic-nurse-b', 'ACTIVE', '${NOW}')`,
    `insert into accounts values ('${IDS.direction}', 'synthetic-direction', 'ACTIVE', '${NOW}')`,
    `insert into accounts values ('${IDS.admin}', 'synthetic-admin', 'ACTIVE', '${NOW}')`,
    ...profileStatements(IDS.admin, "ADMINISTRACION", []),
    `insert into residents (id, center_id, display_name, birth_date, documented_sex_code, status, created_at, created_by_account_id, created_by_profile) values ('${IDS.residentA}', '${IDS.centerA}', 'Residente Sintético A', '1940-01-01', 'unknown', 'ACTIVE', '${NOW}', '${IDS.admin}', 'ADMINISTRACION')`,
    `insert into resident_center_episodes values ('${IDS.episodeA}', '${IDS.residentA}', '${IDS.centerA}', 'SYN-A-001', '${NOW}', null, '${NOW}', '${IDS.admin}', 'ADMINISTRACION')`,
    `insert into resident_location_intervals (id, resident_id, center_id, episode_id, unit_id, valid_from, valid_until, changed_at, changed_by_account_id, changed_by_profile) values ('${IDS.locationA}', '${IDS.residentA}', '${IDS.centerA}', '${IDS.episodeA}', '${IDS.unitA}', '${NOW}', null, '${NOW}', '${IDS.admin}', 'ADMINISTRACION')`,
    ...profileStatements(IDS.nurseA, "ENFERMERIA", [
      "BASELINE_INITIAL_COMPLETE",
      "BASELINE_REEVALUATE",
      "RESIDENT_IDENTITY_CREATE",
    ]),
    ...profileStatements(IDS.nurseB, "ENFERMERIA", [
      "BASELINE_INITIAL_COMPLETE",
      "BASELINE_REEVALUATE",
      "RESIDENT_IDENTITY_CREATE",
    ]),
    ...profileStatements(IDS.direction, "DIRECCION_CLINICA", ["CLINICAL_DETAIL_READ"]),
  ];
}

export function createCompleteDraft(
  database: DatabaseSync,
  options: Readonly<{
    id?: string;
    creatorAccountId?: string;
    reasonCode?: "ALTA" | "REVISION_PROGRAMADA";
    omitArea?: string;
    omitBarthelItem?: string;
  }> = {},
): string {
  const id = options.id ?? IDS.draftA;
  const creator = options.creatorAccountId ?? IDS.nurseA;
  const reason = options.reasonCode ?? "ALTA";
  database
    .prepare(
      `insert into baseline_drafts
        (id, resident_id, center_id, created_in_unit_id, status, reason_code,
         common_information_source_code, common_information_date,
         created_by_account_id, created_by_profile, created_at,
         updated_by_account_id, updated_by_profile, updated_at, draft_revision)
       values (?, ?, ?, ?, 'ACTIVE', ?, 'VALORACION_DIRECTA', '2026-09-06', ?,
               'ENFERMERIA', ?, ?, 'ENFERMERIA', ?, 1)`,
    )
    .run(id, IDS.residentA, IDS.centerA, IDS.unitA, reason, creator, NOW, creator, NOW);

  let counter = 1;
  for (const [areaCode, payload] of Object.entries(VALID_AREA_PAYLOADS)) {
    if (areaCode === options.omitArea) {
      continue;
    }
    const areaId = `51000000-0000-4000-8000-${String(counter + draftOffset(id)).padStart(12, "0")}`;
    database
      .prepare(
        `insert into baseline_draft_areas
          (id, draft_id, resident_id, center_id, area_code, catalog_version_code,
           answer_payload, recorded_by_account_id, recorded_by_profile, recorded_at)
         values (?, ?, ?, ?, ?, 'BASAL_AREAS_V0_1', ?, ?, 'ENFERMERIA', ?)`,
      )
      .run(
        areaId,
        id,
        IDS.residentA,
        IDS.centerA,
        areaCode,
        JSON.stringify(payload),
        creator,
        NOW,
      );
    counter += 1;
  }

  const barthelId = `52000000-0000-4000-8000-${String(draftOffset(id) / 100 + 1).padStart(12, "0")}`;
  database
    .prepare(
      `insert into baseline_draft_barthel
        (id, draft_id, resident_id, center_id, instrument_version_code,
         assessment_date, total_score, recorded_by_account_id, recorded_by_profile, recorded_at)
       values (?, ?, ?, ?, 'BARTHEL_COMUN_V0_1', '2026-09-06', 0, ?, 'ENFERMERIA', ?)`,
    )
    .run(barthelId, id, IDS.residentA, IDS.centerA, creator, NOW);
  counter = 1;
  for (const [itemCode, optionCode, score] of BARTHEL_ZERO) {
    if (itemCode === options.omitBarthelItem) {
      continue;
    }
    const itemId = `53000000-0000-4000-8000-${String(counter + draftOffset(id)).padStart(12, "0")}`;
    database
      .prepare(
        `insert into baseline_draft_barthel_items
          (id, barthel_id, draft_id, resident_id, center_id, instrument_version_code,
           item_code, selected_option_code, awarded_score)
         values (?, ?, ?, ?, ?, 'BARTHEL_COMUN_V0_1', ?, ?, ?)`,
      )
      .run(itemId, barthelId, id, IDS.residentA, IDS.centerA, itemCode, optionCode, score);
    counter += 1;
  }
  return id;
}

export async function createCompleteDraftD1(
  database: D1DatabaseLike,
  options: Readonly<{
    id?: string;
    creatorAccountId?: string;
    reasonCode?: "ALTA" | "REVISION_PROGRAMADA";
    omitArea?: string;
    omitBarthelItem?: string;
  }> = {},
): Promise<string> {
  const id = options.id ?? IDS.draftA;
  const creator = options.creatorAccountId ?? IDS.nurseA;
  const reason = options.reasonCode ?? "ALTA";
  await database
    .prepare(
      `insert into baseline_drafts
        (id, resident_id, center_id, created_in_unit_id, status, reason_code,
         common_information_source_code, common_information_date,
         created_by_account_id, created_by_profile, created_at,
         updated_by_account_id, updated_by_profile, updated_at, draft_revision)
       values (?, ?, ?, ?, 'ACTIVE', ?, 'VALORACION_DIRECTA', '2026-09-06', ?,
               'ENFERMERIA', ?, ?, 'ENFERMERIA', ?, 1)`,
    )
    .bind(id, IDS.residentA, IDS.centerA, IDS.unitA, reason, creator, NOW, creator, NOW)
    .run();

  let counter = 1;
  for (const [areaCode, payload] of Object.entries(VALID_AREA_PAYLOADS)) {
    if (areaCode !== options.omitArea) {
      const areaId = `51000000-0000-4000-8000-${String(counter + draftOffset(id)).padStart(12, "0")}`;
      await database
        .prepare(
          `insert into baseline_draft_areas
            (id, draft_id, resident_id, center_id, area_code, catalog_version_code,
             answer_payload, recorded_by_account_id, recorded_by_profile, recorded_at)
           values (?, ?, ?, ?, ?, 'BASAL_AREAS_V0_1', ?, ?, 'ENFERMERIA', ?)`,
        )
        .bind(
          areaId,
          id,
          IDS.residentA,
          IDS.centerA,
          areaCode,
          JSON.stringify(payload),
          creator,
          NOW,
        )
        .run();
    }
    counter += 1;
  }

  const barthelId = `52000000-0000-4000-8000-${String(draftOffset(id) / 100 + 1).padStart(12, "0")}`;
  await database
    .prepare(
      `insert into baseline_draft_barthel
        (id, draft_id, resident_id, center_id, instrument_version_code,
         assessment_date, total_score, recorded_by_account_id, recorded_by_profile, recorded_at)
       values (?, ?, ?, ?, 'BARTHEL_COMUN_V0_1', '2026-09-06', 0, ?, 'ENFERMERIA', ?)`,
    )
    .bind(barthelId, id, IDS.residentA, IDS.centerA, creator, NOW)
    .run();
  counter = 1;
  for (const [itemCode, optionCode, score] of BARTHEL_ZERO) {
    if (itemCode !== options.omitBarthelItem) {
      const itemId = `53000000-0000-4000-8000-${String(counter + draftOffset(id)).padStart(12, "0")}`;
      await database
        .prepare(
          `insert into baseline_draft_barthel_items
            (id, barthel_id, draft_id, resident_id, center_id, instrument_version_code,
             item_code, selected_option_code, awarded_score)
           values (?, ?, ?, ?, ?, 'BARTHEL_COMUN_V0_1', ?, ?, ?)`,
        )
        .bind(itemId, barthelId, id, IDS.residentA, IDS.centerA, itemCode, optionCode, score)
        .run();
    }
    counter += 1;
  }
  return id;
}

function draftOffset(id: string): number {
  if (id === IDS.draftA) {
    return 0;
  }
  if (id === IDS.draftB) {
    return 100;
  }
  if (id === IDS.draftC) {
    return 200;
  }
  throw new Error("SYNTHETIC_DRAFT_ID_UNSUPPORTED");
}

function profileStatements(
  accountId: string,
  profile: string,
  permissions: readonly string[],
): readonly string[] {
  const suffix = accountId.slice(-12);
  const scopeId = `60000000-0000-4000-8000-${suffix}`;
  const statements = [
    `insert into profile_scopes
      (id, account_id, center_id, profile_code, status, granted_at, granted_by_account_id)
     values ('${scopeId}', '${accountId}', '${IDS.centerA}', '${profile}', 'ACTIVE', '${NOW}', '${IDS.admin}')`,
    `insert into profile_unit_scopes
      (id, profile_scope_id, center_id, unit_id, granted_at, granted_by_account_id)
     values ('61000000-0000-4000-8000-${suffix}', '${scopeId}', '${IDS.centerA}', '${IDS.unitA}', '${NOW}', '${IDS.admin}')`,
  ];
  let counter = 1;
  for (const permission of permissions) {
    statements.push(
      `insert into profile_permissions
        (id, profile_scope_id, center_id, permission_code, granted_at, granted_by_account_id)
       values ('62000000-0000-4${String(counter).padStart(3, "0")}-8000-${suffix}',
               '${scopeId}', '${IDS.centerA}', '${permission}', '${NOW}', '${IDS.admin}')`,
    );
    counter += 1;
  }
  return statements;
}
