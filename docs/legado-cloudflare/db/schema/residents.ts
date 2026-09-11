import { sql } from "drizzle-orm";
import {
  check,
  foreignKey,
  index,
  sqliteTable,
  text,
  uniqueIndex,
} from "drizzle-orm/sqlite-core";

import { accounts, buildings, centers, floors, places, rooms, units } from "./organization.ts";

export const residents = sqliteTable(
  "residents",
  {
    id: text("id").primaryKey(),
    centerId: text("center_id")
      .notNull()
      .references(() => centers.id, { onDelete: "restrict" }),
    displayName: text("display_name").notNull(),
    birthDate: text("birth_date").notNull(),
    documentedSexCode: text("documented_sex_code").notNull(),
    status: text("status").notNull().default("ACTIVE"),
    inactivationReason: text("inactivation_reason"),
    inactivatedAt: text("inactivated_at"),
    inactivatedByAccountId: text("inactivated_by_account_id").references(() => accounts.id, {
      onDelete: "restrict",
    }),
    inactivatedByProfile: text("inactivated_by_profile"),
    createdAt: text("created_at").notNull(),
    createdByAccountId: text("created_by_account_id")
      .notNull()
      .references(() => accounts.id, { onDelete: "restrict" }),
    createdByProfile: text("created_by_profile").notNull(),
  },
  (table) => [
    uniqueIndex("residents_center_id_unique").on(table.centerId, table.id),
    index("residents_center_status_idx").on(table.centerId, table.status, table.id),
    check(
      "residents_documented_sex_check",
      sql`${table.documentedSexCode} in ('male', 'female', 'other', 'unknown')`,
    ),
    check("residents_status_check", sql`${table.status} in ('ACTIVE', 'INACTIVE')`),
    check(
      "residents_created_profile_check",
      sql`${table.createdByProfile} in ('ADMINISTRACION', 'ENFERMERIA')`,
    ),
    check(
      "residents_inactivation_fields_check",
      sql`(${table.status} = 'ACTIVE'
          and ${table.inactivationReason} is null
          and ${table.inactivatedAt} is null
          and ${table.inactivatedByAccountId} is null
          and ${table.inactivatedByProfile} is null)
        or (${table.status} = 'INACTIVE'
          and length(trim(${table.inactivationReason})) > 0
          and ${table.inactivatedAt} is not null
          and ${table.inactivatedByAccountId} is not null
          and ${table.inactivatedByProfile} = 'ADMINISTRACION')`,
    ),
  ],
);

export const residentCenterEpisodes = sqliteTable(
  "resident_center_episodes",
  {
    id: text("id").primaryKey(),
    residentId: text("resident_id").notNull(),
    centerId: text("center_id").notNull(),
    internalReference: text("internal_reference"),
    validFrom: text("valid_from").notNull(),
    validUntil: text("valid_until"),
    createdAt: text("created_at").notNull(),
    createdByAccountId: text("created_by_account_id")
      .notNull()
      .references(() => accounts.id, { onDelete: "restrict" }),
    createdByProfile: text("created_by_profile").notNull(),
  },
  (table) => [
    foreignKey({
      columns: [table.centerId, table.residentId],
      foreignColumns: [residents.centerId, residents.id],
      name: "resident_center_episodes_resident_center_fk",
    }).onDelete("restrict"),
    uniqueIndex("resident_center_episodes_scope_unique").on(
      table.id,
      table.residentId,
      table.centerId,
    ),
    uniqueIndex("resident_center_episodes_active_unique")
      .on(table.residentId)
      .where(sql`${table.validUntil} is null`),
    index("resident_center_episodes_lookup_idx").on(
      table.centerId,
      table.residentId,
      table.validFrom,
    ),
    check(
      "resident_center_episodes_time_check",
      sql`${table.validUntil} is null or ${table.validUntil} > ${table.validFrom}`,
    ),
    check(
      "resident_center_episodes_profile_check",
      sql`${table.createdByProfile} in ('ADMINISTRACION', 'ENFERMERIA')`,
    ),
  ],
);

export const residentLocationIntervals = sqliteTable(
  "resident_location_intervals",
  {
    id: text("id").primaryKey(),
    residentId: text("resident_id").notNull(),
    centerId: text("center_id").notNull(),
    episodeId: text("episode_id").notNull(),
    unitId: text("unit_id").notNull(),
    buildingId: text("building_id"),
    floorId: text("floor_id"),
    roomId: text("room_id"),
    placeId: text("place_id"),
    validFrom: text("valid_from").notNull(),
    validUntil: text("valid_until"),
    changedAt: text("changed_at").notNull(),
    changedByAccountId: text("changed_by_account_id")
      .notNull()
      .references(() => accounts.id, { onDelete: "restrict" }),
    changedByProfile: text("changed_by_profile").notNull(),
  },
  (table) => [
    foreignKey({
      columns: [table.episodeId, table.residentId, table.centerId],
      foreignColumns: [
        residentCenterEpisodes.id,
        residentCenterEpisodes.residentId,
        residentCenterEpisodes.centerId,
      ],
      name: "resident_location_intervals_episode_scope_fk",
    }).onDelete("restrict"),
    foreignKey({
      columns: [table.centerId, table.unitId],
      foreignColumns: [units.centerId, units.id],
      name: "resident_location_intervals_unit_center_fk",
    }).onDelete("restrict"),
    foreignKey({
      columns: [table.centerId, table.buildingId],
      foreignColumns: [buildings.centerId, buildings.id],
      name: "resident_location_intervals_building_center_fk",
    }).onDelete("restrict"),
    foreignKey({
      columns: [table.centerId, table.buildingId, table.floorId],
      foreignColumns: [floors.centerId, floors.buildingId, floors.id],
      name: "resident_location_intervals_floor_branch_fk",
    }).onDelete("restrict"),
    foreignKey({
      columns: [table.centerId, table.unitId, table.roomId],
      foreignColumns: [rooms.centerId, rooms.unitId, rooms.id],
      name: "resident_location_intervals_room_unit_fk",
    }).onDelete("restrict"),
    foreignKey({
      columns: [table.centerId, table.roomId, table.placeId],
      foreignColumns: [places.centerId, places.roomId, places.id],
      name: "resident_location_intervals_place_room_fk",
    }).onDelete("restrict"),
    uniqueIndex("resident_location_intervals_active_unique")
      .on(table.residentId)
      .where(sql`${table.validUntil} is null`),
    index("resident_location_intervals_current_lookup_idx").on(
      table.centerId,
      table.unitId,
      table.residentId,
      table.validUntil,
    ),
    check(
      "resident_location_intervals_time_check",
      sql`${table.validUntil} is null or ${table.validUntil} > ${table.validFrom}`,
    ),
    check(
      "resident_location_intervals_hierarchy_check",
      sql`(${table.floorId} is null or ${table.buildingId} is not null)
        and (${table.placeId} is null or ${table.roomId} is not null)`,
    ),
    check(
      "resident_location_intervals_profile_check",
      sql`${table.changedByProfile} in ('ADMINISTRACION', 'ENFERMERIA')`,
    ),
  ],
);
