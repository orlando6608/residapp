CREATE TABLE `audit_events` (
	`id` text PRIMARY KEY NOT NULL,
	`account_id` text NOT NULL,
	`active_profile` text NOT NULL,
	`center_id` text NOT NULL,
	`unit_id` text,
	`resident_id` text,
	`resource_type` text NOT NULL,
	`resource_id` text NOT NULL,
	`action_code` text NOT NULL,
	`purpose_code` text,
	`occurred_at` text NOT NULL,
	FOREIGN KEY (`account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`center_id`) REFERENCES `centers`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`center_id`,`unit_id`) REFERENCES `units`(`center_id`,`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`center_id`,`resident_id`) REFERENCES `residents`(`center_id`,`id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "audit_events_profile_check" CHECK("audit_events"."active_profile" in ('AUXILIAR', 'ENFERMERIA', 'MEDICINA', 'FAMILIAR', 'ADMINISTRACION', 'DIRECCION_CLINICA')),
	CONSTRAINT "audit_events_direction_read_check" CHECK("audit_events"."action_code" <> 'CLINICAL_DETAIL_READ'
        or ("audit_events"."active_profile" = 'DIRECCION_CLINICA'
          and "audit_events"."purpose_code" = 'SUPERVISION_CLINICA'
          and "audit_events"."unit_id" is not null
          and "audit_events"."resident_id" is not null))
);
--> statement-breakpoint
CREATE INDEX `audit_events_scope_time_idx` ON `audit_events` (`center_id`,`unit_id`,`resident_id`,`occurred_at`);--> statement-breakpoint
CREATE TABLE `idempotency_operations` (
	`id` text PRIMARY KEY NOT NULL,
	`account_id` text NOT NULL,
	`action_code` text NOT NULL,
	`operation_id` text NOT NULL,
	`request_hash` text NOT NULL,
	`status` text DEFAULT 'IN_PROGRESS' NOT NULL,
	`result_resource_id` text,
	`result_json` text,
	`created_at` text NOT NULL,
	`completed_at` text,
	FOREIGN KEY (`account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "idempotency_action_check" CHECK("idempotency_operations"."action_code" in ('RESIDENT_CREATE', 'BASELINE_SIGN', 'CLINICAL_DETAIL_READ')),
	CONSTRAINT "idempotency_status_check" CHECK("idempotency_operations"."status" in ('IN_PROGRESS', 'SUCCEEDED')),
	CONSTRAINT "idempotency_result_check" CHECK(("idempotency_operations"."status" = 'IN_PROGRESS'
          and "idempotency_operations"."result_resource_id" is null
          and "idempotency_operations"."result_json" is null
          and "idempotency_operations"."completed_at" is null)
        or ("idempotency_operations"."status" = 'SUCCEEDED'
          and "idempotency_operations"."result_resource_id" is not null
          and "idempotency_operations"."result_json" is not null
          and json_valid("idempotency_operations"."result_json")
          and "idempotency_operations"."completed_at" is not null))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `idempotency_operation_unique` ON `idempotency_operations` (`account_id`,`action_code`,`operation_id`);--> statement-breakpoint
CREATE TABLE `profile_permissions` (
	`id` text PRIMARY KEY NOT NULL,
	`profile_scope_id` text NOT NULL,
	`center_id` text NOT NULL,
	`permission_code` text NOT NULL,
	`granted_at` text NOT NULL,
	`granted_by_account_id` text NOT NULL,
	`revoked_at` text,
	`revoked_by_account_id` text,
	FOREIGN KEY (`granted_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`revoked_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`profile_scope_id`,`center_id`) REFERENCES `profile_scopes`(`id`,`center_id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "profile_permissions_code_check" CHECK("profile_permissions"."permission_code" in ('RESIDENT_IDENTITY_CREATE', 'BASELINE_INITIAL_COMPLETE', 'BASELINE_REEVALUATE', 'BASELINE_DRAFT_CONTRIBUTE', 'CLINICAL_DETAIL_READ')),
	CONSTRAINT "profile_permissions_revocation_check" CHECK(("profile_permissions"."revoked_at" is null and "profile_permissions"."revoked_by_account_id" is null)
        or ("profile_permissions"."revoked_at" is not null and "profile_permissions"."revoked_by_account_id" is not null))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `profile_permissions_active_unique` ON `profile_permissions` (`profile_scope_id`,`permission_code`) WHERE "profile_permissions"."revoked_at" is null;--> statement-breakpoint
CREATE INDEX `profile_permissions_lookup_idx` ON `profile_permissions` (`center_id`,`profile_scope_id`,`permission_code`,`revoked_at`);--> statement-breakpoint
CREATE TABLE `profile_resident_scopes` (
	`id` text PRIMARY KEY NOT NULL,
	`profile_scope_id` text NOT NULL,
	`center_id` text NOT NULL,
	`resident_id` text NOT NULL,
	`granted_at` text NOT NULL,
	`granted_by_account_id` text NOT NULL,
	`revoked_at` text,
	`revoked_by_account_id` text,
	FOREIGN KEY (`granted_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`revoked_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`profile_scope_id`,`center_id`) REFERENCES `profile_scopes`(`id`,`center_id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`center_id`,`resident_id`) REFERENCES `residents`(`center_id`,`id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "profile_resident_scopes_revocation_check" CHECK(("profile_resident_scopes"."revoked_at" is null and "profile_resident_scopes"."revoked_by_account_id" is null)
        or ("profile_resident_scopes"."revoked_at" is not null and "profile_resident_scopes"."revoked_by_account_id" is not null))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `profile_resident_scopes_active_unique` ON `profile_resident_scopes` (`profile_scope_id`,`resident_id`) WHERE "profile_resident_scopes"."revoked_at" is null;--> statement-breakpoint
CREATE INDEX `profile_resident_scopes_lookup_idx` ON `profile_resident_scopes` (`center_id`,`resident_id`,`profile_scope_id`,`revoked_at`);--> statement-breakpoint
CREATE TABLE `profile_scopes` (
	`id` text PRIMARY KEY NOT NULL,
	`account_id` text NOT NULL,
	`center_id` text NOT NULL,
	`profile_code` text NOT NULL,
	`status` text DEFAULT 'ACTIVE' NOT NULL,
	`granted_at` text NOT NULL,
	`granted_by_account_id` text NOT NULL,
	`revoked_at` text,
	`revoked_by_account_id` text,
	FOREIGN KEY (`account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`center_id`) REFERENCES `centers`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`granted_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`revoked_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "profile_scopes_profile_check" CHECK("profile_scopes"."profile_code" in ('AUXILIAR', 'ENFERMERIA', 'MEDICINA', 'FAMILIAR', 'ADMINISTRACION', 'DIRECCION_CLINICA')),
	CONSTRAINT "profile_scopes_status_check" CHECK("profile_scopes"."status" in ('ACTIVE', 'REVOKED')),
	CONSTRAINT "profile_scopes_revocation_check" CHECK(("profile_scopes"."status" = 'ACTIVE' and "profile_scopes"."revoked_at" is null and "profile_scopes"."revoked_by_account_id" is null)
        or ("profile_scopes"."status" = 'REVOKED' and "profile_scopes"."revoked_at" is not null and "profile_scopes"."revoked_by_account_id" is not null))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `profile_scopes_id_center_unique` ON `profile_scopes` (`id`,`center_id`);--> statement-breakpoint
CREATE UNIQUE INDEX `profile_scopes_active_unique` ON `profile_scopes` (`account_id`,`center_id`,`profile_code`) WHERE "profile_scopes"."status" = 'ACTIVE';--> statement-breakpoint
CREATE INDEX `profile_scopes_authorization_lookup_idx` ON `profile_scopes` (`center_id`,`profile_code`,`account_id`,`status`);--> statement-breakpoint
CREATE TABLE `profile_unit_scopes` (
	`id` text PRIMARY KEY NOT NULL,
	`profile_scope_id` text NOT NULL,
	`center_id` text NOT NULL,
	`unit_id` text NOT NULL,
	`granted_at` text NOT NULL,
	`granted_by_account_id` text NOT NULL,
	`revoked_at` text,
	`revoked_by_account_id` text,
	FOREIGN KEY (`granted_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`revoked_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`profile_scope_id`,`center_id`) REFERENCES `profile_scopes`(`id`,`center_id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`center_id`,`unit_id`) REFERENCES `units`(`center_id`,`id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "profile_unit_scopes_revocation_check" CHECK(("profile_unit_scopes"."revoked_at" is null and "profile_unit_scopes"."revoked_by_account_id" is null)
        or ("profile_unit_scopes"."revoked_at" is not null and "profile_unit_scopes"."revoked_by_account_id" is not null))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `profile_unit_scopes_active_unique` ON `profile_unit_scopes` (`profile_scope_id`,`unit_id`) WHERE "profile_unit_scopes"."revoked_at" is null;--> statement-breakpoint
CREATE INDEX `profile_unit_scopes_lookup_idx` ON `profile_unit_scopes` (`center_id`,`unit_id`,`profile_scope_id`,`revoked_at`);--> statement-breakpoint
CREATE TABLE `barthel_catalog_options` (
	`instrument_version_code` text NOT NULL,
	`item_code` text NOT NULL,
	`option_code` text NOT NULL,
	`awarded_score` integer NOT NULL,
	PRIMARY KEY(`instrument_version_code`, `item_code`, `option_code`, `awarded_score`),
	CONSTRAINT "barthel_catalog_instrument_check" CHECK("barthel_catalog_options"."instrument_version_code" = 'BARTHEL_COMUN_V0_1'),
	CONSTRAINT "barthel_catalog_item_check" CHECK("barthel_catalog_options"."item_code" in ('COMER', 'LAVARSE', 'VESTIRSE', 'ARREGLARSE', 'DEPOSICION', 'MICCION', 'USO_RETRETE', 'TRASLADO_CAMA_SILLON', 'DEAMBULACION', 'ESCALERAS')),
	CONSTRAINT "barthel_catalog_score_check" CHECK("barthel_catalog_options"."awarded_score" in (0, 5, 10, 15))
);
--> statement-breakpoint
CREATE TABLE `baseline_draft_areas` (
	`id` text PRIMARY KEY NOT NULL,
	`draft_id` text NOT NULL,
	`resident_id` text NOT NULL,
	`center_id` text NOT NULL,
	`area_code` text NOT NULL,
	`catalog_version_code` text DEFAULT 'BASAL_AREAS_V0_1' NOT NULL,
	`answer_payload` text NOT NULL,
	`observation` text,
	`information_source_override_code` text,
	`information_source_override_other_text` text,
	`information_date_override` text,
	`recorded_by_account_id` text NOT NULL,
	`recorded_by_profile` text NOT NULL,
	`recorded_at` text NOT NULL,
	FOREIGN KEY (`recorded_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`draft_id`,`resident_id`,`center_id`) REFERENCES `baseline_drafts`(`id`,`resident_id`,`center_id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "baseline_draft_areas_area_check" CHECK("baseline_draft_areas"."area_code" in ('MOVILIDAD', 'ALIMENTACION', 'CONTINENCIA', 'ASEO_HIGIENE', 'COGNICION', 'COMUNICACION', 'CONDUCTA', 'SUENO', 'AYUDAS_HABITUALES')),
	CONSTRAINT "baseline_draft_areas_catalog_check" CHECK("baseline_draft_areas"."catalog_version_code" = 'BASAL_AREAS_V0_1'),
	CONSTRAINT "baseline_draft_areas_json_check" CHECK(json_valid("baseline_draft_areas"."answer_payload")),
	CONSTRAINT "baseline_draft_areas_source_check" CHECK("baseline_draft_areas"."information_source_override_code" is null or "baseline_draft_areas"."information_source_override_code" in ('VALORACION_DIRECTA', 'HISTORIA_O_INFORME_CLINICO', 'PERSONAL_DEL_CENTRO', 'FAMILIAR_O_CUIDADOR', 'FUENTES_COMBINADAS', 'OTRA', 'NO_DOCUMENTADO')),
	CONSTRAINT "baseline_draft_areas_source_other_check" CHECK(("baseline_draft_areas"."information_source_override_code" = 'OTRA'
          and length(trim("baseline_draft_areas"."information_source_override_other_text")) > 0)
        or (coalesce("baseline_draft_areas"."information_source_override_code", '') <> 'OTRA'
          and coalesce(length(trim("baseline_draft_areas"."information_source_override_other_text")), 0) = 0)),
	CONSTRAINT "baseline_draft_areas_profile_check" CHECK("baseline_draft_areas"."recorded_by_profile" in ('ENFERMERIA', 'MEDICINA')),
	CONSTRAINT "baseline_draft_areas_mobility_aid_check" CHECK("baseline_draft_areas"."area_code" <> 'MOVILIDAD'
        or json_extract("baseline_draft_areas"."answer_payload", '$.technicalAidCode') is null
        or json_extract("baseline_draft_areas"."answer_payload", '$.technicalAidCode') in ('NINGUNA', 'BASTON', 'MULETA_O_MULETAS', 'ANDADOR_4_RUEDAS', 'ANDADOR_2_RUEDAS', 'ANDADOR_FIJO_SIN_RUEDAS', 'OTRA', 'NO_DOCUMENTADO')),
	CONSTRAINT "baseline_draft_areas_mobility_other_check" CHECK("baseline_draft_areas"."area_code" <> 'MOVILIDAD'
        or (json_extract("baseline_draft_areas"."answer_payload", '$.technicalAidCode') = 'OTRA'
          and length(trim(json_extract("baseline_draft_areas"."answer_payload", '$.technicalAidOtherText'))) > 0)
        or (coalesce(json_extract("baseline_draft_areas"."answer_payload", '$.technicalAidCode'), '') <> 'OTRA'
          and coalesce(length(trim(json_extract("baseline_draft_areas"."answer_payload", '$.technicalAidOtherText'))), 0) = 0)),
	CONSTRAINT "baseline_draft_areas_feeding_enteral_check" CHECK("baseline_draft_areas"."area_code" <> 'ALIMENTACION'
        or ((coalesce(json_extract("baseline_draft_areas"."answer_payload", '$.foodTextureCode'), '') <> 'NO_APLICA'
            and coalesce(json_extract("baseline_draft_areas"."answer_payload", '$.liquidConsistencyCode'), '') <> 'NO_APLICA')
          or json_extract("baseline_draft_areas"."answer_payload", '$.routeCode') = 'ENTERAL')),
	CONSTRAINT "baseline_draft_areas_feeding_other_check" CHECK("baseline_draft_areas"."area_code" <> 'ALIMENTACION'
        or (json_extract("baseline_draft_areas"."answer_payload", '$.foodTextureCode') = 'OTRA_TEXTURA_ADAPTADA'
          and length(trim(json_extract("baseline_draft_areas"."answer_payload", '$.foodTextureOtherText'))) > 0)
        or (coalesce(json_extract("baseline_draft_areas"."answer_payload", '$.foodTextureCode'), '') <> 'OTRA_TEXTURA_ADAPTADA'
          and coalesce(length(trim(json_extract("baseline_draft_areas"."answer_payload", '$.foodTextureOtherText'))), 0) = 0))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `baseline_draft_areas_area_unique` ON `baseline_draft_areas` (`draft_id`,`area_code`);--> statement-breakpoint
CREATE INDEX `baseline_draft_areas_scope_idx` ON `baseline_draft_areas` (`center_id`,`resident_id`,`draft_id`,`area_code`);--> statement-breakpoint
CREATE TABLE `baseline_draft_barthel` (
	`id` text PRIMARY KEY NOT NULL,
	`draft_id` text NOT NULL,
	`resident_id` text NOT NULL,
	`center_id` text NOT NULL,
	`instrument_version_code` text DEFAULT 'BARTHEL_COMUN_V0_1' NOT NULL,
	`assessment_date` text,
	`total_score` integer,
	`recorded_by_account_id` text NOT NULL,
	`recorded_by_profile` text NOT NULL,
	`recorded_at` text NOT NULL,
	FOREIGN KEY (`recorded_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`draft_id`,`resident_id`,`center_id`) REFERENCES `baseline_drafts`(`id`,`resident_id`,`center_id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "baseline_draft_barthel_instrument_check" CHECK("baseline_draft_barthel"."instrument_version_code" = 'BARTHEL_COMUN_V0_1'),
	CONSTRAINT "baseline_draft_barthel_total_check" CHECK("baseline_draft_barthel"."total_score" is null or "baseline_draft_barthel"."total_score" between 0 and 100),
	CONSTRAINT "baseline_draft_barthel_profile_check" CHECK("baseline_draft_barthel"."recorded_by_profile" in ('ENFERMERIA', 'MEDICINA'))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `baseline_draft_barthel_draft_unique` ON `baseline_draft_barthel` (`draft_id`);--> statement-breakpoint
CREATE UNIQUE INDEX `baseline_draft_barthel_scope_unique` ON `baseline_draft_barthel` (`id`,`draft_id`,`resident_id`,`center_id`,`instrument_version_code`);--> statement-breakpoint
CREATE TABLE `baseline_draft_barthel_items` (
	`id` text PRIMARY KEY NOT NULL,
	`barthel_id` text NOT NULL,
	`draft_id` text NOT NULL,
	`resident_id` text NOT NULL,
	`center_id` text NOT NULL,
	`instrument_version_code` text NOT NULL,
	`item_code` text NOT NULL,
	`selected_option_code` text NOT NULL,
	`awarded_score` integer NOT NULL,
	FOREIGN KEY (`barthel_id`,`draft_id`,`resident_id`,`center_id`,`instrument_version_code`) REFERENCES `baseline_draft_barthel`(`id`,`draft_id`,`resident_id`,`center_id`,`instrument_version_code`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`instrument_version_code`,`item_code`,`selected_option_code`,`awarded_score`) REFERENCES `barthel_catalog_options`(`instrument_version_code`,`item_code`,`option_code`,`awarded_score`) ON UPDATE no action ON DELETE restrict
);
--> statement-breakpoint
CREATE UNIQUE INDEX `baseline_draft_barthel_items_item_unique` ON `baseline_draft_barthel_items` (`barthel_id`,`item_code`);--> statement-breakpoint
CREATE TABLE `baseline_draft_contributions` (
	`id` text PRIMARY KEY NOT NULL,
	`draft_id` text NOT NULL,
	`resident_id` text NOT NULL,
	`center_id` text NOT NULL,
	`unit_id` text NOT NULL,
	`area_code` text NOT NULL,
	`component_code` text NOT NULL,
	`change_payload` text NOT NULL,
	`account_id` text NOT NULL,
	`active_profile` text NOT NULL,
	`draft_revision` integer NOT NULL,
	`contributed_at` text NOT NULL,
	FOREIGN KEY (`account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`draft_id`,`resident_id`,`center_id`) REFERENCES `baseline_drafts`(`id`,`resident_id`,`center_id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`center_id`,`unit_id`) REFERENCES `units`(`center_id`,`id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "baseline_draft_contributions_area_check" CHECK("baseline_draft_contributions"."area_code" in ('MOVILIDAD', 'ALIMENTACION', 'CONTINENCIA', 'ASEO_HIGIENE', 'COGNICION', 'COMUNICACION', 'CONDUCTA', 'SUENO', 'AYUDAS_HABITUALES', 'BARTHEL', 'VERSION')),
	CONSTRAINT "baseline_draft_contributions_json_check" CHECK(json_valid("baseline_draft_contributions"."change_payload")),
	CONSTRAINT "baseline_draft_contributions_profile_check" CHECK("baseline_draft_contributions"."active_profile" in ('ENFERMERIA', 'MEDICINA'))
);
--> statement-breakpoint
CREATE INDEX `baseline_draft_contributions_scope_idx` ON `baseline_draft_contributions` (`center_id`,`unit_id`,`resident_id`,`draft_id`,`contributed_at`);--> statement-breakpoint
CREATE TABLE `baseline_drafts` (
	`id` text PRIMARY KEY NOT NULL,
	`resident_id` text NOT NULL,
	`center_id` text NOT NULL,
	`created_in_unit_id` text NOT NULL,
	`status` text DEFAULT 'ACTIVE' NOT NULL,
	`reason_code` text,
	`common_information_source_code` text,
	`common_information_source_other_text` text,
	`common_information_date` text,
	`created_by_account_id` text NOT NULL,
	`created_by_profile` text NOT NULL,
	`created_at` text NOT NULL,
	`updated_by_account_id` text NOT NULL,
	`updated_by_profile` text NOT NULL,
	`updated_at` text NOT NULL,
	`draft_revision` integer DEFAULT 1 NOT NULL,
	`cancelled_at` text,
	`cancelled_by_account_id` text,
	`cancelled_by_profile` text,
	`cancellation_reason` text,
	FOREIGN KEY (`created_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`updated_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`cancelled_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`center_id`,`resident_id`) REFERENCES `residents`(`center_id`,`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`center_id`,`created_in_unit_id`) REFERENCES `units`(`center_id`,`id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "baseline_drafts_status_check" CHECK("baseline_drafts"."status" in ('ACTIVE', 'CANCELLED', 'SIGNED')),
	CONSTRAINT "baseline_drafts_reason_check" CHECK("baseline_drafts"."reason_code" is null or "baseline_drafts"."reason_code" in ('ALTA', 'REVISION_PROGRAMADA', 'CAMBIO_FUNCIONAL_CONSOLIDADO')),
	CONSTRAINT "baseline_drafts_source_check" CHECK("baseline_drafts"."common_information_source_code" is null or "baseline_drafts"."common_information_source_code" in ('VALORACION_DIRECTA', 'HISTORIA_O_INFORME_CLINICO', 'PERSONAL_DEL_CENTRO', 'FAMILIAR_O_CUIDADOR', 'FUENTES_COMBINADAS', 'OTRA', 'NO_DOCUMENTADO')),
	CONSTRAINT "baseline_drafts_source_other_check" CHECK(("baseline_drafts"."common_information_source_code" = 'OTRA'
          and length(trim("baseline_drafts"."common_information_source_other_text")) > 0)
        or (coalesce("baseline_drafts"."common_information_source_code", '') <> 'OTRA'
          and coalesce(length(trim("baseline_drafts"."common_information_source_other_text")), 0) = 0)),
	CONSTRAINT "baseline_drafts_profiles_check" CHECK("baseline_drafts"."created_by_profile" in ('ENFERMERIA', 'MEDICINA')
        and "baseline_drafts"."updated_by_profile" in ('ENFERMERIA', 'MEDICINA')),
	CONSTRAINT "baseline_drafts_revision_check" CHECK("baseline_drafts"."draft_revision" >= 1),
	CONSTRAINT "baseline_drafts_cancellation_check" CHECK(("baseline_drafts"."status" <> 'CANCELLED'
          and "baseline_drafts"."cancelled_at" is null
          and "baseline_drafts"."cancelled_by_account_id" is null
          and "baseline_drafts"."cancelled_by_profile" is null
          and "baseline_drafts"."cancellation_reason" is null)
        or ("baseline_drafts"."status" = 'CANCELLED'
          and "baseline_drafts"."cancelled_at" is not null
          and "baseline_drafts"."cancelled_by_account_id" = "baseline_drafts"."created_by_account_id"
          and "baseline_drafts"."cancelled_by_profile" = "baseline_drafts"."created_by_profile"
          and length(trim("baseline_drafts"."cancellation_reason")) > 0))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `baseline_drafts_scope_unique` ON `baseline_drafts` (`id`,`resident_id`,`center_id`);--> statement-breakpoint
CREATE UNIQUE INDEX `baseline_drafts_active_unique` ON `baseline_drafts` (`resident_id`) WHERE "baseline_drafts"."status" = 'ACTIVE';--> statement-breakpoint
CREATE INDEX `baseline_drafts_scope_status_idx` ON `baseline_drafts` (`center_id`,`created_in_unit_id`,`resident_id`,`status`);--> statement-breakpoint
CREATE TABLE `baseline_supersessions` (
	`previous_version_id` text PRIMARY KEY NOT NULL,
	`new_version_id` text NOT NULL,
	`resident_id` text NOT NULL,
	`center_id` text NOT NULL,
	`superseded_at` text NOT NULL,
	FOREIGN KEY (`previous_version_id`,`resident_id`,`center_id`) REFERENCES `baseline_versions`(`id`,`resident_id`,`center_id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`new_version_id`,`resident_id`,`center_id`) REFERENCES `baseline_versions`(`id`,`resident_id`,`center_id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "baseline_supersessions_distinct_check" CHECK("baseline_supersessions"."previous_version_id" <> "baseline_supersessions"."new_version_id")
);
--> statement-breakpoint
CREATE UNIQUE INDEX `baseline_supersessions_new_unique` ON `baseline_supersessions` (`new_version_id`);--> statement-breakpoint
CREATE INDEX `baseline_supersessions_scope_idx` ON `baseline_supersessions` (`center_id`,`resident_id`,`superseded_at`);--> statement-breakpoint
CREATE TABLE `baseline_version_areas` (
	`id` text PRIMARY KEY NOT NULL,
	`baseline_version_id` text NOT NULL,
	`resident_id` text NOT NULL,
	`center_id` text NOT NULL,
	`area_code` text NOT NULL,
	`catalog_version_code` text NOT NULL,
	`answer_payload` text NOT NULL,
	`observation` text,
	`information_source_override_code` text,
	`information_source_override_other_text` text,
	`information_date_override` text,
	`recorded_by_account_id` text NOT NULL,
	`recorded_by_profile` text NOT NULL,
	`recorded_at` text NOT NULL,
	FOREIGN KEY (`recorded_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`baseline_version_id`,`resident_id`,`center_id`) REFERENCES `baseline_versions`(`id`,`resident_id`,`center_id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "baseline_version_areas_area_check" CHECK("baseline_version_areas"."area_code" in ('MOVILIDAD', 'ALIMENTACION', 'CONTINENCIA', 'ASEO_HIGIENE', 'COGNICION', 'COMUNICACION', 'CONDUCTA', 'SUENO', 'AYUDAS_HABITUALES')),
	CONSTRAINT "baseline_version_areas_catalog_check" CHECK("baseline_version_areas"."catalog_version_code" = 'BASAL_AREAS_V0_1'),
	CONSTRAINT "baseline_version_areas_json_check" CHECK(json_valid("baseline_version_areas"."answer_payload")),
	CONSTRAINT "baseline_version_areas_profile_check" CHECK("baseline_version_areas"."recorded_by_profile" in ('ENFERMERIA', 'MEDICINA'))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `baseline_version_areas_area_unique` ON `baseline_version_areas` (`baseline_version_id`,`area_code`);--> statement-breakpoint
CREATE TABLE `baseline_version_barthel` (
	`id` text PRIMARY KEY NOT NULL,
	`baseline_version_id` text NOT NULL,
	`resident_id` text NOT NULL,
	`center_id` text NOT NULL,
	`instrument_version_code` text NOT NULL,
	`assessment_date` text NOT NULL,
	`total_score` integer NOT NULL,
	`recorded_by_account_id` text NOT NULL,
	`recorded_by_profile` text NOT NULL,
	`recorded_at` text NOT NULL,
	FOREIGN KEY (`recorded_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`baseline_version_id`,`resident_id`,`center_id`) REFERENCES `baseline_versions`(`id`,`resident_id`,`center_id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "baseline_version_barthel_instrument_check" CHECK("baseline_version_barthel"."instrument_version_code" = 'BARTHEL_COMUN_V0_1'),
	CONSTRAINT "baseline_version_barthel_total_check" CHECK("baseline_version_barthel"."total_score" between 0 and 100),
	CONSTRAINT "baseline_version_barthel_profile_check" CHECK("baseline_version_barthel"."recorded_by_profile" in ('ENFERMERIA', 'MEDICINA'))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `baseline_version_barthel_version_unique` ON `baseline_version_barthel` (`baseline_version_id`);--> statement-breakpoint
CREATE UNIQUE INDEX `baseline_version_barthel_scope_unique` ON `baseline_version_barthel` (`id`,`baseline_version_id`,`resident_id`,`center_id`,`instrument_version_code`);--> statement-breakpoint
CREATE TABLE `baseline_version_barthel_items` (
	`id` text PRIMARY KEY NOT NULL,
	`barthel_id` text NOT NULL,
	`baseline_version_id` text NOT NULL,
	`resident_id` text NOT NULL,
	`center_id` text NOT NULL,
	`instrument_version_code` text NOT NULL,
	`item_code` text NOT NULL,
	`selected_option_code` text NOT NULL,
	`awarded_score` integer NOT NULL,
	FOREIGN KEY (`barthel_id`,`baseline_version_id`,`resident_id`,`center_id`,`instrument_version_code`) REFERENCES `baseline_version_barthel`(`id`,`baseline_version_id`,`resident_id`,`center_id`,`instrument_version_code`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`instrument_version_code`,`item_code`,`selected_option_code`,`awarded_score`) REFERENCES `barthel_catalog_options`(`instrument_version_code`,`item_code`,`option_code`,`awarded_score`) ON UPDATE no action ON DELETE restrict
);
--> statement-breakpoint
CREATE UNIQUE INDEX `baseline_version_barthel_items_item_unique` ON `baseline_version_barthel_items` (`barthel_id`,`item_code`);--> statement-breakpoint
CREATE TABLE `baseline_versions` (
	`id` text PRIMARY KEY NOT NULL,
	`source_draft_id` text NOT NULL,
	`resident_id` text NOT NULL,
	`center_id` text NOT NULL,
	`created_in_unit_id` text NOT NULL,
	`version_number` integer NOT NULL,
	`reason_code` text NOT NULL,
	`common_information_source_code` text NOT NULL,
	`common_information_source_other_text` text,
	`common_information_date` text NOT NULL,
	`created_by_account_id` text NOT NULL,
	`created_by_profile` text NOT NULL,
	`created_at` text NOT NULL,
	`signed_by_account_id` text NOT NULL,
	`signed_by_profile` text NOT NULL,
	`signed_at` text NOT NULL,
	`valid_from` text NOT NULL,
	`activation_operation_id` text NOT NULL,
	FOREIGN KEY (`source_draft_id`) REFERENCES `baseline_drafts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`created_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`signed_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`center_id`,`resident_id`) REFERENCES `residents`(`center_id`,`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`center_id`,`created_in_unit_id`) REFERENCES `units`(`center_id`,`id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "baseline_versions_number_check" CHECK("baseline_versions"."version_number" >= 1),
	CONSTRAINT "baseline_versions_reason_check" CHECK("baseline_versions"."reason_code" in ('ALTA', 'REVISION_PROGRAMADA', 'CAMBIO_FUNCIONAL_CONSOLIDADO')),
	CONSTRAINT "baseline_versions_source_check" CHECK("baseline_versions"."common_information_source_code" in ('VALORACION_DIRECTA', 'HISTORIA_O_INFORME_CLINICO', 'PERSONAL_DEL_CENTRO', 'FAMILIAR_O_CUIDADOR', 'FUENTES_COMBINADAS', 'OTRA', 'NO_DOCUMENTADO')),
	CONSTRAINT "baseline_versions_source_other_check" CHECK(("baseline_versions"."common_information_source_code" = 'OTRA'
          and length(trim("baseline_versions"."common_information_source_other_text")) > 0)
        or ("baseline_versions"."common_information_source_code" <> 'OTRA'
          and coalesce(length(trim("baseline_versions"."common_information_source_other_text")), 0) = 0)),
	CONSTRAINT "baseline_versions_signer_check" CHECK("baseline_versions"."created_by_account_id" = "baseline_versions"."signed_by_account_id"
        and "baseline_versions"."created_by_profile" = "baseline_versions"."signed_by_profile"
        and "baseline_versions"."signed_by_profile" in ('ENFERMERIA', 'MEDICINA')),
	CONSTRAINT "baseline_versions_time_check" CHECK("baseline_versions"."signed_at" >= "baseline_versions"."created_at" and "baseline_versions"."valid_from" = "baseline_versions"."signed_at")
);
--> statement-breakpoint
CREATE UNIQUE INDEX `baseline_versions_source_draft_unique` ON `baseline_versions` (`source_draft_id`);--> statement-breakpoint
CREATE UNIQUE INDEX `baseline_versions_resident_number_unique` ON `baseline_versions` (`resident_id`,`version_number`);--> statement-breakpoint
CREATE UNIQUE INDEX `baseline_versions_activation_operation_unique` ON `baseline_versions` (`activation_operation_id`);--> statement-breakpoint
CREATE UNIQUE INDEX `baseline_versions_scope_unique` ON `baseline_versions` (`id`,`resident_id`,`center_id`);--> statement-breakpoint
CREATE INDEX `baseline_versions_scope_time_idx` ON `baseline_versions` (`center_id`,`resident_id`,`version_number`,`signed_at`);--> statement-breakpoint
CREATE TABLE `resident_current_baselines` (
	`resident_id` text PRIMARY KEY NOT NULL,
	`center_id` text NOT NULL,
	`baseline_version_id` text NOT NULL,
	`activated_at` text NOT NULL,
	FOREIGN KEY (`center_id`,`resident_id`) REFERENCES `residents`(`center_id`,`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`baseline_version_id`,`resident_id`,`center_id`) REFERENCES `baseline_versions`(`id`,`resident_id`,`center_id`) ON UPDATE no action ON DELETE restrict
);
--> statement-breakpoint
CREATE UNIQUE INDEX `resident_current_baselines_version_unique` ON `resident_current_baselines` (`baseline_version_id`);--> statement-breakpoint
CREATE INDEX `resident_current_baselines_scope_idx` ON `resident_current_baselines` (`center_id`,`resident_id`);--> statement-breakpoint
CREATE TABLE `accounts` (
	`id` text PRIMARY KEY NOT NULL,
	`external_subject` text NOT NULL,
	`status` text DEFAULT 'ACTIVE' NOT NULL,
	`created_at` text NOT NULL,
	CONSTRAINT "accounts_status_check" CHECK("accounts"."status" in ('ACTIVE', 'SUSPENDED'))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `accounts_external_subject_unique` ON `accounts` (`external_subject`);--> statement-breakpoint
CREATE TABLE `buildings` (
	`id` text PRIMARY KEY NOT NULL,
	`center_id` text NOT NULL,
	`code` text NOT NULL,
	`display_name` text NOT NULL,
	`status` text DEFAULT 'ACTIVE' NOT NULL,
	`created_at` text NOT NULL,
	FOREIGN KEY (`center_id`) REFERENCES `centers`(`id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "buildings_status_check" CHECK("buildings"."status" in ('ACTIVE', 'INACTIVE'))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `buildings_center_code_unique` ON `buildings` (`center_id`,`code`);--> statement-breakpoint
CREATE UNIQUE INDEX `buildings_center_id_unique` ON `buildings` (`center_id`,`id`);--> statement-breakpoint
CREATE TABLE `center_location_config_versions` (
	`id` text PRIMARY KEY NOT NULL,
	`center_id` text NOT NULL,
	`version_number` integer NOT NULL,
	`rooms_enabled` integer NOT NULL,
	`rooms_required` integer NOT NULL,
	`places_enabled` integer NOT NULL,
	`places_required` integer NOT NULL,
	`valid_from` text NOT NULL,
	`valid_until` text,
	`created_at` text NOT NULL,
	FOREIGN KEY (`center_id`) REFERENCES `centers`(`id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "center_location_config_flags_check" CHECK(("center_location_config_versions"."rooms_required" = 0 or "center_location_config_versions"."rooms_enabled" = 1)
        and ("center_location_config_versions"."places_required" = 0 or ("center_location_config_versions"."places_enabled" = 1 and "center_location_config_versions"."rooms_enabled" = 1))
        and ("center_location_config_versions"."places_enabled" = 0 or "center_location_config_versions"."rooms_enabled" = 1)),
	CONSTRAINT "center_location_config_time_check" CHECK("center_location_config_versions"."valid_until" is null or "center_location_config_versions"."valid_until" > "center_location_config_versions"."valid_from")
);
--> statement-breakpoint
CREATE UNIQUE INDEX `center_location_config_version_unique` ON `center_location_config_versions` (`center_id`,`version_number`);--> statement-breakpoint
CREATE UNIQUE INDEX `center_location_config_current_unique` ON `center_location_config_versions` (`center_id`) WHERE "center_location_config_versions"."valid_until" is null;--> statement-breakpoint
CREATE INDEX `center_location_config_lookup_idx` ON `center_location_config_versions` (`center_id`,`valid_from`,`valid_until`);--> statement-breakpoint
CREATE TABLE `centers` (
	`id` text PRIMARY KEY NOT NULL,
	`code` text NOT NULL,
	`display_name` text NOT NULL,
	`status` text DEFAULT 'ACTIVE' NOT NULL,
	`created_at` text NOT NULL,
	CONSTRAINT "centers_status_check" CHECK("centers"."status" in ('ACTIVE', 'INACTIVE'))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `centers_code_unique` ON `centers` (`code`);--> statement-breakpoint
CREATE TABLE `floors` (
	`id` text PRIMARY KEY NOT NULL,
	`center_id` text NOT NULL,
	`building_id` text NOT NULL,
	`code` text NOT NULL,
	`display_name` text NOT NULL,
	`status` text DEFAULT 'ACTIVE' NOT NULL,
	`created_at` text NOT NULL,
	FOREIGN KEY (`center_id`,`building_id`) REFERENCES `buildings`(`center_id`,`id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "floors_status_check" CHECK("floors"."status" in ('ACTIVE', 'INACTIVE'))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `floors_building_code_unique` ON `floors` (`building_id`,`code`);--> statement-breakpoint
CREATE UNIQUE INDEX `floors_center_building_id_unique` ON `floors` (`center_id`,`building_id`,`id`);--> statement-breakpoint
CREATE TABLE `places` (
	`id` text PRIMARY KEY NOT NULL,
	`center_id` text NOT NULL,
	`room_id` text NOT NULL,
	`code` text NOT NULL,
	`display_name` text NOT NULL,
	`status` text DEFAULT 'ACTIVE' NOT NULL,
	`created_at` text NOT NULL,
	FOREIGN KEY (`center_id`,`room_id`) REFERENCES `rooms`(`center_id`,`id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "places_status_check" CHECK("places"."status" in ('ACTIVE', 'INACTIVE'))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `places_room_code_unique` ON `places` (`room_id`,`code`);--> statement-breakpoint
CREATE UNIQUE INDEX `places_center_room_id_unique` ON `places` (`center_id`,`room_id`,`id`);--> statement-breakpoint
CREATE TABLE `rooms` (
	`id` text PRIMARY KEY NOT NULL,
	`center_id` text NOT NULL,
	`unit_id` text NOT NULL,
	`code` text NOT NULL,
	`display_name` text NOT NULL,
	`status` text DEFAULT 'ACTIVE' NOT NULL,
	`created_at` text NOT NULL,
	FOREIGN KEY (`center_id`,`unit_id`) REFERENCES `units`(`center_id`,`id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "rooms_status_check" CHECK("rooms"."status" in ('ACTIVE', 'INACTIVE'))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `rooms_unit_code_unique` ON `rooms` (`unit_id`,`code`);--> statement-breakpoint
CREATE UNIQUE INDEX `rooms_center_unit_id_unique` ON `rooms` (`center_id`,`unit_id`,`id`);--> statement-breakpoint
CREATE UNIQUE INDEX `rooms_center_id_unique` ON `rooms` (`center_id`,`id`);--> statement-breakpoint
CREATE TABLE `units` (
	`id` text PRIMARY KEY NOT NULL,
	`center_id` text NOT NULL,
	`building_id` text,
	`floor_id` text,
	`code` text NOT NULL,
	`display_name` text NOT NULL,
	`status` text DEFAULT 'ACTIVE' NOT NULL,
	`created_at` text NOT NULL,
	FOREIGN KEY (`center_id`) REFERENCES `centers`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`center_id`,`building_id`) REFERENCES `buildings`(`center_id`,`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`center_id`,`building_id`,`floor_id`) REFERENCES `floors`(`center_id`,`building_id`,`id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "units_floor_requires_building_check" CHECK("units"."floor_id" is null or "units"."building_id" is not null),
	CONSTRAINT "units_status_check" CHECK("units"."status" in ('ACTIVE', 'INACTIVE'))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `units_center_code_unique` ON `units` (`center_id`,`code`);--> statement-breakpoint
CREATE UNIQUE INDEX `units_center_id_unique` ON `units` (`center_id`,`id`);--> statement-breakpoint
CREATE TABLE `resident_center_episodes` (
	`id` text PRIMARY KEY NOT NULL,
	`resident_id` text NOT NULL,
	`center_id` text NOT NULL,
	`internal_reference` text,
	`valid_from` text NOT NULL,
	`valid_until` text,
	`created_at` text NOT NULL,
	`created_by_account_id` text NOT NULL,
	`created_by_profile` text NOT NULL,
	FOREIGN KEY (`created_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`center_id`,`resident_id`) REFERENCES `residents`(`center_id`,`id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "resident_center_episodes_time_check" CHECK("resident_center_episodes"."valid_until" is null or "resident_center_episodes"."valid_until" > "resident_center_episodes"."valid_from"),
	CONSTRAINT "resident_center_episodes_profile_check" CHECK("resident_center_episodes"."created_by_profile" in ('ADMINISTRACION', 'ENFERMERIA'))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `resident_center_episodes_scope_unique` ON `resident_center_episodes` (`id`,`resident_id`,`center_id`);--> statement-breakpoint
CREATE UNIQUE INDEX `resident_center_episodes_active_unique` ON `resident_center_episodes` (`resident_id`) WHERE "resident_center_episodes"."valid_until" is null;--> statement-breakpoint
CREATE INDEX `resident_center_episodes_lookup_idx` ON `resident_center_episodes` (`center_id`,`resident_id`,`valid_from`);--> statement-breakpoint
CREATE TABLE `resident_location_intervals` (
	`id` text PRIMARY KEY NOT NULL,
	`resident_id` text NOT NULL,
	`center_id` text NOT NULL,
	`episode_id` text NOT NULL,
	`unit_id` text NOT NULL,
	`building_id` text,
	`floor_id` text,
	`room_id` text,
	`place_id` text,
	`valid_from` text NOT NULL,
	`valid_until` text,
	`changed_at` text NOT NULL,
	`changed_by_account_id` text NOT NULL,
	`changed_by_profile` text NOT NULL,
	FOREIGN KEY (`changed_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`episode_id`,`resident_id`,`center_id`) REFERENCES `resident_center_episodes`(`id`,`resident_id`,`center_id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`center_id`,`unit_id`) REFERENCES `units`(`center_id`,`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`center_id`,`building_id`) REFERENCES `buildings`(`center_id`,`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`center_id`,`building_id`,`floor_id`) REFERENCES `floors`(`center_id`,`building_id`,`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`center_id`,`unit_id`,`room_id`) REFERENCES `rooms`(`center_id`,`unit_id`,`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`center_id`,`room_id`,`place_id`) REFERENCES `places`(`center_id`,`room_id`,`id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "resident_location_intervals_time_check" CHECK("resident_location_intervals"."valid_until" is null or "resident_location_intervals"."valid_until" > "resident_location_intervals"."valid_from"),
	CONSTRAINT "resident_location_intervals_hierarchy_check" CHECK(("resident_location_intervals"."floor_id" is null or "resident_location_intervals"."building_id" is not null)
        and ("resident_location_intervals"."place_id" is null or "resident_location_intervals"."room_id" is not null)),
	CONSTRAINT "resident_location_intervals_profile_check" CHECK("resident_location_intervals"."changed_by_profile" in ('ADMINISTRACION', 'ENFERMERIA'))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `resident_location_intervals_active_unique` ON `resident_location_intervals` (`resident_id`) WHERE "resident_location_intervals"."valid_until" is null;--> statement-breakpoint
CREATE INDEX `resident_location_intervals_current_lookup_idx` ON `resident_location_intervals` (`center_id`,`unit_id`,`resident_id`,`valid_until`);--> statement-breakpoint
CREATE TABLE `residents` (
	`id` text PRIMARY KEY NOT NULL,
	`center_id` text NOT NULL,
	`display_name` text NOT NULL,
	`birth_date` text NOT NULL,
	`documented_sex_code` text NOT NULL,
	`status` text DEFAULT 'ACTIVE' NOT NULL,
	`inactivation_reason` text,
	`inactivated_at` text,
	`inactivated_by_account_id` text,
	`inactivated_by_profile` text,
	`created_at` text NOT NULL,
	`created_by_account_id` text NOT NULL,
	`created_by_profile` text NOT NULL,
	FOREIGN KEY (`center_id`) REFERENCES `centers`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`inactivated_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	FOREIGN KEY (`created_by_account_id`) REFERENCES `accounts`(`id`) ON UPDATE no action ON DELETE restrict,
	CONSTRAINT "residents_documented_sex_check" CHECK("residents"."documented_sex_code" in ('male', 'female', 'other', 'unknown')),
	CONSTRAINT "residents_status_check" CHECK("residents"."status" in ('ACTIVE', 'INACTIVE')),
	CONSTRAINT "residents_created_profile_check" CHECK("residents"."created_by_profile" in ('ADMINISTRACION', 'ENFERMERIA')),
	CONSTRAINT "residents_inactivation_fields_check" CHECK(("residents"."status" = 'ACTIVE'
          and "residents"."inactivation_reason" is null
          and "residents"."inactivated_at" is null
          and "residents"."inactivated_by_account_id" is null
          and "residents"."inactivated_by_profile" is null)
        or ("residents"."status" = 'INACTIVE'
          and length(trim("residents"."inactivation_reason")) > 0
          and "residents"."inactivated_at" is not null
          and "residents"."inactivated_by_account_id" is not null
          and "residents"."inactivated_by_profile" = 'ADMINISTRACION'))
);
--> statement-breakpoint
CREATE UNIQUE INDEX `residents_center_id_unique` ON `residents` (`center_id`,`id`);--> statement-breakpoint
CREATE INDEX `residents_center_status_idx` ON `residents` (`center_id`,`status`,`id`);
--> statement-breakpoint
INSERT INTO `barthel_catalog_options`
  (`instrument_version_code`, `item_code`, `option_code`, `awarded_score`)
VALUES
  ('BARTHEL_COMUN_V0_1', 'COMER', 'INDEPENDIENTE', 10),
  ('BARTHEL_COMUN_V0_1', 'COMER', 'AYUDA_PREPARAR_O_CORTAR', 5),
  ('BARTHEL_COMUN_V0_1', 'COMER', 'DEPENDIENTE', 0),
  ('BARTHEL_COMUN_V0_1', 'LAVARSE', 'SOLO_COMPLETO', 5),
  ('BARTHEL_COMUN_V0_1', 'LAVARSE', 'NECESITA_AYUDA', 0),
  ('BARTHEL_COMUN_V0_1', 'VESTIRSE', 'INDEPENDIENTE', 10),
  ('BARTHEL_COMUN_V0_1', 'VESTIRSE', 'AYUDA_REALIZA_AL_MENOS_MITAD', 5),
  ('BARTHEL_COMUN_V0_1', 'VESTIRSE', 'DEPENDIENTE', 0),
  ('BARTHEL_COMUN_V0_1', 'ARREGLARSE', 'INDEPENDIENTE_HIGIENE_PERSONAL_BASICA', 5),
  ('BARTHEL_COMUN_V0_1', 'ARREGLARSE', 'NECESITA_AYUDA', 0),
  ('BARTHEL_COMUN_V0_1', 'DEPOSICION', 'CONTINENTE', 10),
  ('BARTHEL_COMUN_V0_1', 'DEPOSICION', 'INCONTINENCIA_OCASIONAL_O_AYUDA_ENEMAS_SUPOSITORIOS', 5),
  ('BARTHEL_COMUN_V0_1', 'DEPOSICION', 'INCONTINENTE', 0),
  ('BARTHEL_COMUN_V0_1', 'MICCION', 'CONTINENTE', 10),
  ('BARTHEL_COMUN_V0_1', 'MICCION', 'MAX_UN_EPISODIO_24H_O_AYUDA_SONDA_COLECTOR', 5),
  ('BARTHEL_COMUN_V0_1', 'MICCION', 'INCONTINENTE', 0),
  ('BARTHEL_COMUN_V0_1', 'USO_RETRETE', 'INDEPENDIENTE', 10),
  ('BARTHEL_COMUN_V0_1', 'USO_RETRETE', 'PEQUENA_AYUDA_ROPA_O_TRANSFERENCIA_SE_LIMPIA_SOLO', 5),
  ('BARTHEL_COMUN_V0_1', 'USO_RETRETE', 'DEPENDIENTE', 0),
  ('BARTHEL_COMUN_V0_1', 'TRASLADO_CAMA_SILLON', 'INDEPENDIENTE', 15),
  ('BARTHEL_COMUN_V0_1', 'TRASLADO_CAMA_SILLON', 'SUPERVISION_O_MINIMA_AYUDA', 10),
  ('BARTHEL_COMUN_V0_1', 'TRASLADO_CAMA_SILLON', 'GRAN_AYUDA_MANTIENE_SEDESTACION', 5),
  ('BARTHEL_COMUN_V0_1', 'TRASLADO_CAMA_SILLON', 'DEPENDIENTE_GRUA_O_DOS_PERSONAS', 0),
  ('BARTHEL_COMUN_V0_1', 'DEAMBULACION', 'CAMINA_50M_INDEPENDIENTE_CON_AYUDA_TECNICA_SI_PRECISA', 15),
  ('BARTHEL_COMUN_V0_1', 'DEAMBULACION', 'NECESITA_AYUDA_O_SUPERVISION', 10),
  ('BARTHEL_COMUN_V0_1', 'DEAMBULACION', 'INDEPENDIENTE_EN_SILLA_RUEDAS', 5),
  ('BARTHEL_COMUN_V0_1', 'DEAMBULACION', 'DEPENDIENTE', 0),
  ('BARTHEL_COMUN_V0_1', 'ESCALERAS', 'SUBE_BAJA_UN_PISO_SOLO', 10),
  ('BARTHEL_COMUN_V0_1', 'ESCALERAS', 'NECESITA_AYUDA_O_SUPERVISION', 5),
  ('BARTHEL_COMUN_V0_1', 'ESCALERAS', 'DEPENDIENTE', 0);
--> statement-breakpoint
CREATE TRIGGER `barthel_catalog_options_no_update`
BEFORE UPDATE ON `barthel_catalog_options`
BEGIN
  SELECT RAISE(ABORT, 'BARTHEL_CATALOG_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `barthel_catalog_options_no_delete`
BEFORE DELETE ON `barthel_catalog_options`
BEGIN
  SELECT RAISE(ABORT, 'BARTHEL_CATALOG_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `center_location_config_versions_close_only`
BEFORE UPDATE ON `center_location_config_versions`
WHEN OLD.`valid_until` IS NOT NULL
  OR NEW.`id` <> OLD.`id`
  OR NEW.`center_id` <> OLD.`center_id`
  OR NEW.`version_number` <> OLD.`version_number`
  OR NEW.`rooms_enabled` <> OLD.`rooms_enabled`
  OR NEW.`rooms_required` <> OLD.`rooms_required`
  OR NEW.`places_enabled` <> OLD.`places_enabled`
  OR NEW.`places_required` <> OLD.`places_required`
  OR NEW.`valid_from` <> OLD.`valid_from`
  OR NEW.`created_at` <> OLD.`created_at`
  OR NEW.`valid_until` IS NULL
BEGIN
  SELECT RAISE(ABORT, 'LOCATION_CONFIG_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `center_location_config_versions_no_delete`
BEFORE DELETE ON `center_location_config_versions`
BEGIN
  SELECT RAISE(ABORT, 'LOCATION_CONFIG_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `resident_center_episodes_no_overlap`
BEFORE INSERT ON `resident_center_episodes`
BEGIN
  SELECT CASE WHEN NOT EXISTS (
    SELECT 1 FROM `accounts` account
    JOIN `profile_scopes` profile
      ON profile.`account_id` = account.`id`
     AND profile.`center_id` = NEW.`center_id`
     AND profile.`profile_code` = NEW.`created_by_profile`
     AND profile.`status` = 'ACTIVE'
    WHERE account.`id` = NEW.`created_by_account_id`
      AND account.`status` = 'ACTIVE'
      AND (
        profile.`profile_code` = 'ADMINISTRACION'
        OR (profile.`profile_code` = 'ENFERMERIA' AND EXISTS (
          SELECT 1 FROM `profile_permissions` permission
          WHERE permission.`profile_scope_id` = profile.`id`
            AND permission.`center_id` = profile.`center_id`
            AND permission.`permission_code` = 'RESIDENT_IDENTITY_CREATE'
            AND permission.`revoked_at` IS NULL
        ))
      )
  ) THEN RAISE(ABORT, 'RESIDENT_CREATE_NOT_AUTHORIZED') END;
  SELECT CASE WHEN EXISTS (
    SELECT 1 FROM `resident_center_episodes` existing
    WHERE existing.`resident_id` = NEW.`resident_id`
      AND (NEW.`valid_until` IS NULL OR existing.`valid_from` < NEW.`valid_until`)
      AND (existing.`valid_until` IS NULL OR NEW.`valid_from` < existing.`valid_until`)
  ) THEN RAISE(ABORT, 'RESIDENT_EPISODE_OVERLAP') END;
END;
--> statement-breakpoint
CREATE TRIGGER `resident_center_episodes_close_only`
BEFORE UPDATE ON `resident_center_episodes`
WHEN OLD.`valid_until` IS NOT NULL
  OR NEW.`id` <> OLD.`id`
  OR NEW.`resident_id` <> OLD.`resident_id`
  OR NEW.`center_id` <> OLD.`center_id`
  OR NEW.`internal_reference` IS NOT OLD.`internal_reference`
  OR NEW.`valid_from` <> OLD.`valid_from`
  OR NEW.`created_at` <> OLD.`created_at`
  OR NEW.`created_by_account_id` <> OLD.`created_by_account_id`
  OR NEW.`created_by_profile` <> OLD.`created_by_profile`
  OR NEW.`valid_until` IS NULL
BEGIN
  SELECT RAISE(ABORT, 'RESIDENT_EPISODE_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `resident_center_episodes_no_delete`
BEFORE DELETE ON `resident_center_episodes`
BEGIN
  SELECT RAISE(ABORT, 'RESIDENT_EPISODE_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `resident_location_intervals_validate_insert`
BEFORE INSERT ON `resident_location_intervals`
BEGIN
  SELECT CASE WHEN NOT EXISTS (
    SELECT 1 FROM `accounts` account
    JOIN `profile_scopes` profile
      ON profile.`account_id` = account.`id`
     AND profile.`center_id` = NEW.`center_id`
     AND profile.`profile_code` = NEW.`changed_by_profile`
     AND profile.`status` = 'ACTIVE'
    JOIN `profile_unit_scopes` unit_scope
      ON unit_scope.`profile_scope_id` = profile.`id`
     AND unit_scope.`center_id` = profile.`center_id`
     AND unit_scope.`unit_id` = NEW.`unit_id`
     AND unit_scope.`revoked_at` IS NULL
    WHERE account.`id` = NEW.`changed_by_account_id`
      AND account.`status` = 'ACTIVE'
      AND (
        profile.`profile_code` = 'ADMINISTRACION'
        OR (profile.`profile_code` = 'ENFERMERIA' AND EXISTS (
          SELECT 1 FROM `profile_permissions` permission
          WHERE permission.`profile_scope_id` = profile.`id`
            AND permission.`center_id` = profile.`center_id`
            AND permission.`permission_code` = 'RESIDENT_IDENTITY_CREATE'
            AND permission.`revoked_at` IS NULL
        ))
      )
  ) THEN RAISE(ABORT, 'RESIDENT_CREATE_NOT_AUTHORIZED') END;
  SELECT CASE WHEN NOT EXISTS (
    SELECT 1 FROM `resident_center_episodes` episode
    WHERE episode.`id` = NEW.`episode_id`
      AND episode.`resident_id` = NEW.`resident_id`
      AND episode.`center_id` = NEW.`center_id`
      AND episode.`valid_until` IS NULL
  ) THEN RAISE(ABORT, 'RESIDENT_EPISODE_NOT_ACTIVE') END;
  SELECT CASE WHEN EXISTS (
    SELECT 1 FROM `resident_location_intervals` existing
    WHERE existing.`resident_id` = NEW.`resident_id`
      AND (NEW.`valid_until` IS NULL OR existing.`valid_from` < NEW.`valid_until`)
      AND (existing.`valid_until` IS NULL OR NEW.`valid_from` < existing.`valid_until`)
  ) THEN RAISE(ABORT, 'RESIDENT_LOCATION_OVERLAP') END;
  SELECT CASE WHEN NOT EXISTS (
    SELECT 1 FROM `center_location_config_versions` config
    WHERE config.`center_id` = NEW.`center_id`
      AND config.`valid_until` IS NULL
      AND (config.`rooms_enabled` = 1 OR NEW.`room_id` IS NULL)
      AND (config.`places_enabled` = 1 OR NEW.`place_id` IS NULL)
      AND (config.`rooms_required` = 0 OR NEW.`room_id` IS NOT NULL)
      AND (config.`places_required` = 0 OR NEW.`place_id` IS NOT NULL)
  ) THEN RAISE(ABORT, 'RESIDENT_LOCATION_CONFIG_INVALID') END;
END;
--> statement-breakpoint
CREATE TRIGGER `resident_location_intervals_close_only`
BEFORE UPDATE ON `resident_location_intervals`
WHEN OLD.`valid_until` IS NOT NULL
  OR NEW.`id` <> OLD.`id`
  OR NEW.`resident_id` <> OLD.`resident_id`
  OR NEW.`center_id` <> OLD.`center_id`
  OR NEW.`episode_id` <> OLD.`episode_id`
  OR NEW.`unit_id` <> OLD.`unit_id`
  OR NEW.`building_id` IS NOT OLD.`building_id`
  OR NEW.`floor_id` IS NOT OLD.`floor_id`
  OR NEW.`room_id` IS NOT OLD.`room_id`
  OR NEW.`place_id` IS NOT OLD.`place_id`
  OR NEW.`valid_from` <> OLD.`valid_from`
  OR NEW.`changed_at` <> OLD.`changed_at`
  OR NEW.`changed_by_account_id` <> OLD.`changed_by_account_id`
  OR NEW.`changed_by_profile` <> OLD.`changed_by_profile`
  OR NEW.`valid_until` IS NULL
BEGIN
  SELECT RAISE(ABORT, 'RESIDENT_LOCATION_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `resident_location_intervals_no_delete`
BEFORE DELETE ON `resident_location_intervals`
BEGIN
  SELECT RAISE(ABORT, 'RESIDENT_LOCATION_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_drafts_transition_guard`
BEFORE UPDATE ON `baseline_drafts`
WHEN OLD.`status` <> 'ACTIVE'
  OR NEW.`id` <> OLD.`id`
  OR NEW.`resident_id` <> OLD.`resident_id`
  OR NEW.`center_id` <> OLD.`center_id`
  OR NEW.`created_in_unit_id` <> OLD.`created_in_unit_id`
  OR NEW.`created_by_account_id` <> OLD.`created_by_account_id`
  OR NEW.`created_by_profile` <> OLD.`created_by_profile`
  OR NEW.`created_at` <> OLD.`created_at`
  OR NEW.`status` NOT IN ('ACTIVE', 'CANCELLED', 'SIGNED')
  OR (NEW.`status` = 'ACTIVE' AND NEW.`draft_revision` <> OLD.`draft_revision` + 1)
  OR (NEW.`status` <> 'ACTIVE' AND NEW.`draft_revision` <> OLD.`draft_revision`)
  OR (NEW.`status` = 'SIGNED' AND NOT EXISTS (
    SELECT 1 FROM `baseline_versions` version WHERE version.`source_draft_id` = OLD.`id`
  ))
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_DRAFT_TRANSITION_INVALID');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_drafts_no_delete`
BEFORE DELETE ON `baseline_drafts`
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_DRAFT_DELETE_FORBIDDEN');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_draft_areas_mutable_only_while_active_update`
BEFORE UPDATE ON `baseline_draft_areas`
WHEN NOT EXISTS (
  SELECT 1 FROM `baseline_drafts` draft
  WHERE draft.`id` = OLD.`draft_id` AND draft.`status` = 'ACTIVE'
)
  OR NEW.`id` <> OLD.`id`
  OR NEW.`draft_id` <> OLD.`draft_id`
  OR NEW.`resident_id` <> OLD.`resident_id`
  OR NEW.`center_id` <> OLD.`center_id`
  OR NEW.`area_code` <> OLD.`area_code`
  OR NEW.`catalog_version_code` <> OLD.`catalog_version_code`
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_DRAFT_AREA_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_draft_areas_insert_only_while_active`
BEFORE INSERT ON `baseline_draft_areas`
WHEN NOT EXISTS (
  SELECT 1 FROM `baseline_drafts` draft
  WHERE draft.`id` = NEW.`draft_id` AND draft.`status` = 'ACTIVE'
)
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_DRAFT_AREA_PARENT_NOT_ACTIVE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_draft_areas_mutable_only_while_active_delete`
BEFORE DELETE ON `baseline_draft_areas`
WHEN NOT EXISTS (
  SELECT 1 FROM `baseline_drafts` draft
  WHERE draft.`id` = OLD.`draft_id` AND draft.`status` = 'ACTIVE'
)
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_DRAFT_AREA_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_draft_barthel_mutable_only_while_active_update`
BEFORE UPDATE ON `baseline_draft_barthel`
WHEN NOT EXISTS (
  SELECT 1 FROM `baseline_drafts` draft
  WHERE draft.`id` = OLD.`draft_id` AND draft.`status` = 'ACTIVE'
)
  OR NEW.`id` <> OLD.`id`
  OR NEW.`draft_id` <> OLD.`draft_id`
  OR NEW.`resident_id` <> OLD.`resident_id`
  OR NEW.`center_id` <> OLD.`center_id`
  OR NEW.`instrument_version_code` <> OLD.`instrument_version_code`
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_DRAFT_BARTHEL_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_draft_barthel_insert_only_while_active`
BEFORE INSERT ON `baseline_draft_barthel`
WHEN NOT EXISTS (
  SELECT 1 FROM `baseline_drafts` draft
  WHERE draft.`id` = NEW.`draft_id` AND draft.`status` = 'ACTIVE'
)
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_DRAFT_BARTHEL_PARENT_NOT_ACTIVE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_draft_barthel_mutable_only_while_active_delete`
BEFORE DELETE ON `baseline_draft_barthel`
WHEN NOT EXISTS (
  SELECT 1 FROM `baseline_drafts` draft
  WHERE draft.`id` = OLD.`draft_id` AND draft.`status` = 'ACTIVE'
)
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_DRAFT_BARTHEL_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_draft_barthel_items_mutable_only_while_active_update`
BEFORE UPDATE ON `baseline_draft_barthel_items`
WHEN NOT EXISTS (
  SELECT 1 FROM `baseline_drafts` draft
  WHERE draft.`id` = OLD.`draft_id` AND draft.`status` = 'ACTIVE'
)
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_DRAFT_BARTHEL_ITEM_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_draft_barthel_items_insert_only_while_active`
BEFORE INSERT ON `baseline_draft_barthel_items`
WHEN NOT EXISTS (
  SELECT 1 FROM `baseline_drafts` draft
  WHERE draft.`id` = NEW.`draft_id` AND draft.`status` = 'ACTIVE'
)
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_DRAFT_BARTHEL_ITEM_PARENT_NOT_ACTIVE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_draft_barthel_items_mutable_only_while_active_delete`
BEFORE DELETE ON `baseline_draft_barthel_items`
WHEN NOT EXISTS (
  SELECT 1 FROM `baseline_drafts` draft
  WHERE draft.`id` = OLD.`draft_id` AND draft.`status` = 'ACTIVE'
)
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_DRAFT_BARTHEL_ITEM_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_draft_areas_payload_validate_insert`
BEFORE INSERT ON `baseline_draft_areas`
BEGIN
  SELECT CASE WHEN NEW.`area_code` = 'CONTINENCIA' AND (
    (EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.managementCodes') WHERE value = 'OTRO')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.managementOtherText'), ''))) = 0)
    OR (NOT EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.managementCodes') WHERE value = 'OTRO')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.managementOtherText'), ''))) > 0)
  ) THEN RAISE(ABORT, 'BASELINE_OPEN_TEXT_INVALID') END;
  SELECT CASE WHEN NEW.`area_code` = 'COMUNICACION' AND (
    (EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.usualFormsCodes') WHERE value = 'OTRA')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.usualFormOtherText'), ''))) = 0)
    OR (NOT EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.usualFormsCodes') WHERE value = 'OTRA')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.usualFormOtherText'), ''))) > 0)
  ) THEN RAISE(ABORT, 'BASELINE_OPEN_TEXT_INVALID') END;
  SELECT CASE WHEN NEW.`area_code` = 'CONDUCTA' AND (
    (EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.patternCodes') WHERE value = 'OTRA')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.patternOtherText'), ''))) = 0)
    OR (NOT EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.patternCodes') WHERE value = 'OTRA')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.patternOtherText'), ''))) > 0)
  ) THEN RAISE(ABORT, 'BASELINE_OPEN_TEXT_INVALID') END;
  SELECT CASE WHEN NEW.`area_code` = 'AYUDAS_HABITUALES' AND (
    (EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.aidCodes') WHERE value = 'OTRO_PRODUCTO_DE_APOYO')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.otherSupportProductText'), ''))) = 0)
    OR (NOT EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.aidCodes') WHERE value = 'OTRO_PRODUCTO_DE_APOYO')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.otherSupportProductText'), ''))) > 0)
    OR (EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.aidCodes') WHERE value = 'OTRO')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.otherSupportText'), ''))) = 0)
    OR (NOT EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.aidCodes') WHERE value = 'OTRO')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.otherSupportText'), ''))) > 0)
  ) THEN RAISE(ABORT, 'BASELINE_OPEN_TEXT_INVALID') END;
  SELECT CASE WHEN NEW.`area_code` = 'COGNICION' AND (
    (json_extract(NEW.`answer_payload`, '$.etiologyCode') = 'OTRA'
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.etiologyOtherText'), ''))) = 0)
    OR (coalesce(json_extract(NEW.`answer_payload`, '$.etiologyCode'), '') <> 'OTRA'
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.etiologyOtherText'), ''))) > 0)
    OR (json_extract(NEW.`answer_payload`, '$.clinicalReferenceSourceCode') = 'OTRA'
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.clinicalReferenceSourceOtherText'), ''))) = 0)
    OR (coalesce(json_extract(NEW.`answer_payload`, '$.clinicalReferenceSourceCode'), '') <> 'OTRA'
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.clinicalReferenceSourceOtherText'), ''))) > 0)
  ) THEN RAISE(ABORT, 'BASELINE_OPEN_TEXT_INVALID') END;
  SELECT CASE WHEN NEW.`area_code` = 'ALIMENTACION' AND (
    (json_extract(NEW.`answer_payload`, '$.swallowingPrecautionsCode') = 'PRECAUCIONES_DOCUMENTADAS'
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.swallowingPrecautionsText'), ''))) = 0)
    OR (coalesce(json_extract(NEW.`answer_payload`, '$.swallowingPrecautionsCode'), '') <> 'PRECAUCIONES_DOCUMENTADAS'
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.swallowingPrecautionsText'), ''))) > 0)
  ) THEN RAISE(ABORT, 'BASELINE_OPEN_TEXT_INVALID') END;
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_draft_areas_payload_validate_update`
BEFORE UPDATE OF `answer_payload` ON `baseline_draft_areas`
BEGIN
  SELECT CASE WHEN NEW.`area_code` = 'CONTINENCIA' AND (
    (EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.managementCodes') WHERE value = 'OTRO')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.managementOtherText'), ''))) = 0)
    OR (NOT EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.managementCodes') WHERE value = 'OTRO')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.managementOtherText'), ''))) > 0)
  ) THEN RAISE(ABORT, 'BASELINE_OPEN_TEXT_INVALID') END;
  SELECT CASE WHEN NEW.`area_code` = 'COMUNICACION' AND (
    (EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.usualFormsCodes') WHERE value = 'OTRA')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.usualFormOtherText'), ''))) = 0)
    OR (NOT EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.usualFormsCodes') WHERE value = 'OTRA')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.usualFormOtherText'), ''))) > 0)
  ) THEN RAISE(ABORT, 'BASELINE_OPEN_TEXT_INVALID') END;
  SELECT CASE WHEN NEW.`area_code` = 'CONDUCTA' AND (
    (EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.patternCodes') WHERE value = 'OTRA')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.patternOtherText'), ''))) = 0)
    OR (NOT EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.patternCodes') WHERE value = 'OTRA')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.patternOtherText'), ''))) > 0)
  ) THEN RAISE(ABORT, 'BASELINE_OPEN_TEXT_INVALID') END;
  SELECT CASE WHEN NEW.`area_code` = 'AYUDAS_HABITUALES' AND (
    (EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.aidCodes') WHERE value = 'OTRO_PRODUCTO_DE_APOYO')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.otherSupportProductText'), ''))) = 0)
    OR (NOT EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.aidCodes') WHERE value = 'OTRO_PRODUCTO_DE_APOYO')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.otherSupportProductText'), ''))) > 0)
    OR (EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.aidCodes') WHERE value = 'OTRO')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.otherSupportText'), ''))) = 0)
    OR (NOT EXISTS (SELECT 1 FROM json_each(NEW.`answer_payload`, '$.aidCodes') WHERE value = 'OTRO')
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.otherSupportText'), ''))) > 0)
  ) THEN RAISE(ABORT, 'BASELINE_OPEN_TEXT_INVALID') END;
  SELECT CASE WHEN NEW.`area_code` = 'COGNICION' AND (
    (json_extract(NEW.`answer_payload`, '$.etiologyCode') = 'OTRA'
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.etiologyOtherText'), ''))) = 0)
    OR (coalesce(json_extract(NEW.`answer_payload`, '$.etiologyCode'), '') <> 'OTRA'
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.etiologyOtherText'), ''))) > 0)
    OR (json_extract(NEW.`answer_payload`, '$.clinicalReferenceSourceCode') = 'OTRA'
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.clinicalReferenceSourceOtherText'), ''))) = 0)
    OR (coalesce(json_extract(NEW.`answer_payload`, '$.clinicalReferenceSourceCode'), '') <> 'OTRA'
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.clinicalReferenceSourceOtherText'), ''))) > 0)
  ) THEN RAISE(ABORT, 'BASELINE_OPEN_TEXT_INVALID') END;
  SELECT CASE WHEN NEW.`area_code` = 'ALIMENTACION' AND (
    (json_extract(NEW.`answer_payload`, '$.swallowingPrecautionsCode') = 'PRECAUCIONES_DOCUMENTADAS'
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.swallowingPrecautionsText'), ''))) = 0)
    OR (coalesce(json_extract(NEW.`answer_payload`, '$.swallowingPrecautionsCode'), '') <> 'PRECAUCIONES_DOCUMENTADAS'
      AND length(trim(coalesce(json_extract(NEW.`answer_payload`, '$.swallowingPrecautionsText'), ''))) > 0)
  ) THEN RAISE(ABORT, 'BASELINE_OPEN_TEXT_INVALID') END;
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_versions_validate_insert`
BEFORE INSERT ON `baseline_versions`
BEGIN
  SELECT CASE WHEN NOT EXISTS (
    SELECT 1 FROM `accounts` account
    JOIN `profile_scopes` profile
      ON profile.`account_id` = account.`id`
     AND profile.`center_id` = NEW.`center_id`
     AND profile.`profile_code` = NEW.`signed_by_profile`
     AND profile.`status` = 'ACTIVE'
    JOIN `profile_unit_scopes` unit_scope
      ON unit_scope.`profile_scope_id` = profile.`id`
     AND unit_scope.`center_id` = profile.`center_id`
     AND unit_scope.`unit_id` = NEW.`created_in_unit_id`
     AND unit_scope.`revoked_at` IS NULL
    JOIN `profile_permissions` permission
      ON permission.`profile_scope_id` = profile.`id`
     AND permission.`center_id` = profile.`center_id`
     AND permission.`permission_code` = CASE
       WHEN NEW.`reason_code` = 'ALTA' THEN 'BASELINE_INITIAL_COMPLETE'
       ELSE 'BASELINE_REEVALUATE'
     END
     AND permission.`revoked_at` IS NULL
    JOIN `resident_location_intervals` location
      ON location.`resident_id` = NEW.`resident_id`
     AND location.`center_id` = NEW.`center_id`
     AND location.`unit_id` = NEW.`created_in_unit_id`
     AND location.`valid_until` IS NULL
    WHERE account.`id` = NEW.`signed_by_account_id`
      AND account.`status` = 'ACTIVE'
  ) THEN RAISE(ABORT, 'BASELINE_SIGN_NOT_AUTHORIZED') END;
  SELECT CASE WHEN NOT EXISTS (
    SELECT 1 FROM `baseline_drafts` draft
    JOIN `residents` resident
      ON resident.`id` = draft.`resident_id` AND resident.`center_id` = draft.`center_id`
    WHERE draft.`id` = NEW.`source_draft_id`
      AND draft.`resident_id` = NEW.`resident_id`
      AND draft.`center_id` = NEW.`center_id`
      AND draft.`created_in_unit_id` = NEW.`created_in_unit_id`
      AND draft.`status` = 'ACTIVE'
      AND resident.`status` = 'ACTIVE'
      AND draft.`reason_code` = NEW.`reason_code`
      AND draft.`common_information_source_code` = NEW.`common_information_source_code`
      AND draft.`common_information_source_other_text` IS NEW.`common_information_source_other_text`
      AND draft.`common_information_date` = NEW.`common_information_date`
      AND draft.`created_by_account_id` = NEW.`created_by_account_id`
      AND draft.`created_by_profile` = NEW.`created_by_profile`
  ) THEN RAISE(ABORT, 'BASELINE_DRAFT_NOT_SIGNABLE') END;
  SELECT CASE WHEN NEW.`version_number` <> (
    SELECT coalesce(max(version.`version_number`), 0) + 1
    FROM `baseline_versions` version
    WHERE version.`resident_id` = NEW.`resident_id`
  ) THEN RAISE(ABORT, 'BASELINE_VERSION_NUMBER_INVALID') END;
  SELECT CASE WHEN (
    SELECT count(*) FROM `baseline_draft_areas` area
    WHERE area.`draft_id` = NEW.`source_draft_id`
      AND area.`catalog_version_code` = 'BASAL_AREAS_V0_1'
  ) <> 9 THEN RAISE(ABORT, 'BASELINE_AREAS_INCOMPLETE') END;
  SELECT CASE WHEN EXISTS (
    SELECT 1 FROM `baseline_draft_areas` area
    WHERE area.`draft_id` = NEW.`source_draft_id`
      AND (
        (area.`area_code` = 'MOVILIDAD' AND (
          json_extract(area.`answer_payload`, '$.displacementModeCode') IS NULL
          OR json_extract(area.`answer_payload`, '$.technicalAidCode') IS NULL
          OR json_extract(area.`answer_payload`, '$.transferCode') IS NULL))
        OR (area.`area_code` = 'ALIMENTACION' AND (
          json_extract(area.`answer_payload`, '$.routeCode') IS NULL
          OR json_extract(area.`answer_payload`, '$.foodTextureCode') IS NULL
          OR json_extract(area.`answer_payload`, '$.liquidConsistencyCode') IS NULL
          OR json_extract(area.`answer_payload`, '$.assistanceCode') IS NULL
          OR json_extract(area.`answer_payload`, '$.swallowingPrecautionsCode') IS NULL))
        OR (area.`area_code` = 'CONTINENCIA' AND (
          json_extract(area.`answer_payload`, '$.urinationCode') IS NULL
          OR json_extract(area.`answer_payload`, '$.bowelCode') IS NULL
          OR coalesce(json_array_length(area.`answer_payload`, '$.managementCodes'), 0) = 0))
        OR (area.`area_code` = 'ASEO_HIGIENE' AND (
          json_extract(area.`answer_payload`, '$.personalCareAssistanceCode') IS NULL
          OR json_extract(area.`answer_payload`, '$.bathingAssistanceCode') IS NULL))
        OR (area.`area_code` = 'COGNICION' AND json_extract(area.`answer_payload`, '$.categoryCode') IS NULL)
        OR (area.`area_code` = 'COMUNICACION' AND (
          json_extract(area.`answer_payload`, '$.comprehensionCode') IS NULL
          OR json_extract(area.`answer_payload`, '$.expressionCode') IS NULL
          OR coalesce(json_array_length(area.`answer_payload`, '$.usualFormsCodes'), 0) = 0))
        OR (area.`area_code` = 'CONDUCTA' AND (
          json_extract(area.`answer_payload`, '$.statusCode') IS NULL
          OR (json_extract(area.`answer_payload`, '$.statusCode') = 'PATRONES_CONDUCTUALES_HABITUALES'
            AND coalesce(json_array_length(area.`answer_payload`, '$.patternCodes'), 0) = 0)))
        OR (area.`area_code` = 'SUENO'
          AND coalesce(json_array_length(area.`answer_payload`, '$.patternCodes'), 0) = 0)
        OR (area.`area_code` = 'AYUDAS_HABITUALES'
          AND coalesce(json_array_length(area.`answer_payload`, '$.aidCodes'), 0) = 0)
      )
  ) THEN RAISE(ABORT, 'BASELINE_AREA_COMPONENTS_INCOMPLETE') END;
  SELECT CASE WHEN EXISTS (
    SELECT 1 FROM `baseline_draft_areas` area
    WHERE area.`draft_id` = NEW.`source_draft_id`
      AND CASE area.`area_code`
        WHEN 'MOVILIDAD' THEN
          json_extract(area.`answer_payload`, '$.displacementModeCode') NOT IN (
            'DEAMBULA_INDEPENDIENTE_SIN_AYUDA', 'DEAMBULA_CON_AYUDA_TECNICA',
            'DEAMBULA_CON_SUPERVISION', 'DEAMBULA_CON_AYUDA_FISICA_1_PERSONA',
            'DEAMBULA_CON_AYUDA_FISICA_2_PERSONAS', 'SILLA_RUEDAS_AUTOPROPULSADA',
            'SILLA_RUEDAS_IMPULSADA_POR_OTRA_PERSONA', 'SIN_DESPLAZAMIENTO_FUNCIONAL',
            'NO_DOCUMENTADO')
          OR json_extract(area.`answer_payload`, '$.technicalAidCode') NOT IN (
            'NINGUNA', 'BASTON', 'MULETA_O_MULETAS', 'ANDADOR_4_RUEDAS',
            'ANDADOR_2_RUEDAS', 'ANDADOR_FIJO_SIN_RUEDAS', 'OTRA', 'NO_DOCUMENTADO')
          OR json_extract(area.`answer_payload`, '$.transferCode') NOT IN (
            'INDEPENDIENTE', 'SUPERVISION', 'AYUDA_1_PERSONA', 'AYUDA_2_PERSONAS',
            'GRUA', 'NO_DOCUMENTADO')
        WHEN 'ALIMENTACION' THEN
          json_extract(area.`answer_payload`, '$.routeCode') NOT IN ('ORAL', 'ENTERAL', 'MIXTA', 'NO_DOCUMENTADO')
          OR json_extract(area.`answer_payload`, '$.foodTextureCode') NOT IN (
            'NORMAL', 'TROCEADA', 'TRITURADA', 'PURE', 'OTRA_TEXTURA_ADAPTADA',
            'NO_APLICA', 'NO_DOCUMENTADO')
          OR json_extract(area.`answer_payload`, '$.liquidConsistencyCode') NOT IN (
            'IDDSI_0_FINO_SIN_ESPESAR', 'IDDSI_1_LIGERAMENTE_ESPESO',
            'IDDSI_2_POCO_ESPESO', 'IDDSI_3_MODERADAMENTE_ESPESO',
            'IDDSI_4_EXTREMADAMENTE_ESPESO', 'NO_APLICA', 'NO_DOCUMENTADO')
          OR json_extract(area.`answer_payload`, '$.assistanceCode') NOT IN (
            'INDEPENDIENTE', 'PREPARAR_O_CORTAR_ALIMENTOS', 'SUPERVISION_O_INDICACIONES',
            'AYUDA_FISICA_PARCIAL', 'AYUDA_TOTAL', 'NO_DOCUMENTADO')
          OR json_extract(area.`answer_payload`, '$.swallowingPrecautionsCode') NOT IN (
            'NINGUNA_DOCUMENTADA', 'PRECAUCIONES_DOCUMENTADAS', 'NO_DOCUMENTADO')
          OR ((json_extract(area.`answer_payload`, '$.foodTextureCode') = 'NO_APLICA'
              OR json_extract(area.`answer_payload`, '$.liquidConsistencyCode') = 'NO_APLICA')
            AND json_extract(area.`answer_payload`, '$.routeCode') <> 'ENTERAL')
        WHEN 'CONTINENCIA' THEN
          json_extract(area.`answer_payload`, '$.urinationCode') NOT IN (
            'CONTINENTE', 'INCONTINENCIA_OCASIONAL', 'INCONTINENCIA_HABITUAL', 'NO_DOCUMENTADO')
          OR json_extract(area.`answer_payload`, '$.bowelCode') NOT IN (
            'CONTINENTE', 'INCONTINENCIA_OCASIONAL', 'INCONTINENCIA_HABITUAL', 'NO_DOCUMENTADO')
          OR EXISTS (SELECT 1 FROM json_each(area.`answer_payload`, '$.managementCodes')
            WHERE value NOT IN ('NINGUNO', 'ABSORBENTE', 'SONDA_URINARIA', 'UROSTOMIA',
              'COLOSTOMIA_ILEOSTOMIA', 'OTRO', 'NO_DOCUMENTADO'))
          OR (json_array_length(area.`answer_payload`, '$.managementCodes') > 1
            AND EXISTS (SELECT 1 FROM json_each(area.`answer_payload`, '$.managementCodes')
              WHERE value IN ('NINGUNO', 'NO_DOCUMENTADO')))
        WHEN 'ASEO_HIGIENE' THEN
          json_extract(area.`answer_payload`, '$.personalCareAssistanceCode') NOT IN (
            'INDEPENDIENTE', 'SUPERVISION_O_INDICACIONES', 'AYUDA_PARCIAL',
            'AYUDA_TOTAL', 'NO_DOCUMENTADO')
          OR json_extract(area.`answer_payload`, '$.bathingAssistanceCode') NOT IN (
            'INDEPENDIENTE', 'SUPERVISION', 'AYUDA_PARCIAL', 'AYUDA_TOTAL', 'NO_DOCUMENTADO')
        WHEN 'COGNICION' THEN
          json_extract(area.`answer_payload`, '$.categoryCode') NOT IN (
            'SIN_DETERIORO_CONOCIDO_O_DOCUMENTADO', 'DETERIORO_COGNITIVO_LEVE_DOCUMENTADO',
            'DEMENCIA_DOCUMENTADA', 'SITUACION_NO_DETERMINADA')
          OR (json_extract(area.`answer_payload`, '$.etiologyCode') IS NOT NULL
            AND json_extract(area.`answer_payload`, '$.categoryCode') <> 'DEMENCIA_DOCUMENTADA')
          OR (json_extract(area.`answer_payload`, '$.etiologyCode') IS NOT NULL
            AND json_extract(area.`answer_payload`, '$.etiologyCode') NOT IN (
              'ENFERMEDAD_ALZHEIMER', 'DEMENCIA_VASCULAR', 'DEMENCIA_MIXTA',
              'DEMENCIA_CON_CUERPOS_DE_LEWY', 'DEMENCIA_FRONTOTEMPORAL',
              'DEMENCIA_ASOCIADA_ENFERMEDAD_PARKINSON', 'SINDROME_CORTICOBASAL',
              'OTRA', 'ETIOLOGIA_NO_ESPECIFICADA'))
          OR (json_extract(area.`answer_payload`, '$.gdsCode') IS NOT NULL
            AND json_extract(area.`answer_payload`, '$.gdsCode') NOT IN (
              'NO_DOCUMENTADO', 'GDS_1', 'GDS_2', 'GDS_3', 'GDS_4', 'GDS_5', 'GDS_6', 'GDS_7'))
          OR ((json_extract(area.`answer_payload`, '$.etiologyCode') IS NOT NULL
              OR json_extract(area.`answer_payload`, '$.gdsCode') GLOB 'GDS_[1-7]')
            AND (json_extract(area.`answer_payload`, '$.clinicalReferenceSourceCode') IS NULL
              OR json_extract(area.`answer_payload`, '$.clinicalReferenceSourceCode') = 'NO_DOCUMENTADO'
              OR length(trim(coalesce(json_extract(area.`answer_payload`, '$.clinicalReferenceDate'), ''))) = 0))
        WHEN 'COMUNICACION' THEN
          json_extract(area.`answer_payload`, '$.comprehensionCode') NOT IN (
            'COMPRENSION_FUNCIONAL', 'NECESITA_FRASES_SENCILLAS_REPETICION_O_APOYO',
            'COMPRENSION_MUY_LIMITADA', 'NO_SE_HA_PODIDO_DETERMINAR', 'NO_DOCUMENTADO')
          OR json_extract(area.`answer_payload`, '$.expressionCode') NOT IN (
            'EXPRESA_NECESIDADES_EFICAZMENTE',
            'EXPRESION_VERBAL_LIMITADA_PERO_COMUNICA_NECESIDADES_BASICAS',
            'COMUNICACION_PRINCIPALMENTE_NO_VERBAL',
            'NO_EXPRESA_NECESIDADES_DE_FORMA_FIABLE', 'NO_DOCUMENTADO')
          OR EXISTS (SELECT 1 FROM json_each(area.`answer_payload`, '$.usualFormsCodes')
            WHERE value NOT IN ('LENGUAJE_ORAL', 'GESTOS', 'ESCRITURA',
              'TABLERO_O_DISPOSITIVO', 'OTRA', 'NO_SE_IDENTIFICA_FORMA_EFECTIVA',
              'NO_DOCUMENTADO'))
          OR (json_array_length(area.`answer_payload`, '$.usualFormsCodes') > 1
            AND EXISTS (SELECT 1 FROM json_each(area.`answer_payload`, '$.usualFormsCodes')
              WHERE value IN ('NO_SE_IDENTIFICA_FORMA_EFECTIVA', 'NO_DOCUMENTADO')))
        WHEN 'CONDUCTA' THEN
          json_extract(area.`answer_payload`, '$.statusCode') NOT IN (
            'SIN_CONDUCTAS_RELEVANTES_CONOCIDAS', 'PATRONES_CONDUCTUALES_HABITUALES',
            'NO_DOCUMENTADO')
          OR EXISTS (SELECT 1 FROM json_each(area.`answer_payload`, '$.patternCodes')
            WHERE value NOT IN ('APATIA_O_RETRAIMIENTO', 'ANIMO_BAJO_HABITUAL',
              'ANSIEDAD_O_TEMOR', 'IRRITABILIDAD', 'AGITACION_O_INQUIETUD',
              'RESISTENCIA_A_LOS_CUIDADOS', 'CONDUCTAS_O_VOCALIZACIONES_REPETITIVAS',
              'DEAMBULACION_ERRATICA_O_INTENTO_DE_SALIDA', 'AGRESIVIDAD_VERBAL',
              'AGRESIVIDAD_FISICA', 'DESINHIBICION',
              'IDEAS_DELIRANTES_O_ALUCINACIONES_DOCUMENTADAS', 'OTRA'))
          OR (json_extract(area.`answer_payload`, '$.statusCode') <> 'PATRONES_CONDUCTUALES_HABITUALES'
            AND coalesce(json_array_length(area.`answer_payload`, '$.patternCodes'), 0) <> 0)
        WHEN 'SUENO' THEN
          EXISTS (SELECT 1 FROM json_each(area.`answer_payload`, '$.patternCodes')
            WHERE value NOT IN ('PATRON_HABITUALMENTE_CONSERVADO', 'DIFICULTAD_INICIO_SUENO',
              'DESPERTARES_FRECUENTES', 'DESPERTAR_PRECOZ', 'INVERSION_SUENO_VIGILIA',
              'SOMNOLENCIA_DIURNA_HABITUAL', 'PATRON_IRREGULAR_VARIABLE', 'NO_DOCUMENTADO'))
          OR (json_array_length(area.`answer_payload`, '$.patternCodes') > 1
            AND EXISTS (SELECT 1 FROM json_each(area.`answer_payload`, '$.patternCodes')
              WHERE value IN ('PATRON_HABITUALMENTE_CONSERVADO', 'NO_DOCUMENTADO')))
        WHEN 'AYUDAS_HABITUALES' THEN
          EXISTS (SELECT 1 FROM json_each(area.`answer_payload`, '$.aidCodes')
            WHERE value NOT IN ('GAFAS', 'AUDIFONO', 'TABLERO_O_DISPOSITIVO_COMUNICACION',
              'PROTESIS_DENTAL', 'CUBIERTOS_O_VAJILLA_ADAPTADOS',
              'OTRO_PRODUCTO_DE_APOYO', 'OXIGENOTERAPIA_HABITUAL', 'CPAP_BIPAP',
              'OTRO', 'NINGUNO', 'NO_DOCUMENTADO'))
          OR (json_array_length(area.`answer_payload`, '$.aidCodes') > 1
            AND EXISTS (SELECT 1 FROM json_each(area.`answer_payload`, '$.aidCodes')
              WHERE value IN ('NINGUNO', 'NO_DOCUMENTADO')))
        ELSE 1
      END
  ) THEN RAISE(ABORT, 'BASELINE_AREA_CATALOG_INVALID') END;
  SELECT CASE WHEN NOT EXISTS (
    SELECT 1 FROM `baseline_draft_barthel` assessment
    WHERE assessment.`draft_id` = NEW.`source_draft_id`
      AND assessment.`instrument_version_code` = 'BARTHEL_COMUN_V0_1'
      AND assessment.`assessment_date` IS NOT NULL
      AND assessment.`total_score` = (
        SELECT sum(item.`awarded_score`)
        FROM `baseline_draft_barthel_items` item
        WHERE item.`barthel_id` = assessment.`id`
      )
      AND 10 = (
        SELECT count(*) FROM `baseline_draft_barthel_items` item
        WHERE item.`barthel_id` = assessment.`id`
      )
  ) THEN RAISE(ABORT, 'BASELINE_BARTHEL_INCOMPLETE') END;
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_version_areas_copy_guard`
BEFORE INSERT ON `baseline_version_areas`
WHEN NOT EXISTS (
  SELECT 1 FROM `baseline_versions` version
  JOIN `baseline_draft_areas` area ON area.`draft_id` = version.`source_draft_id`
  WHERE version.`id` = NEW.`baseline_version_id`
    AND area.`area_code` = NEW.`area_code`
    AND area.`resident_id` = NEW.`resident_id`
    AND area.`center_id` = NEW.`center_id`
    AND area.`catalog_version_code` = NEW.`catalog_version_code`
    AND area.`answer_payload` = NEW.`answer_payload`
    AND area.`observation` IS NEW.`observation`
    AND area.`information_source_override_code` IS NEW.`information_source_override_code`
    AND area.`information_source_override_other_text` IS NEW.`information_source_override_other_text`
    AND area.`information_date_override` IS NEW.`information_date_override`
    AND area.`recorded_by_account_id` = NEW.`recorded_by_account_id`
    AND area.`recorded_by_profile` = NEW.`recorded_by_profile`
    AND area.`recorded_at` = NEW.`recorded_at`
)
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_SIGNED_AREA_NOT_FROM_DRAFT');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_version_barthel_copy_guard`
BEFORE INSERT ON `baseline_version_barthel`
WHEN NOT EXISTS (
  SELECT 1 FROM `baseline_versions` version
  JOIN `baseline_draft_barthel` assessment ON assessment.`draft_id` = version.`source_draft_id`
  WHERE version.`id` = NEW.`baseline_version_id`
    AND assessment.`resident_id` = NEW.`resident_id`
    AND assessment.`center_id` = NEW.`center_id`
    AND assessment.`instrument_version_code` = NEW.`instrument_version_code`
    AND assessment.`assessment_date` = NEW.`assessment_date`
    AND assessment.`total_score` = NEW.`total_score`
    AND assessment.`recorded_by_account_id` = NEW.`recorded_by_account_id`
    AND assessment.`recorded_by_profile` = NEW.`recorded_by_profile`
    AND assessment.`recorded_at` = NEW.`recorded_at`
)
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_SIGNED_BARTHEL_NOT_FROM_DRAFT');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_version_barthel_items_copy_guard`
BEFORE INSERT ON `baseline_version_barthel_items`
WHEN NOT EXISTS (
  SELECT 1 FROM `baseline_versions` version
  JOIN `baseline_draft_barthel` assessment ON assessment.`draft_id` = version.`source_draft_id`
  JOIN `baseline_draft_barthel_items` item ON item.`barthel_id` = assessment.`id`
  WHERE version.`id` = NEW.`baseline_version_id`
    AND item.`item_code` = NEW.`item_code`
    AND item.`selected_option_code` = NEW.`selected_option_code`
    AND item.`awarded_score` = NEW.`awarded_score`
)
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_SIGNED_BARTHEL_ITEM_NOT_FROM_DRAFT');
END;
--> statement-breakpoint
CREATE TRIGGER `resident_current_baselines_validate_insert`
BEFORE INSERT ON `resident_current_baselines`
WHEN NOT EXISTS (
  SELECT 1 FROM `baseline_versions` version
  WHERE version.`id` = NEW.`baseline_version_id`
    AND version.`resident_id` = NEW.`resident_id`
    AND version.`center_id` = NEW.`center_id`
    AND version.`version_number` = 1
    AND NEW.`activated_at` = version.`valid_from`
    AND 9 = (SELECT count(*) FROM `baseline_version_areas` area
      WHERE area.`baseline_version_id` = version.`id`)
    AND 1 = (SELECT count(*) FROM `baseline_version_barthel` assessment
      WHERE assessment.`baseline_version_id` = version.`id`)
    AND 10 = (SELECT count(*) FROM `baseline_version_barthel_items` item
      WHERE item.`baseline_version_id` = version.`id`)
)
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_CURRENT_INITIAL_INVALID');
END;
--> statement-breakpoint
CREATE TRIGGER `resident_current_baselines_validate_update`
BEFORE UPDATE ON `resident_current_baselines`
WHEN NEW.`resident_id` <> OLD.`resident_id`
  OR NEW.`center_id` <> OLD.`center_id`
  OR NOT EXISTS (
    SELECT 1 FROM `baseline_supersessions` supersession
    JOIN `baseline_versions` previous
      ON previous.`id` = supersession.`previous_version_id`
     AND previous.`resident_id` = supersession.`resident_id`
     AND previous.`center_id` = supersession.`center_id`
    JOIN `baseline_versions` next
      ON next.`id` = supersession.`new_version_id`
     AND next.`resident_id` = supersession.`resident_id`
     AND next.`center_id` = supersession.`center_id`
    WHERE supersession.`previous_version_id` = OLD.`baseline_version_id`
      AND supersession.`new_version_id` = NEW.`baseline_version_id`
      AND supersession.`resident_id` = OLD.`resident_id`
      AND supersession.`center_id` = OLD.`center_id`
      AND next.`version_number` = previous.`version_number` + 1
      AND next.`valid_from` > previous.`valid_from`
      AND supersession.`superseded_at` = next.`valid_from`
      AND NEW.`activated_at` = supersession.`superseded_at`
  )
  OR NOT EXISTS (
    SELECT 1 FROM `baseline_versions` version
    WHERE version.`id` = NEW.`baseline_version_id`
      AND version.`resident_id` = NEW.`resident_id`
      AND version.`center_id` = NEW.`center_id`
      AND 9 = (SELECT count(*) FROM `baseline_version_areas` area
        WHERE area.`baseline_version_id` = version.`id`)
      AND 1 = (SELECT count(*) FROM `baseline_version_barthel` assessment
        WHERE assessment.`baseline_version_id` = version.`id`)
      AND 10 = (SELECT count(*) FROM `baseline_version_barthel_items` item
        WHERE item.`baseline_version_id` = version.`id`)
  )
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_CURRENT_TRANSITION_INVALID');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_supersessions_validate_insert`
BEFORE INSERT ON `baseline_supersessions`
WHEN NOT EXISTS (
  SELECT 1
    FROM `baseline_versions` previous
    JOIN `baseline_versions` next
      ON next.`id` = NEW.`new_version_id`
     AND next.`resident_id` = previous.`resident_id`
     AND next.`center_id` = previous.`center_id`
    JOIN `resident_current_baselines` current
      ON current.`resident_id` = previous.`resident_id`
     AND current.`center_id` = previous.`center_id`
     AND current.`baseline_version_id` = previous.`id`
   WHERE previous.`id` = NEW.`previous_version_id`
     AND previous.`resident_id` = NEW.`resident_id`
     AND previous.`center_id` = NEW.`center_id`
     AND next.`resident_id` = NEW.`resident_id`
     AND next.`center_id` = NEW.`center_id`
     AND next.`version_number` = previous.`version_number` + 1
     AND next.`signed_at` > previous.`signed_at`
     AND next.`valid_from` > previous.`valid_from`
     AND NEW.`superseded_at` = next.`valid_from`
     AND NEW.`superseded_at` > previous.`signed_at`
     AND NEW.`superseded_at` > previous.`valid_from`
)
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_SUPERSESSION_INVALID');
END;
--> statement-breakpoint
CREATE TRIGGER `resident_current_baselines_no_delete`
BEFORE DELETE ON `resident_current_baselines`
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_CURRENT_DELETE_FORBIDDEN');
END;
--> statement-breakpoint
CREATE TRIGGER `idempotency_operations_transition_guard`
BEFORE UPDATE ON `idempotency_operations`
WHEN OLD.`status` <> 'IN_PROGRESS'
  OR NEW.`id` <> OLD.`id`
  OR NEW.`account_id` <> OLD.`account_id`
  OR NEW.`action_code` <> OLD.`action_code`
  OR NEW.`operation_id` <> OLD.`operation_id`
  OR NEW.`request_hash` <> OLD.`request_hash`
  OR NEW.`created_at` <> OLD.`created_at`
  OR NEW.`status` <> 'SUCCEEDED'
BEGIN
  SELECT RAISE(ABORT, 'IDEMPOTENCY_OPERATION_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `idempotency_operations_no_delete`
BEFORE DELETE ON `idempotency_operations`
BEGIN
  SELECT RAISE(ABORT, 'IDEMPOTENCY_OPERATION_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `profile_scopes_revoke_only`
BEFORE UPDATE ON `profile_scopes`
WHEN OLD.`status` <> 'ACTIVE'
  OR NEW.`id` <> OLD.`id`
  OR NEW.`account_id` <> OLD.`account_id`
  OR NEW.`center_id` <> OLD.`center_id`
  OR NEW.`profile_code` <> OLD.`profile_code`
  OR NEW.`granted_at` <> OLD.`granted_at`
  OR NEW.`granted_by_account_id` <> OLD.`granted_by_account_id`
  OR NEW.`status` <> 'REVOKED'
BEGIN
  SELECT RAISE(ABORT, 'PROFILE_SCOPE_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `profile_scopes_no_delete`
BEFORE DELETE ON `profile_scopes`
BEGIN
  SELECT RAISE(ABORT, 'PROFILE_SCOPE_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `profile_permissions_revoke_only`
BEFORE UPDATE ON `profile_permissions`
WHEN OLD.`revoked_at` IS NOT NULL
  OR NEW.`id` <> OLD.`id`
  OR NEW.`profile_scope_id` <> OLD.`profile_scope_id`
  OR NEW.`center_id` <> OLD.`center_id`
  OR NEW.`permission_code` <> OLD.`permission_code`
  OR NEW.`granted_at` <> OLD.`granted_at`
  OR NEW.`granted_by_account_id` <> OLD.`granted_by_account_id`
  OR NEW.`revoked_at` IS NULL
BEGIN
  SELECT RAISE(ABORT, 'PROFILE_PERMISSION_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `profile_permissions_no_delete`
BEFORE DELETE ON `profile_permissions`
BEGIN
  SELECT RAISE(ABORT, 'PROFILE_PERMISSION_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `profile_unit_scopes_revoke_only`
BEFORE UPDATE ON `profile_unit_scopes`
WHEN OLD.`revoked_at` IS NOT NULL
  OR NEW.`id` <> OLD.`id`
  OR NEW.`profile_scope_id` <> OLD.`profile_scope_id`
  OR NEW.`center_id` <> OLD.`center_id`
  OR NEW.`unit_id` <> OLD.`unit_id`
  OR NEW.`granted_at` <> OLD.`granted_at`
  OR NEW.`granted_by_account_id` <> OLD.`granted_by_account_id`
  OR NEW.`revoked_at` IS NULL
BEGIN
  SELECT RAISE(ABORT, 'PROFILE_UNIT_SCOPE_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `profile_unit_scopes_no_delete`
BEFORE DELETE ON `profile_unit_scopes`
BEGIN
  SELECT RAISE(ABORT, 'PROFILE_UNIT_SCOPE_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `profile_resident_scopes_revoke_only`
BEFORE UPDATE ON `profile_resident_scopes`
WHEN OLD.`revoked_at` IS NOT NULL
  OR NEW.`id` <> OLD.`id`
  OR NEW.`profile_scope_id` <> OLD.`profile_scope_id`
  OR NEW.`center_id` <> OLD.`center_id`
  OR NEW.`resident_id` <> OLD.`resident_id`
  OR NEW.`granted_at` <> OLD.`granted_at`
  OR NEW.`granted_by_account_id` <> OLD.`granted_by_account_id`
  OR NEW.`revoked_at` IS NULL
BEGIN
  SELECT RAISE(ABORT, 'PROFILE_RESIDENT_SCOPE_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `profile_resident_scopes_no_delete`
BEFORE DELETE ON `profile_resident_scopes`
BEGIN
  SELECT RAISE(ABORT, 'PROFILE_RESIDENT_SCOPE_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_versions_no_update`
BEFORE UPDATE ON `baseline_versions`
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_VERSION_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_versions_no_delete`
BEFORE DELETE ON `baseline_versions`
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_VERSION_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_version_areas_no_update`
BEFORE UPDATE ON `baseline_version_areas`
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_VERSION_AREA_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_version_areas_no_delete`
BEFORE DELETE ON `baseline_version_areas`
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_VERSION_AREA_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_version_barthel_no_update`
BEFORE UPDATE ON `baseline_version_barthel`
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_VERSION_BARTHEL_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_version_barthel_no_delete`
BEFORE DELETE ON `baseline_version_barthel`
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_VERSION_BARTHEL_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_version_barthel_items_no_update`
BEFORE UPDATE ON `baseline_version_barthel_items`
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_VERSION_BARTHEL_ITEM_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_version_barthel_items_no_delete`
BEFORE DELETE ON `baseline_version_barthel_items`
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_VERSION_BARTHEL_ITEM_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_supersessions_no_update`
BEFORE UPDATE ON `baseline_supersessions`
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_SUPERSESSION_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_supersessions_no_delete`
BEFORE DELETE ON `baseline_supersessions`
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_SUPERSESSION_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_draft_contributions_no_update`
BEFORE UPDATE ON `baseline_draft_contributions`
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_CONTRIBUTION_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_draft_contributions_insert_only_while_active`
BEFORE INSERT ON `baseline_draft_contributions`
WHEN NOT EXISTS (
  SELECT 1 FROM `baseline_drafts` draft
  WHERE draft.`id` = NEW.`draft_id` AND draft.`status` = 'ACTIVE'
)
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_CONTRIBUTION_PARENT_NOT_ACTIVE');
END;
--> statement-breakpoint
CREATE TRIGGER `baseline_draft_contributions_no_delete`
BEFORE DELETE ON `baseline_draft_contributions`
BEGIN
  SELECT RAISE(ABORT, 'BASELINE_CONTRIBUTION_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `audit_events_no_update`
BEFORE UPDATE ON `audit_events`
BEGIN
  SELECT RAISE(ABORT, 'AUDIT_EVENT_IMMUTABLE');
END;
--> statement-breakpoint
CREATE TRIGGER `audit_events_no_delete`
BEFORE DELETE ON `audit_events`
BEGIN
  SELECT RAISE(ABORT, 'AUDIT_EVENT_IMMUTABLE');
END;
