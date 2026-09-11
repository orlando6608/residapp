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

export const profileScopes = sqliteTable(
  "profile_scopes",
  {
    id: text("id").primaryKey(),
    accountId: text("account_id")
      .notNull()
      .references(() => accounts.id, { onDelete: "restrict" }),
    centerId: text("center_id")
      .notNull()
      .references(() => centers.id, { onDelete: "restrict" }),
    profileCode: text("profile_code").notNull(),
    status: text("status").notNull().default("ACTIVE"),
    grantedAt: text("granted_at").notNull(),
    grantedByAccountId: text("granted_by_account_id")
      .notNull()
      .references(() => accounts.id, { onDelete: "restrict" }),
    revokedAt: text("revoked_at"),
    revokedByAccountId: text("revoked_by_account_id").references(() => accounts.id, {
      onDelete: "restrict",
    }),
  },
  (table) => [
    uniqueIndex("profile_scopes_id_center_unique").on(table.id, table.centerId),
    uniqueIndex("profile_scopes_active_unique")
      .on(table.accountId, table.centerId, table.profileCode)
      .where(sql`${table.status} = 'ACTIVE'`),
    index("profile_scopes_authorization_lookup_idx").on(
      table.centerId,
      table.profileCode,
      table.accountId,
      table.status,
    ),
    check(
      "profile_scopes_profile_check",
      sql`${table.profileCode} in ('AUXILIAR', 'ENFERMERIA', 'MEDICINA', 'FAMILIAR', 'ADMINISTRACION', 'DIRECCION_CLINICA')`,
    ),
    check("profile_scopes_status_check", sql`${table.status} in ('ACTIVE', 'REVOKED')`),
    check(
      "profile_scopes_revocation_check",
      sql`(${table.status} = 'ACTIVE' and ${table.revokedAt} is null and ${table.revokedByAccountId} is null)
        or (${table.status} = 'REVOKED' and ${table.revokedAt} is not null and ${table.revokedByAccountId} is not null)`,
    ),
  ],
);

export const profileUnitScopes = sqliteTable(
  "profile_unit_scopes",
  {
    id: text("id").primaryKey(),
    profileScopeId: text("profile_scope_id").notNull(),
    centerId: text("center_id").notNull(),
    unitId: text("unit_id").notNull(),
    grantedAt: text("granted_at").notNull(),
    grantedByAccountId: text("granted_by_account_id")
      .notNull()
      .references(() => accounts.id, { onDelete: "restrict" }),
    revokedAt: text("revoked_at"),
    revokedByAccountId: text("revoked_by_account_id").references(() => accounts.id, {
      onDelete: "restrict",
    }),
  },
  (table) => [
    foreignKey({
      columns: [table.profileScopeId, table.centerId],
      foreignColumns: [profileScopes.id, profileScopes.centerId],
      name: "profile_unit_scopes_profile_center_fk",
    }).onDelete("restrict"),
    foreignKey({
      columns: [table.centerId, table.unitId],
      foreignColumns: [units.centerId, units.id],
      name: "profile_unit_scopes_unit_center_fk",
    }).onDelete("restrict"),
    uniqueIndex("profile_unit_scopes_active_unique")
      .on(table.profileScopeId, table.unitId)
      .where(sql`${table.revokedAt} is null`),
    index("profile_unit_scopes_lookup_idx").on(
      table.centerId,
      table.unitId,
      table.profileScopeId,
      table.revokedAt,
    ),
    check(
      "profile_unit_scopes_revocation_check",
      sql`(${table.revokedAt} is null and ${table.revokedByAccountId} is null)
        or (${table.revokedAt} is not null and ${table.revokedByAccountId} is not null)`,
    ),
  ],
);

export const profileResidentScopes = sqliteTable(
  "profile_resident_scopes",
  {
    id: text("id").primaryKey(),
    profileScopeId: text("profile_scope_id").notNull(),
    centerId: text("center_id").notNull(),
    residentId: text("resident_id").notNull(),
    grantedAt: text("granted_at").notNull(),
    grantedByAccountId: text("granted_by_account_id")
      .notNull()
      .references(() => accounts.id, { onDelete: "restrict" }),
    revokedAt: text("revoked_at"),
    revokedByAccountId: text("revoked_by_account_id").references(() => accounts.id, {
      onDelete: "restrict",
    }),
  },
  (table) => [
    foreignKey({
      columns: [table.profileScopeId, table.centerId],
      foreignColumns: [profileScopes.id, profileScopes.centerId],
      name: "profile_resident_scopes_profile_center_fk",
    }).onDelete("restrict"),
    foreignKey({
      columns: [table.centerId, table.residentId],
      foreignColumns: [residents.centerId, residents.id],
      name: "profile_resident_scopes_resident_center_fk",
    }).onDelete("restrict"),
    uniqueIndex("profile_resident_scopes_active_unique")
      .on(table.profileScopeId, table.residentId)
      .where(sql`${table.revokedAt} is null`),
    index("profile_resident_scopes_lookup_idx").on(
      table.centerId,
      table.residentId,
      table.profileScopeId,
      table.revokedAt,
    ),
    check(
      "profile_resident_scopes_revocation_check",
      sql`(${table.revokedAt} is null and ${table.revokedByAccountId} is null)
        or (${table.revokedAt} is not null and ${table.revokedByAccountId} is not null)`,
    ),
  ],
);

export const profilePermissions = sqliteTable(
  "profile_permissions",
  {
    id: text("id").primaryKey(),
    profileScopeId: text("profile_scope_id").notNull(),
    centerId: text("center_id").notNull(),
    permissionCode: text("permission_code").notNull(),
    grantedAt: text("granted_at").notNull(),
    grantedByAccountId: text("granted_by_account_id")
      .notNull()
      .references(() => accounts.id, { onDelete: "restrict" }),
    revokedAt: text("revoked_at"),
    revokedByAccountId: text("revoked_by_account_id").references(() => accounts.id, {
      onDelete: "restrict",
    }),
  },
  (table) => [
    foreignKey({
      columns: [table.profileScopeId, table.centerId],
      foreignColumns: [profileScopes.id, profileScopes.centerId],
      name: "profile_permissions_profile_center_fk",
    }).onDelete("restrict"),
    uniqueIndex("profile_permissions_active_unique")
      .on(table.profileScopeId, table.permissionCode)
      .where(sql`${table.revokedAt} is null`),
    index("profile_permissions_lookup_idx").on(
      table.centerId,
      table.profileScopeId,
      table.permissionCode,
      table.revokedAt,
    ),
    check(
      "profile_permissions_code_check",
      sql`${table.permissionCode} in ('RESIDENT_IDENTITY_CREATE', 'BASELINE_INITIAL_COMPLETE', 'BASELINE_REEVALUATE', 'BASELINE_DRAFT_CONTRIBUTE', 'CLINICAL_DETAIL_READ')`,
    ),
    check(
      "profile_permissions_revocation_check",
      sql`(${table.revokedAt} is null and ${table.revokedByAccountId} is null)
        or (${table.revokedAt} is not null and ${table.revokedByAccountId} is not null)`,
    ),
  ],
);
