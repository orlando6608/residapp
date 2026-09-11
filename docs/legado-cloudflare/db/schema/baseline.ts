import { sql } from "drizzle-orm";
import {
  check,
  foreignKey,
  index,
  integer,
  primaryKey,
  sqliteTable,
  text,
  uniqueIndex,
} from "drizzle-orm/sqlite-core";

import { accounts, units } from "./organization.ts";
import { residents } from "./residents.ts";

export const barthelCatalogOptions = sqliteTable(
  "barthel_catalog_options",
  {
    instrumentVersionCode: text("instrument_version_code").notNull(),
    itemCode: text("item_code").notNull(),
    optionCode: text("option_code").notNull(),
    awardedScore: integer("awarded_score").notNull(),
  },
  (table) => [
    primaryKey({
      columns: [
        table.instrumentVersionCode,
        table.itemCode,
        table.optionCode,
        table.awardedScore,
      ],
      name: "barthel_catalog_options_pk",
    }),
    check(
      "barthel_catalog_instrument_check",
      sql`${table.instrumentVersionCode} = 'BARTHEL_COMUN_V0_1'`,
    ),
    check(
      "barthel_catalog_item_check",
      sql`${table.itemCode} in ('COMER', 'LAVARSE', 'VESTIRSE', 'ARREGLARSE', 'DEPOSICION', 'MICCION', 'USO_RETRETE', 'TRASLADO_CAMA_SILLON', 'DEAMBULACION', 'ESCALERAS')`,
    ),
    check(
      "barthel_catalog_score_check",
      sql`${table.awardedScore} in (0, 5, 10, 15)`,
    ),
  ],
);

export const baselineDrafts = sqliteTable(
  "baseline_drafts",
  {
    id: text("id").primaryKey(),
    residentId: text("resident_id").notNull(),
    centerId: text("center_id").notNull(),
    createdInUnitId: text("created_in_unit_id").notNull(),
    status: text("status").notNull().default("ACTIVE"),
    reasonCode: text("reason_code"),
    commonInformationSourceCode: text("common_information_source_code"),
    commonInformationSourceOtherText: text("common_information_source_other_text"),
    commonInformationDate: text("common_information_date"),
    createdByAccountId: text("created_by_account_id")
      .notNull()
      .references(() => accounts.id, { onDelete: "restrict" }),
    createdByProfile: text("created_by_profile").notNull(),
    createdAt: text("created_at").notNull(),
    updatedByAccountId: text("updated_by_account_id")
      .notNull()
      .references(() => accounts.id, { onDelete: "restrict" }),
    updatedByProfile: text("updated_by_profile").notNull(),
    updatedAt: text("updated_at").notNull(),
    draftRevision: integer("draft_revision").notNull().default(1),
    cancelledAt: text("cancelled_at"),
    cancelledByAccountId: text("cancelled_by_account_id").references(() => accounts.id, {
      onDelete: "restrict",
    }),
    cancelledByProfile: text("cancelled_by_profile"),
    cancellationReason: text("cancellation_reason"),
  },
  (table) => [
    foreignKey({
      columns: [table.centerId, table.residentId],
      foreignColumns: [residents.centerId, residents.id],
      name: "baseline_drafts_resident_center_fk",
    }).onDelete("restrict"),
    foreignKey({
      columns: [table.centerId, table.createdInUnitId],
      foreignColumns: [units.centerId, units.id],
      name: "baseline_drafts_unit_center_fk",
    }).onDelete("restrict"),
    uniqueIndex("baseline_drafts_scope_unique").on(table.id, table.residentId, table.centerId),
    uniqueIndex("baseline_drafts_active_unique")
      .on(table.residentId)
      .where(sql`${table.status} = 'ACTIVE'`),
    index("baseline_drafts_scope_status_idx").on(
      table.centerId,
      table.createdInUnitId,
      table.residentId,
      table.status,
    ),
    check("baseline_drafts_status_check", sql`${table.status} in ('ACTIVE', 'CANCELLED', 'SIGNED')`),
    check(
      "baseline_drafts_reason_check",
      sql`${table.reasonCode} is null or ${table.reasonCode} in ('ALTA', 'REVISION_PROGRAMADA', 'CAMBIO_FUNCIONAL_CONSOLIDADO')`,
    ),
    check(
      "baseline_drafts_source_check",
      sql`${table.commonInformationSourceCode} is null or ${table.commonInformationSourceCode} in ('VALORACION_DIRECTA', 'HISTORIA_O_INFORME_CLINICO', 'PERSONAL_DEL_CENTRO', 'FAMILIAR_O_CUIDADOR', 'FUENTES_COMBINADAS', 'OTRA', 'NO_DOCUMENTADO')`,
    ),
    check(
      "baseline_drafts_source_other_check",
      sql`(${table.commonInformationSourceCode} = 'OTRA'
          and length(trim(${table.commonInformationSourceOtherText})) > 0)
        or (coalesce(${table.commonInformationSourceCode}, '') <> 'OTRA'
          and coalesce(length(trim(${table.commonInformationSourceOtherText})), 0) = 0)`,
    ),
    check(
      "baseline_drafts_profiles_check",
      sql`${table.createdByProfile} in ('ENFERMERIA', 'MEDICINA')
        and ${table.updatedByProfile} in ('ENFERMERIA', 'MEDICINA')`,
    ),
    check("baseline_drafts_revision_check", sql`${table.draftRevision} >= 1`),
    check(
      "baseline_drafts_cancellation_check",
      sql`(${table.status} <> 'CANCELLED'
          and ${table.cancelledAt} is null
          and ${table.cancelledByAccountId} is null
          and ${table.cancelledByProfile} is null
          and ${table.cancellationReason} is null)
        or (${table.status} = 'CANCELLED'
          and ${table.cancelledAt} is not null
          and ${table.cancelledByAccountId} = ${table.createdByAccountId}
          and ${table.cancelledByProfile} = ${table.createdByProfile}
          and length(trim(${table.cancellationReason})) > 0)`,
    ),
  ],
);

export const baselineDraftAreas = sqliteTable(
  "baseline_draft_areas",
  {
    id: text("id").primaryKey(),
    draftId: text("draft_id").notNull(),
    residentId: text("resident_id").notNull(),
    centerId: text("center_id").notNull(),
    areaCode: text("area_code").notNull(),
    catalogVersionCode: text("catalog_version_code").notNull().default("BASAL_AREAS_V0_1"),
    answerPayload: text("answer_payload").notNull(),
    observation: text("observation"),
    informationSourceOverrideCode: text("information_source_override_code"),
    informationSourceOverrideOtherText: text("information_source_override_other_text"),
    informationDateOverride: text("information_date_override"),
    recordedByAccountId: text("recorded_by_account_id")
      .notNull()
      .references(() => accounts.id, { onDelete: "restrict" }),
    recordedByProfile: text("recorded_by_profile").notNull(),
    recordedAt: text("recorded_at").notNull(),
  },
  (table) => [
    foreignKey({
      columns: [table.draftId, table.residentId, table.centerId],
      foreignColumns: [baselineDrafts.id, baselineDrafts.residentId, baselineDrafts.centerId],
      name: "baseline_draft_areas_draft_scope_fk",
    }).onDelete("restrict"),
    uniqueIndex("baseline_draft_areas_area_unique").on(table.draftId, table.areaCode),
    index("baseline_draft_areas_scope_idx").on(
      table.centerId,
      table.residentId,
      table.draftId,
      table.areaCode,
    ),
    check(
      "baseline_draft_areas_area_check",
      sql`${table.areaCode} in ('MOVILIDAD', 'ALIMENTACION', 'CONTINENCIA', 'ASEO_HIGIENE', 'COGNICION', 'COMUNICACION', 'CONDUCTA', 'SUENO', 'AYUDAS_HABITUALES')`,
    ),
    check(
      "baseline_draft_areas_catalog_check",
      sql`${table.catalogVersionCode} = 'BASAL_AREAS_V0_1'`,
    ),
    check("baseline_draft_areas_json_check", sql`json_valid(${table.answerPayload})`),
    check(
      "baseline_draft_areas_source_check",
      sql`${table.informationSourceOverrideCode} is null or ${table.informationSourceOverrideCode} in ('VALORACION_DIRECTA', 'HISTORIA_O_INFORME_CLINICO', 'PERSONAL_DEL_CENTRO', 'FAMILIAR_O_CUIDADOR', 'FUENTES_COMBINADAS', 'OTRA', 'NO_DOCUMENTADO')`,
    ),
    check(
      "baseline_draft_areas_source_other_check",
      sql`(${table.informationSourceOverrideCode} = 'OTRA'
          and length(trim(${table.informationSourceOverrideOtherText})) > 0)
        or (coalesce(${table.informationSourceOverrideCode}, '') <> 'OTRA'
          and coalesce(length(trim(${table.informationSourceOverrideOtherText})), 0) = 0)`,
    ),
    check(
      "baseline_draft_areas_profile_check",
      sql`${table.recordedByProfile} in ('ENFERMERIA', 'MEDICINA')`,
    ),
    check(
      "baseline_draft_areas_mobility_aid_check",
      sql`${table.areaCode} <> 'MOVILIDAD'
        or json_extract(${table.answerPayload}, '$.technicalAidCode') is null
        or json_extract(${table.answerPayload}, '$.technicalAidCode') in ('NINGUNA', 'BASTON', 'MULETA_O_MULETAS', 'ANDADOR_4_RUEDAS', 'ANDADOR_2_RUEDAS', 'ANDADOR_FIJO_SIN_RUEDAS', 'OTRA', 'NO_DOCUMENTADO')`,
    ),
    check(
      "baseline_draft_areas_mobility_other_check",
      sql`${table.areaCode} <> 'MOVILIDAD'
        or (json_extract(${table.answerPayload}, '$.technicalAidCode') = 'OTRA'
          and length(trim(json_extract(${table.answerPayload}, '$.technicalAidOtherText'))) > 0)
        or (coalesce(json_extract(${table.answerPayload}, '$.technicalAidCode'), '') <> 'OTRA'
          and coalesce(length(trim(json_extract(${table.answerPayload}, '$.technicalAidOtherText'))), 0) = 0)`,
    ),
    check(
      "baseline_draft_areas_feeding_enteral_check",
      sql`${table.areaCode} <> 'ALIMENTACION'
        or ((coalesce(json_extract(${table.answerPayload}, '$.foodTextureCode'), '') <> 'NO_APLICA'
            and coalesce(json_extract(${table.answerPayload}, '$.liquidConsistencyCode'), '') <> 'NO_APLICA')
          or json_extract(${table.answerPayload}, '$.routeCode') = 'ENTERAL')`,
    ),
    check(
      "baseline_draft_areas_feeding_other_check",
      sql`${table.areaCode} <> 'ALIMENTACION'
        or (json_extract(${table.answerPayload}, '$.foodTextureCode') = 'OTRA_TEXTURA_ADAPTADA'
          and length(trim(json_extract(${table.answerPayload}, '$.foodTextureOtherText'))) > 0)
        or (coalesce(json_extract(${table.answerPayload}, '$.foodTextureCode'), '') <> 'OTRA_TEXTURA_ADAPTADA'
          and coalesce(length(trim(json_extract(${table.answerPayload}, '$.foodTextureOtherText'))), 0) = 0)`,
    ),
  ],
);

export const baselineDraftBarthel = sqliteTable(
  "baseline_draft_barthel",
  {
    id: text("id").primaryKey(),
    draftId: text("draft_id").notNull(),
    residentId: text("resident_id").notNull(),
    centerId: text("center_id").notNull(),
    instrumentVersionCode: text("instrument_version_code").notNull().default("BARTHEL_COMUN_V0_1"),
    assessmentDate: text("assessment_date"),
    totalScore: integer("total_score"),
    recordedByAccountId: text("recorded_by_account_id")
      .notNull()
      .references(() => accounts.id, { onDelete: "restrict" }),
    recordedByProfile: text("recorded_by_profile").notNull(),
    recordedAt: text("recorded_at").notNull(),
  },
  (table) => [
    foreignKey({
      columns: [table.draftId, table.residentId, table.centerId],
      foreignColumns: [baselineDrafts.id, baselineDrafts.residentId, baselineDrafts.centerId],
      name: "baseline_draft_barthel_draft_scope_fk",
    }).onDelete("restrict"),
    uniqueIndex("baseline_draft_barthel_draft_unique").on(table.draftId),
    uniqueIndex("baseline_draft_barthel_scope_unique").on(
      table.id,
      table.draftId,
      table.residentId,
      table.centerId,
      table.instrumentVersionCode,
    ),
    check(
      "baseline_draft_barthel_instrument_check",
      sql`${table.instrumentVersionCode} = 'BARTHEL_COMUN_V0_1'`,
    ),
    check(
      "baseline_draft_barthel_total_check",
      sql`${table.totalScore} is null or ${table.totalScore} between 0 and 100`,
    ),
    check(
      "baseline_draft_barthel_profile_check",
      sql`${table.recordedByProfile} in ('ENFERMERIA', 'MEDICINA')`,
    ),
  ],
);

export const baselineDraftBarthelItems = sqliteTable(
  "baseline_draft_barthel_items",
  {
    id: text("id").primaryKey(),
    barthelId: text("barthel_id").notNull(),
    draftId: text("draft_id").notNull(),
    residentId: text("resident_id").notNull(),
    centerId: text("center_id").notNull(),
    instrumentVersionCode: text("instrument_version_code").notNull(),
    itemCode: text("item_code").notNull(),
    selectedOptionCode: text("selected_option_code").notNull(),
    awardedScore: integer("awarded_score").notNull(),
  },
  (table) => [
    foreignKey({
      columns: [
        table.barthelId,
        table.draftId,
        table.residentId,
        table.centerId,
        table.instrumentVersionCode,
      ],
      foreignColumns: [
        baselineDraftBarthel.id,
        baselineDraftBarthel.draftId,
        baselineDraftBarthel.residentId,
        baselineDraftBarthel.centerId,
        baselineDraftBarthel.instrumentVersionCode,
      ],
      name: "baseline_draft_barthel_items_assessment_scope_fk",
    }).onDelete("restrict"),
    foreignKey({
      columns: [
        table.instrumentVersionCode,
        table.itemCode,
        table.selectedOptionCode,
        table.awardedScore,
      ],
      foreignColumns: [
        barthelCatalogOptions.instrumentVersionCode,
        barthelCatalogOptions.itemCode,
        barthelCatalogOptions.optionCode,
        barthelCatalogOptions.awardedScore,
      ],
      name: "baseline_draft_barthel_items_catalog_fk",
    }).onDelete("restrict"),
    uniqueIndex("baseline_draft_barthel_items_item_unique").on(table.barthelId, table.itemCode),
  ],
);

export const baselineDraftContributions = sqliteTable(
  "baseline_draft_contributions",
  {
    id: text("id").primaryKey(),
    draftId: text("draft_id").notNull(),
    residentId: text("resident_id").notNull(),
    centerId: text("center_id").notNull(),
    unitId: text("unit_id").notNull(),
    areaCode: text("area_code").notNull(),
    componentCode: text("component_code").notNull(),
    changePayload: text("change_payload").notNull(),
    accountId: text("account_id")
      .notNull()
      .references(() => accounts.id, { onDelete: "restrict" }),
    activeProfile: text("active_profile").notNull(),
    draftRevision: integer("draft_revision").notNull(),
    contributedAt: text("contributed_at").notNull(),
  },
  (table) => [
    foreignKey({
      columns: [table.draftId, table.residentId, table.centerId],
      foreignColumns: [baselineDrafts.id, baselineDrafts.residentId, baselineDrafts.centerId],
      name: "baseline_draft_contributions_draft_scope_fk",
    }).onDelete("restrict"),
    foreignKey({
      columns: [table.centerId, table.unitId],
      foreignColumns: [units.centerId, units.id],
      name: "baseline_draft_contributions_unit_center_fk",
    }).onDelete("restrict"),
    index("baseline_draft_contributions_scope_idx").on(
      table.centerId,
      table.unitId,
      table.residentId,
      table.draftId,
      table.contributedAt,
    ),
    check(
      "baseline_draft_contributions_area_check",
      sql`${table.areaCode} in ('MOVILIDAD', 'ALIMENTACION', 'CONTINENCIA', 'ASEO_HIGIENE', 'COGNICION', 'COMUNICACION', 'CONDUCTA', 'SUENO', 'AYUDAS_HABITUALES', 'BARTHEL', 'VERSION')`,
    ),
    check("baseline_draft_contributions_json_check", sql`json_valid(${table.changePayload})`),
    check(
      "baseline_draft_contributions_profile_check",
      sql`${table.activeProfile} in ('ENFERMERIA', 'MEDICINA')`,
    ),
  ],
);

export const baselineVersions = sqliteTable(
  "baseline_versions",
  {
    id: text("id").primaryKey(),
    sourceDraftId: text("source_draft_id")
      .notNull()
      .references(() => baselineDrafts.id, { onDelete: "restrict" }),
    residentId: text("resident_id").notNull(),
    centerId: text("center_id").notNull(),
    createdInUnitId: text("created_in_unit_id").notNull(),
    versionNumber: integer("version_number").notNull(),
    reasonCode: text("reason_code").notNull(),
    commonInformationSourceCode: text("common_information_source_code").notNull(),
    commonInformationSourceOtherText: text("common_information_source_other_text"),
    commonInformationDate: text("common_information_date").notNull(),
    createdByAccountId: text("created_by_account_id")
      .notNull()
      .references(() => accounts.id, { onDelete: "restrict" }),
    createdByProfile: text("created_by_profile").notNull(),
    createdAt: text("created_at").notNull(),
    signedByAccountId: text("signed_by_account_id")
      .notNull()
      .references(() => accounts.id, { onDelete: "restrict" }),
    signedByProfile: text("signed_by_profile").notNull(),
    signedAt: text("signed_at").notNull(),
    validFrom: text("valid_from").notNull(),
    activationOperationId: text("activation_operation_id").notNull(),
  },
  (table) => [
    foreignKey({
      columns: [table.centerId, table.residentId],
      foreignColumns: [residents.centerId, residents.id],
      name: "baseline_versions_resident_center_fk",
    }).onDelete("restrict"),
    foreignKey({
      columns: [table.centerId, table.createdInUnitId],
      foreignColumns: [units.centerId, units.id],
      name: "baseline_versions_unit_center_fk",
    }).onDelete("restrict"),
    uniqueIndex("baseline_versions_source_draft_unique").on(table.sourceDraftId),
    uniqueIndex("baseline_versions_resident_number_unique").on(table.residentId, table.versionNumber),
    uniqueIndex("baseline_versions_activation_operation_unique").on(table.activationOperationId),
    uniqueIndex("baseline_versions_scope_unique").on(table.id, table.residentId, table.centerId),
    index("baseline_versions_scope_time_idx").on(
      table.centerId,
      table.residentId,
      table.versionNumber,
      table.signedAt,
    ),
    check("baseline_versions_number_check", sql`${table.versionNumber} >= 1`),
    check(
      "baseline_versions_reason_check",
      sql`${table.reasonCode} in ('ALTA', 'REVISION_PROGRAMADA', 'CAMBIO_FUNCIONAL_CONSOLIDADO')`,
    ),
    check(
      "baseline_versions_source_check",
      sql`${table.commonInformationSourceCode} in ('VALORACION_DIRECTA', 'HISTORIA_O_INFORME_CLINICO', 'PERSONAL_DEL_CENTRO', 'FAMILIAR_O_CUIDADOR', 'FUENTES_COMBINADAS', 'OTRA', 'NO_DOCUMENTADO')`,
    ),
    check(
      "baseline_versions_source_other_check",
      sql`(${table.commonInformationSourceCode} = 'OTRA'
          and length(trim(${table.commonInformationSourceOtherText})) > 0)
        or (${table.commonInformationSourceCode} <> 'OTRA'
          and coalesce(length(trim(${table.commonInformationSourceOtherText})), 0) = 0)`,
    ),
    check(
      "baseline_versions_signer_check",
      sql`${table.createdByAccountId} = ${table.signedByAccountId}
        and ${table.createdByProfile} = ${table.signedByProfile}
        and ${table.signedByProfile} in ('ENFERMERIA', 'MEDICINA')`,
    ),
    check(
      "baseline_versions_time_check",
      sql`${table.signedAt} >= ${table.createdAt} and ${table.validFrom} = ${table.signedAt}`,
    ),
  ],
);

export const baselineVersionAreas = sqliteTable(
  "baseline_version_areas",
  {
    id: text("id").primaryKey(),
    baselineVersionId: text("baseline_version_id").notNull(),
    residentId: text("resident_id").notNull(),
    centerId: text("center_id").notNull(),
    areaCode: text("area_code").notNull(),
    catalogVersionCode: text("catalog_version_code").notNull(),
    answerPayload: text("answer_payload").notNull(),
    observation: text("observation"),
    informationSourceOverrideCode: text("information_source_override_code"),
    informationSourceOverrideOtherText: text("information_source_override_other_text"),
    informationDateOverride: text("information_date_override"),
    recordedByAccountId: text("recorded_by_account_id")
      .notNull()
      .references(() => accounts.id, { onDelete: "restrict" }),
    recordedByProfile: text("recorded_by_profile").notNull(),
    recordedAt: text("recorded_at").notNull(),
  },
  (table) => [
    foreignKey({
      columns: [table.baselineVersionId, table.residentId, table.centerId],
      foreignColumns: [baselineVersions.id, baselineVersions.residentId, baselineVersions.centerId],
      name: "baseline_version_areas_version_scope_fk",
    }).onDelete("restrict"),
    uniqueIndex("baseline_version_areas_area_unique").on(table.baselineVersionId, table.areaCode),
    check(
      "baseline_version_areas_area_check",
      sql`${table.areaCode} in ('MOVILIDAD', 'ALIMENTACION', 'CONTINENCIA', 'ASEO_HIGIENE', 'COGNICION', 'COMUNICACION', 'CONDUCTA', 'SUENO', 'AYUDAS_HABITUALES')`,
    ),
    check(
      "baseline_version_areas_catalog_check",
      sql`${table.catalogVersionCode} = 'BASAL_AREAS_V0_1'`,
    ),
    check("baseline_version_areas_json_check", sql`json_valid(${table.answerPayload})`),
    check(
      "baseline_version_areas_profile_check",
      sql`${table.recordedByProfile} in ('ENFERMERIA', 'MEDICINA')`,
    ),
  ],
);

export const baselineVersionBarthel = sqliteTable(
  "baseline_version_barthel",
  {
    id: text("id").primaryKey(),
    baselineVersionId: text("baseline_version_id").notNull(),
    residentId: text("resident_id").notNull(),
    centerId: text("center_id").notNull(),
    instrumentVersionCode: text("instrument_version_code").notNull(),
    assessmentDate: text("assessment_date").notNull(),
    totalScore: integer("total_score").notNull(),
    recordedByAccountId: text("recorded_by_account_id")
      .notNull()
      .references(() => accounts.id, { onDelete: "restrict" }),
    recordedByProfile: text("recorded_by_profile").notNull(),
    recordedAt: text("recorded_at").notNull(),
  },
  (table) => [
    foreignKey({
      columns: [table.baselineVersionId, table.residentId, table.centerId],
      foreignColumns: [baselineVersions.id, baselineVersions.residentId, baselineVersions.centerId],
      name: "baseline_version_barthel_version_scope_fk",
    }).onDelete("restrict"),
    uniqueIndex("baseline_version_barthel_version_unique").on(table.baselineVersionId),
    uniqueIndex("baseline_version_barthel_scope_unique").on(
      table.id,
      table.baselineVersionId,
      table.residentId,
      table.centerId,
      table.instrumentVersionCode,
    ),
    check(
      "baseline_version_barthel_instrument_check",
      sql`${table.instrumentVersionCode} = 'BARTHEL_COMUN_V0_1'`,
    ),
    check(
      "baseline_version_barthel_total_check",
      sql`${table.totalScore} between 0 and 100`,
    ),
    check(
      "baseline_version_barthel_profile_check",
      sql`${table.recordedByProfile} in ('ENFERMERIA', 'MEDICINA')`,
    ),
  ],
);

export const baselineVersionBarthelItems = sqliteTable(
  "baseline_version_barthel_items",
  {
    id: text("id").primaryKey(),
    barthelId: text("barthel_id").notNull(),
    baselineVersionId: text("baseline_version_id").notNull(),
    residentId: text("resident_id").notNull(),
    centerId: text("center_id").notNull(),
    instrumentVersionCode: text("instrument_version_code").notNull(),
    itemCode: text("item_code").notNull(),
    selectedOptionCode: text("selected_option_code").notNull(),
    awardedScore: integer("awarded_score").notNull(),
  },
  (table) => [
    foreignKey({
      columns: [
        table.barthelId,
        table.baselineVersionId,
        table.residentId,
        table.centerId,
        table.instrumentVersionCode,
      ],
      foreignColumns: [
        baselineVersionBarthel.id,
        baselineVersionBarthel.baselineVersionId,
        baselineVersionBarthel.residentId,
        baselineVersionBarthel.centerId,
        baselineVersionBarthel.instrumentVersionCode,
      ],
      name: "baseline_version_barthel_items_assessment_scope_fk",
    }).onDelete("restrict"),
    foreignKey({
      columns: [
        table.instrumentVersionCode,
        table.itemCode,
        table.selectedOptionCode,
        table.awardedScore,
      ],
      foreignColumns: [
        barthelCatalogOptions.instrumentVersionCode,
        barthelCatalogOptions.itemCode,
        barthelCatalogOptions.optionCode,
        barthelCatalogOptions.awardedScore,
      ],
      name: "baseline_version_barthel_items_catalog_fk",
    }).onDelete("restrict"),
    uniqueIndex("baseline_version_barthel_items_item_unique").on(table.barthelId, table.itemCode),
  ],
);

export const residentCurrentBaselines = sqliteTable(
  "resident_current_baselines",
  {
    residentId: text("resident_id").primaryKey(),
    centerId: text("center_id").notNull(),
    baselineVersionId: text("baseline_version_id").notNull(),
    activatedAt: text("activated_at").notNull(),
  },
  (table) => [
    foreignKey({
      columns: [table.centerId, table.residentId],
      foreignColumns: [residents.centerId, residents.id],
      name: "resident_current_baselines_resident_center_fk",
    }).onDelete("restrict"),
    foreignKey({
      columns: [table.baselineVersionId, table.residentId, table.centerId],
      foreignColumns: [baselineVersions.id, baselineVersions.residentId, baselineVersions.centerId],
      name: "resident_current_baselines_version_scope_fk",
    }).onDelete("restrict"),
    uniqueIndex("resident_current_baselines_version_unique").on(table.baselineVersionId),
    index("resident_current_baselines_scope_idx").on(table.centerId, table.residentId),
  ],
);

export const baselineSupersessions = sqliteTable(
  "baseline_supersessions",
  {
    previousVersionId: text("previous_version_id").primaryKey(),
    newVersionId: text("new_version_id").notNull(),
    residentId: text("resident_id").notNull(),
    centerId: text("center_id").notNull(),
    supersededAt: text("superseded_at").notNull(),
  },
  (table) => [
    foreignKey({
      columns: [table.previousVersionId, table.residentId, table.centerId],
      foreignColumns: [baselineVersions.id, baselineVersions.residentId, baselineVersions.centerId],
      name: "baseline_supersessions_previous_scope_fk",
    }).onDelete("restrict"),
    foreignKey({
      columns: [table.newVersionId, table.residentId, table.centerId],
      foreignColumns: [baselineVersions.id, baselineVersions.residentId, baselineVersions.centerId],
      name: "baseline_supersessions_new_scope_fk",
    }).onDelete("restrict"),
    uniqueIndex("baseline_supersessions_new_unique").on(table.newVersionId),
    index("baseline_supersessions_scope_idx").on(table.centerId, table.residentId, table.supersededAt),
    check(
      "baseline_supersessions_distinct_check",
      sql`${table.previousVersionId} <> ${table.newVersionId}`,
    ),
  ],
);
