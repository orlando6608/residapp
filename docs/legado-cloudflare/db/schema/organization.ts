import { sql } from "drizzle-orm";
import {
  check,
  foreignKey,
  index,
  integer,
  sqliteTable,
  text,
  uniqueIndex,
} from "drizzle-orm/sqlite-core";

export const centers = sqliteTable(
  "centers",
  {
    id: text("id").primaryKey(),
    code: text("code").notNull(),
    displayName: text("display_name").notNull(),
    status: text("status").notNull().default("ACTIVE"),
    createdAt: text("created_at").notNull(),
  },
  (table) => [
    uniqueIndex("centers_code_unique").on(table.code),
    check("centers_status_check", sql`${table.status} in ('ACTIVE', 'INACTIVE')`),
  ],
);

export const centerLocationConfigVersions = sqliteTable(
  "center_location_config_versions",
  {
    id: text("id").primaryKey(),
    centerId: text("center_id")
      .notNull()
      .references(() => centers.id, { onDelete: "restrict" }),
    versionNumber: integer("version_number").notNull(),
    roomsEnabled: integer("rooms_enabled", { mode: "boolean" }).notNull(),
    roomsRequired: integer("rooms_required", { mode: "boolean" }).notNull(),
    placesEnabled: integer("places_enabled", { mode: "boolean" }).notNull(),
    placesRequired: integer("places_required", { mode: "boolean" }).notNull(),
    validFrom: text("valid_from").notNull(),
    validUntil: text("valid_until"),
    createdAt: text("created_at").notNull(),
  },
  (table) => [
    uniqueIndex("center_location_config_version_unique").on(
      table.centerId,
      table.versionNumber,
    ),
    uniqueIndex("center_location_config_current_unique")
      .on(table.centerId)
      .where(sql`${table.validUntil} is null`),
    index("center_location_config_lookup_idx").on(
      table.centerId,
      table.validFrom,
      table.validUntil,
    ),
    check(
      "center_location_config_flags_check",
      sql`(${table.roomsRequired} = 0 or ${table.roomsEnabled} = 1)
        and (${table.placesRequired} = 0 or (${table.placesEnabled} = 1 and ${table.roomsEnabled} = 1))
        and (${table.placesEnabled} = 0 or ${table.roomsEnabled} = 1)`,
    ),
    check(
      "center_location_config_time_check",
      sql`${table.validUntil} is null or ${table.validUntil} > ${table.validFrom}`,
    ),
  ],
);

export const buildings = sqliteTable(
  "buildings",
  {
    id: text("id").primaryKey(),
    centerId: text("center_id")
      .notNull()
      .references(() => centers.id, { onDelete: "restrict" }),
    code: text("code").notNull(),
    displayName: text("display_name").notNull(),
    status: text("status").notNull().default("ACTIVE"),
    createdAt: text("created_at").notNull(),
  },
  (table) => [
    uniqueIndex("buildings_center_code_unique").on(table.centerId, table.code),
    uniqueIndex("buildings_center_id_unique").on(table.centerId, table.id),
    check("buildings_status_check", sql`${table.status} in ('ACTIVE', 'INACTIVE')`),
  ],
);

export const floors = sqliteTable(
  "floors",
  {
    id: text("id").primaryKey(),
    centerId: text("center_id").notNull(),
    buildingId: text("building_id").notNull(),
    code: text("code").notNull(),
    displayName: text("display_name").notNull(),
    status: text("status").notNull().default("ACTIVE"),
    createdAt: text("created_at").notNull(),
  },
  (table) => [
    foreignKey({
      columns: [table.centerId, table.buildingId],
      foreignColumns: [buildings.centerId, buildings.id],
      name: "floors_building_center_fk",
    }).onDelete("restrict"),
    uniqueIndex("floors_building_code_unique").on(table.buildingId, table.code),
    uniqueIndex("floors_center_building_id_unique").on(
      table.centerId,
      table.buildingId,
      table.id,
    ),
    check("floors_status_check", sql`${table.status} in ('ACTIVE', 'INACTIVE')`),
  ],
);

export const units = sqliteTable(
  "units",
  {
    id: text("id").primaryKey(),
    centerId: text("center_id")
      .notNull()
      .references(() => centers.id, { onDelete: "restrict" }),
    buildingId: text("building_id"),
    floorId: text("floor_id"),
    code: text("code").notNull(),
    displayName: text("display_name").notNull(),
    status: text("status").notNull().default("ACTIVE"),
    createdAt: text("created_at").notNull(),
  },
  (table) => [
    foreignKey({
      columns: [table.centerId, table.buildingId],
      foreignColumns: [buildings.centerId, buildings.id],
      name: "units_building_center_fk",
    }).onDelete("restrict"),
    foreignKey({
      columns: [table.centerId, table.buildingId, table.floorId],
      foreignColumns: [floors.centerId, floors.buildingId, floors.id],
      name: "units_floor_building_center_fk",
    }).onDelete("restrict"),
    uniqueIndex("units_center_code_unique").on(table.centerId, table.code),
    uniqueIndex("units_center_id_unique").on(table.centerId, table.id),
    check(
      "units_floor_requires_building_check",
      sql`${table.floorId} is null or ${table.buildingId} is not null`,
    ),
    check("units_status_check", sql`${table.status} in ('ACTIVE', 'INACTIVE')`),
  ],
);

export const rooms = sqliteTable(
  "rooms",
  {
    id: text("id").primaryKey(),
    centerId: text("center_id").notNull(),
    unitId: text("unit_id").notNull(),
    code: text("code").notNull(),
    displayName: text("display_name").notNull(),
    status: text("status").notNull().default("ACTIVE"),
    createdAt: text("created_at").notNull(),
  },
  (table) => [
    foreignKey({
      columns: [table.centerId, table.unitId],
      foreignColumns: [units.centerId, units.id],
      name: "rooms_unit_center_fk",
    }).onDelete("restrict"),
    uniqueIndex("rooms_unit_code_unique").on(table.unitId, table.code),
    uniqueIndex("rooms_center_unit_id_unique").on(table.centerId, table.unitId, table.id),
    uniqueIndex("rooms_center_id_unique").on(table.centerId, table.id),
    check("rooms_status_check", sql`${table.status} in ('ACTIVE', 'INACTIVE')`),
  ],
);

export const places = sqliteTable(
  "places",
  {
    id: text("id").primaryKey(),
    centerId: text("center_id").notNull(),
    roomId: text("room_id").notNull(),
    code: text("code").notNull(),
    displayName: text("display_name").notNull(),
    status: text("status").notNull().default("ACTIVE"),
    createdAt: text("created_at").notNull(),
  },
  (table) => [
    foreignKey({
      columns: [table.centerId, table.roomId],
      foreignColumns: [rooms.centerId, rooms.id],
      name: "places_room_center_fk",
    }).onDelete("restrict"),
    uniqueIndex("places_room_code_unique").on(table.roomId, table.code),
    uniqueIndex("places_center_room_id_unique").on(table.centerId, table.roomId, table.id),
    check("places_status_check", sql`${table.status} in ('ACTIVE', 'INACTIVE')`),
  ],
);

export const accounts = sqliteTable(
  "accounts",
  {
    id: text("id").primaryKey(),
    externalSubject: text("external_subject").notNull(),
    status: text("status").notNull().default("ACTIVE"),
    createdAt: text("created_at").notNull(),
  },
  (table) => [
    uniqueIndex("accounts_external_subject_unique").on(table.externalSubject),
    check("accounts_status_check", sql`${table.status} in ('ACTIVE', 'SUSPENDED')`),
  ],
);
