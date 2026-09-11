import { sql } from "drizzle-orm";
import {
  check,
  foreignKey,
  index,
  sqliteTable,
  text,
  uniqueIndex,
} from "drizzle-orm/sqlite-core";

import { accounts, centers, units } from "./organization.ts";
import { residents } from "./residents.ts";

export const idempotencyOperations = sqliteTable(
  "idempotency_operations",
  {
    id: text("id").primaryKey(),
    accountId: text("account_id")
      .notNull()
      .references(() => accounts.id, { onDelete: "restrict" }),
    actionCode: text("action_code").notNull(),
    operationId: text("operation_id").notNull(),
    requestHash: text("request_hash").notNull(),
    status: text("status").notNull().default("IN_PROGRESS"),
    resultResourceId: text("result_resource_id"),
    resultJson: text("result_json"),
    createdAt: text("created_at").notNull(),
    completedAt: text("completed_at"),
  },
  (table) => [
    uniqueIndex("idempotency_operation_unique").on(
      table.accountId,
      table.actionCode,
      table.operationId,
    ),
    check(
      "idempotency_action_check",
      sql`${table.actionCode} in ('RESIDENT_CREATE', 'BASELINE_SIGN', 'CLINICAL_DETAIL_READ')`,
    ),
    check(
      "idempotency_status_check",
      sql`${table.status} in ('IN_PROGRESS', 'SUCCEEDED')`,
    ),
    check(
      "idempotency_result_check",
      sql`(${table.status} = 'IN_PROGRESS'
          and ${table.resultResourceId} is null
          and ${table.resultJson} is null
          and ${table.completedAt} is null)
        or (${table.status} = 'SUCCEEDED'
          and ${table.resultResourceId} is not null
          and ${table.resultJson} is not null
          and json_valid(${table.resultJson})
          and ${table.completedAt} is not null)`,
    ),
  ],
);

export const auditEvents = sqliteTable(
  "audit_events",
  {
    id: text("id").primaryKey(),
    accountId: text("account_id")
      .notNull()
      .references(() => accounts.id, { onDelete: "restrict" }),
    activeProfile: text("active_profile").notNull(),
    centerId: text("center_id")
      .notNull()
      .references(() => centers.id, { onDelete: "restrict" }),
    unitId: text("unit_id"),
    residentId: text("resident_id"),
    resourceType: text("resource_type").notNull(),
    resourceId: text("resource_id").notNull(),
    actionCode: text("action_code").notNull(),
    purposeCode: text("purpose_code"),
    occurredAt: text("occurred_at").notNull(),
  },
  (table) => [
    foreignKey({
      columns: [table.centerId, table.unitId],
      foreignColumns: [units.centerId, units.id],
      name: "audit_events_unit_center_fk",
    }).onDelete("restrict"),
    foreignKey({
      columns: [table.centerId, table.residentId],
      foreignColumns: [residents.centerId, residents.id],
      name: "audit_events_resident_center_fk",
    }).onDelete("restrict"),
    index("audit_events_scope_time_idx").on(
      table.centerId,
      table.unitId,
      table.residentId,
      table.occurredAt,
    ),
    check(
      "audit_events_profile_check",
      sql`${table.activeProfile} in ('AUXILIAR', 'ENFERMERIA', 'MEDICINA', 'FAMILIAR', 'ADMINISTRACION', 'DIRECCION_CLINICA')`,
    ),
    check(
      "audit_events_direction_read_check",
      sql`${table.actionCode} <> 'CLINICAL_DETAIL_READ'
        or (${table.activeProfile} = 'DIRECCION_CLINICA'
          and ${table.purposeCode} = 'SUPERVISION_CLINICA'
          and ${table.unitId} is not null
          and ${table.residentId} is not null)`,
    ),
  ],
);
