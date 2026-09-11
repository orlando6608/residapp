import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { convertV4MiniflareOptions, Miniflare } from "miniflare";

import type { D1DatabaseLike, D1PreparedStatementLike, D1ResultLike } from "../db/repositories/d1.ts";
import { createResidentBaselineService } from "../lib/application/resident-baseline-service.ts";
import * as capabilityApi from "../lib/authorization/request-context.ts";
import {
  executeBaselineSign, executeDirectionBaselineRead, executeResidentCreate, resolveRequestContext,
  type RequestAuthorizationContext,
} from "../lib/authorization/request-context.ts";
import { createSyntheticSessionProvider } from "../lib/session/synthetic-session-provider.ts";
import type { SessionIdentityProvider } from "../lib/session/session-provider.ts";
import { createCompleteDraft, createCompleteDraftD1, IDS, seedFoundation, seedFoundationD1 } from "./fixtures/db-fixtures.ts";
import { applyInitialMigration, SqliteD1Database } from "./support/sqlite-d1.ts";

const scopes = {
  nurse: "60000000-0000-4000-8000-000000000001",
  otherNurse: "60000000-0000-4000-8000-000000000002",
  direction: "60000000-0000-4000-8000-000000000003",
  admin: "60000000-0000-4000-8000-000000000004",
};
const unknownId = "90000000-0000-4000-8000-000000000099";
const now = "2026-09-07T08:00:00.000Z";
const denied = { ok: false, error: { code: "ACCESS_DENIED", message: "No se puede acceder a esta operación." } };
const verified = (externalSubject: string): SessionIdentityProvider => ({
  async getVerifiedIdentity() { return { externalSubject }; },
});
const createInput = (overrides = {}) => ({
  profileScopeId: scopes.admin, centerId: IDS.centerA, unitId: IDS.unitA,
  displayName: "Residente Sintético Nuevo", birthDate: "1940-01-01", documentedSexCode: "unknown",
  operationId: "71000000-0000-4000-8000-000000000001", ...overrides,
});
const signInput = (overrides = {}) => ({
  profileScopeId: scopes.nurse, centerId: IDS.centerA, residentId: IDS.residentA,
  draftId: IDS.draftA, expectedDraftRevision: 1,
  operationId: "70000000-0000-4000-8000-000000000001", ...overrides,
});
const readInput = (overrides = {}) => ({
  profileScopeId: scopes.direction, centerId: IDS.centerA, residentId: IDS.residentA,
  resourceType: "BASELINE_CURRENT", purpose: "SUPERVISION_CLINICA", ...overrides,
});

async function capability<Kind extends "CREATE" | "SIGN" | "READ">(db: D1DatabaseLike, kind: Kind) {
  return resolveRequestContext({ database: db,
    session: verified(kind === "READ" ? "synthetic-direction" : "synthetic-nurse-a"),
    selection: { profileScopeId: kind === "READ" ? scopes.direction : scopes.nurse, centerId: IDS.centerA },
    target: kind === "CREATE" ? { kind: "CREATE", unitId: IDS.unitA } :
      kind === "SIGN" ? { kind: "SIGN", residentId: IDS.residentA, draftId: IDS.draftA } : { kind: "READ", residentId: IDS.residentA },
    resourceType: "BASELINE_CURRENT", purpose: "SUPERVISION_CLINICA",
  }) as Promise<RequestAuthorizationContext<Kind>>;
}

async function withDb(run: (db: SqliteD1Database) => Promise<void>) {
  const db = new SqliteD1Database();
  try { applyInitialMigration(db.raw); seedFoundation(db.raw); await run(db); }
  finally { db.close(); }
}
function counts(db: SqliteD1Database) {
  return ["residents", "resident_center_episodes", "resident_location_intervals", "baseline_versions",
    "resident_current_baselines", "audit_events", "idempotency_operations"].map((table) =>
    (db.raw.prepare(`select count(*) as n from ${table}`).get() as { n: number }).n);
}
function revoke(db: SqliteD1Database, table: string, scope: string, code?: string) {
  assert.ok(["profile_scopes", "profile_unit_scopes", "profile_resident_scopes", "profile_permissions"].includes(table));
  db.raw.prepare(`update ${table} set revoked_at = ?, revoked_by_account_id = ?
    ${table === "profile_scopes" ? ", status = 'REVOKED'" : ""}
    where ${table === "profile_scopes" ? "id" : "profile_scope_id"} = ?
    ${code ? "and permission_code = ?" : ""}`).run(now, IDS.admin, scope, ...(code ? [code] : []));
}
function restrictResident(db: SqliteD1Database, scope: string, residentId: string = IDS.residentA) {
  db.raw.prepare(`insert into profile_resident_scopes
    (id, profile_scope_id, center_id, resident_id, granted_at, granted_by_account_id)
    values (?, ?, ?, ?, ?, ?)`).run(crypto.randomUUID(), scope, IDS.centerA, residentId, now, IDS.admin);
}

test("BOUNDARY-T01 ausencia de identidad: denegación y ninguna escritura", async () => withDb(async (db) => {
  const before = counts(db);
  assert.deepEqual(await createResidentBaselineService(db).createResident(createInput()), denied);
  assert.deepEqual(counts(db), before);
}));

test("BOUNDARY-T02 identidad desconocida: misma denegación externa", async () => withDb(async (db) => {
  assert.deepEqual(await createResidentBaselineService(db, verified("synthetic-unknown")).createResident(createInput()), denied);
}));

test("BOUNDARY-T03 cuenta suspendida no opera aunque conserve perfil y permisos", async () => withDb(async (db) => {
  db.raw.prepare("update accounts set status = 'SUSPENDED' where id = ?").run(IDS.admin);
  assert.deepEqual(await createResidentBaselineService(db, verified("synthetic-admin")).createResident(createInput()), denied);
}));

test("BOUNDARY-T04 selección de perfil inexistente", async () => withDb(async (db) => {
  assert.deepEqual(await createResidentBaselineService(db, verified("synthetic-admin"))
    .createResident(createInput({ profileScopeId: unknownId })), denied);
}));

test("BOUNDARY-T05 perfil revocado no se resuelve", async () => withDb(async (db) => {
  revoke(db, "profile_scopes", scopes.admin);
  assert.deepEqual(await createResidentBaselineService(db, verified("synthetic-admin")).createResident(createInput()), denied);
}));

test("BOUNDARY-T06 perfil de otra cuenta no confiere identidad", async () => withDb(async (db) => {
  assert.deepEqual(await createResidentBaselineService(db, verified("synthetic-nurse-a")).createResident(createInput()), denied);
}));

test("BOUNDARY-T07 centro fuera del grant seleccionado", async () => withDb(async (db) => {
  assert.deepEqual(await createResidentBaselineService(db, verified("synthetic-admin"))
    .createResident(createInput({ centerId: IDS.centerB })), denied);
}));

test("BOUNDARY-T08 unidad existente del mismo centro sin concesión", async () => withDb(async (db) => {
  db.raw.prepare("insert into units (id, center_id, code, display_name, status, created_at) values (?, ?, 'UNASSIGNED', 'Unidad Sintética Sin Ámbito', 'ACTIVE', ?)")
    .run(unknownId, IDS.centerA, now);
  assert.deepEqual(await createResidentBaselineService(db, verified("synthetic-admin"))
    .createResident(createInput({ unitId: unknownId })), denied);
}));

test("BOUNDARY-T09 restricción individual excluye otro residente de la misma unidad", async () => withDb(async (db) => {
  const result = await createResidentBaselineService(db, verified("synthetic-admin")).createResident(createInput());
  assert.ok(result.ok);
  restrictResident(db, scopes.direction, result.value.residentId);
  assert.deepEqual(await createResidentBaselineService(db, verified("synthetic-direction")).readDirectionBaseline(readInput()), denied);
}));

test("BOUNDARY-T10 permiso ausente y permisos de otro perfil no se combinan", async () => withDb(async (db) => {
  db.raw.prepare(`insert into profile_scopes (id, account_id, center_id, profile_code, granted_at, granted_by_account_id)
    values (?, ?, ?, 'DIRECCION_CLINICA', ?, ?)`).run(unknownId, IDS.nurseA, IDS.centerA, now, IDS.admin);
  db.raw.prepare(`insert into profile_unit_scopes (id, profile_scope_id, center_id, unit_id, granted_at, granted_by_account_id)
    values (?, ?, ?, ?, ?, ?)`).run(crypto.randomUUID(), unknownId, IDS.centerA, IDS.unitA, now, IDS.admin);
  assert.deepEqual(await createResidentBaselineService(db, verified("synthetic-nurse-a"))
    .readDirectionBaseline(readInput({ profileScopeId: unknownId })), denied);
}));

test("BOUNDARY-T11 permiso revocado se revalida con la misma sesión abierta", async () => withDb(async (db) => {
  createCompleteDraft(db.raw);
  const service = createResidentBaselineService(db, verified("synthetic-nurse-a"));
  assert.ok((await service.signBaseline(signInput())).ok);
  revoke(db, "profile_permissions", scopes.nurse, "BASELINE_INITIAL_COMPLETE");
  assert.deepEqual(await service.signBaseline(signInput()), denied);
}));

test("BOUNDARY-T12 ámbitos de unidad y último residente revocados no amplían acceso", async () => {
  await withDb(async (db) => {
    revoke(db, "profile_unit_scopes", scopes.admin);
    assert.deepEqual(await createResidentBaselineService(db, verified("synthetic-admin")).createResident(createInput()), denied);
  });
  await withDb(async (db) => {
    restrictResident(db, scopes.direction);
    revoke(db, "profile_resident_scopes", scopes.direction);
    assert.deepEqual(await createResidentBaselineService(db, verified("synthetic-direction")).readDirectionBaseline(readInput()), denied);
  });
});

test("BOUNDARY-T13 cambio malicioso de perfil y mezcla multirol denegados", async () => withDb(async (db) => {
  createCompleteDraft(db.raw);
  db.raw.prepare(`insert into profile_scopes (id, account_id, center_id, profile_code, granted_at, granted_by_account_id)
    values (?, ?, ?, 'MEDICINA', ?, ?)`).run(unknownId, IDS.nurseA, IDS.centerA, now, IDS.admin);
  db.raw.prepare(`insert into profile_unit_scopes (id, profile_scope_id, center_id, unit_id, granted_at, granted_by_account_id)
    values (?, ?, ?, ?, ?, ?)`).run(crypto.randomUUID(), unknownId, IDS.centerA, IDS.unitA, now, IDS.admin);
  const service = createResidentBaselineService(db, verified("synthetic-nurse-a"));
  assert.deepEqual(await service.signBaseline(signInput({ profileScopeId: unknownId })), denied);
  assert.deepEqual(await service.signBaseline(signInput({ activeProfile: "MEDICINA" })), denied);
  assert.equal(counts(db)[3], 0);
}));

test("BOUNDARY-T14 claims, cabeceras, cookies y permisos cliente no confieren autoridad", async () => withDb(async (db) => {
  const service = createResidentBaselineService(db, verified("synthetic-nurse-a"));
  const before = counts(db);
  for (const claim of [
    { permissions: ["RESIDENT_IDENTITY_CREATE"] }, { accountId: IDS.admin },
    { subject: { authenticated: true, accountActive: true } },
    { headers: { "x-account-id": IDS.admin } }, { cookie: "account=synthetic-admin" },
    { externalSubject: "synthetic-admin" }, { activeProfile: "ADMINISTRACION" },
  ]) assert.deepEqual(await service.createResident(createInput(claim)), denied);
  assert.deepEqual(counts(db), before);
}));

test("BOUNDARY-T15 aislamiento bidireccional entre dos centros con un actor multicentro", async () => withDb(async (db) => {
  db.raw.prepare(`insert into profile_scopes (id, account_id, center_id, profile_code, granted_at, granted_by_account_id)
    values (?, ?, ?, 'ADMINISTRACION', ?, ?)`).run(unknownId, IDS.admin, IDS.centerB, now, IDS.admin);
  db.raw.prepare(`insert into profile_unit_scopes (id, profile_scope_id, center_id, unit_id, granted_at, granted_by_account_id)
    values (?, ?, ?, ?, ?, ?)`).run(crypto.randomUUID(), unknownId, IDS.centerB, IDS.unitB, now, IDS.admin);
  const service = createResidentBaselineService(db, verified("synthetic-admin"));
  const selectionB = { profileScopeId: unknownId, centerId: IDS.centerB, unitId: IDS.unitB };
  for (const override of [
    { unitId: IDS.unitB }, { centerId: IDS.centerB },
    { ...selectionB, unitId: IDS.unitA }, { ...selectionB, centerId: IDS.centerA },
  ]) assert.deepEqual(await service.createResident(createInput(override)), denied);
  const created = await service.createResident(createInput(selectionB));
  assert.ok(created.ok);
  assert.deepEqual(await createResidentBaselineService(db, verified("synthetic-direction"))
    .readDirectionBaseline(readInput({ residentId: created.value.residentId })), denied);
}));

test("BOUNDARY-T16 inexistente y no autorizado tienen idéntico resultado serializado", async () => withDb(async (db) => {
  const admin = createResidentBaselineService(db, verified("synthetic-admin"));
  const created = await admin.createResident(createInput());
  assert.ok(created.ok);
  restrictResident(db, scopes.direction, created.value.residentId);
  const direction = createResidentBaselineService(db, verified("synthetic-direction"));
  const absent = await direction.readDirectionBaseline(readInput({ residentId: unknownId }));
  const unauthorized = await direction.readDirectionBaseline(readInput());
  assert.equal(JSON.stringify(absent), JSON.stringify(unauthorized));
  assert.deepEqual(absent, denied);
}));

test("BOUNDARY-T17 alta autorizada atómica, idempotente y con autoría del servidor", async () => withDb(async (db) => {
  const service = createResidentBaselineService(db, verified("synthetic-admin"));
  const before = counts(db);
  const result = await service.createResident(createInput());
  assert.ok(result.ok);
  assert.deepEqual(await service.createResident(createInput()), result);
  assert.deepEqual(counts(db), before.map((n, i) => n + ([0, 1, 2, 5, 6].includes(i) ? 1 : 0)));
  const audit = db.raw.prepare("select account_id, active_profile, center_id, unit_id from audit_events where resident_id = ?").get(result.value.residentId);
  assert.deepEqual({ ...audit }, { account_id: IDS.admin, active_profile: "ADMINISTRACION", center_id: IDS.centerA, unit_id: IDS.unitA });
}));

test("BOUNDARY-T18 firma autorizada, idempotente y reevalúa con permiso independiente", async () => withDb(async (db) => {
  createCompleteDraft(db.raw);
  const service = createResidentBaselineService(db, verified("synthetic-nurse-a"));
  const result = await service.signBaseline(signInput());
  assert.ok(result.ok);
  assert.equal(result.value.versionNumber, 1);
  assert.deepEqual(await service.signBaseline(signInput()), result);
  createCompleteDraft(db.raw, { id: IDS.draftB, reasonCode: "REVISION_PROGRAMADA" });
  const second = await service.signBaseline(signInput({ draftId: IDS.draftB, operationId: crypto.randomUUID() }));
  assert.ok(second.ok);
  assert.equal(second.value.versionNumber, 2);
}));

test("BOUNDARY-T19 nadie firma el borrador de otra cuenta", async () => withDb(async (db) => {
  createCompleteDraft(db.raw);
  const before = counts(db);
  assert.deepEqual(await createResidentBaselineService(db, verified("synthetic-nurse-b"))
    .signBaseline(signInput({ profileScopeId: scopes.otherNurse })), denied);
  assert.deepEqual(counts(db), before);
}));

test("BOUNDARY-T20 Dirección recibe cabeceras solo tras auditoría de cada versión", async () => withDb(async (db) => {
  createCompleteDraft(db.raw);
  assert.ok((await createResidentBaselineService(db, verified("synthetic-nurse-a")).signBaseline(signInput())).ok);
  const direction = createResidentBaselineService(db, verified("synthetic-direction"));
  for (const resourceType of ["BASELINE_CURRENT", "BASELINE_HISTORY"]) {
    const result = await direction.readDirectionBaseline(readInput({ resourceType }));
    assert.ok(result.ok);
    assert.equal(result.value.length, 1);
    const audit = db.raw.prepare("select resource_id, account_id, purpose_code from audit_events where resource_type = ?").get(resourceType);
    assert.deepEqual({ ...audit }, { resource_id: result.value[0].id, account_id: IDS.direction, purpose_code: "SUPERVISION_CLINICA" });
  }
}));

test("BOUNDARY-T21 fallo de auditoría impide entregar contenido y no se presenta como vacío", async () => withDb(async (db) => {
  createCompleteDraft(db.raw);
  assert.ok((await createResidentBaselineService(db, verified("synthetic-nurse-a")).signBaseline(signInput())).ok);
  db.raw.exec("create trigger boundary_audit_failure before insert on audit_events begin select raise(abort, 'SYNTHETIC_AUDIT_FAILURE'); end");
  const before = counts(db);
  const result = await createResidentBaselineService(db, verified("synthetic-direction")).readDirectionBaseline(readInput());
  assert.deepEqual(result, { ok: false, error: { code: "UNAVAILABLE", message: "No se ha podido completar la operación." } });
  assert.equal("value" in result, false);
  assert.deepEqual(counts(db), before);
}));

test("BOUNDARY-T22 proveedor sintético desactivado por defecto y fuera de test/desarrollo local", async () => {
  const keys = ["NODE_ENV", "CONNECT_SYNTHETIC_SESSION_ENABLED", "CONNECT_LOCAL_DEVELOPMENT"];
  const saved = keys.map((key) => process.env[key]);
  const provider = createSyntheticSessionProvider("synthetic-admin");
  try {
    for (const key of keys) delete process.env[key];
    await assert.rejects(provider.getVerifiedIdentity(), /SYNTHETIC_SESSION_DISABLED/u);
    process.env.CONNECT_SYNTHETIC_SESSION_ENABLED = "1";
    for (const environment of ["production", "staging", "development", ""]) {
      Object.assign(process.env, { NODE_ENV: environment });
      await assert.rejects(provider.getVerifiedIdentity(), /SYNTHETIC_SESSION_DISABLED/u);
    }
    Object.assign(process.env, { NODE_ENV: "test" });
    assert.deepEqual(await provider.getVerifiedIdentity(), { externalSubject: "synthetic-admin" });
    Object.assign(process.env, { NODE_ENV: "development" });
    process.env.CONNECT_LOCAL_DEVELOPMENT = "1";
    assert.deepEqual(await provider.getVerifiedIdentity(), { externalSubject: "synthetic-admin" });
    Object.assign(process.env, { NODE_ENV: "production" });
    await assert.rejects(provider.getVerifiedIdentity(), /SYNTHETIC_SESSION_DISABLED/u);
  } finally {
    keys.forEach((key, i) => { if (saved[i] === undefined) delete process.env[key]; else process.env[key] = saved[i]; });
  }
});

/** Intercala una revocación justo antes del batch que contiene la operación crítica. */
function beforeCriticalBatch(database: D1DatabaseLike, match: string, change: () => Promise<void> | void): D1DatabaseLike {
  const queries = new WeakMap<D1PreparedStatementLike, string>();
  const rawStatements = new WeakMap<D1PreparedStatementLike, D1PreparedStatementLike>();
  let pending = true;
  function track(raw: D1PreparedStatementLike, sql: string): D1PreparedStatementLike {
    const statement: D1PreparedStatementLike = {
      bind(...values) { return track(raw.bind(...values), sql); },
      first: () => raw.first(), all: () => raw.all(), run: () => raw.run(),
    };
    queries.set(statement, sql);
    rawStatements.set(statement, raw);
    return statement;
  }
  return {
    prepare(sql) { return track(database.prepare(sql), sql); },
    async batch<Row>(statements: readonly D1PreparedStatementLike[]): Promise<readonly D1ResultLike<Row>[]> {
      if (pending && statements.some((statement) => queries.get(statement)?.includes(match))) {
        pending = false; await change();
      }
      return database.batch<Row>(statements.map((statement) => rawStatements.get(statement)!));
    },
  };
}

for (const operation of ["create", "sign", "read"] as const) {
  for (const revoked of ["account", "profile", "unit", "permission", "resident"] as const) {
    if (operation === "create" && revoked === "resident") continue;
    test(`BOUNDARY-T23 ${operation}: revocar ${revoked} dentro de la carrera TOCTOU`, async () => withDb(async (db) => {
      createCompleteDraft(db.raw);
      if (operation === "read") assert.ok((await createResidentBaselineService(db, verified("synthetic-nurse-a")).signBaseline(signInput())).ok);
      const isRead = operation === "read";
      const scope = isRead ? scopes.direction : scopes.nurse;
      const account = isRead ? IDS.direction : IDS.nurseA;
      if (revoked === "resident") restrictResident(db, scope);
      const before = counts(db);
      const guarded = beforeCriticalBatch(db,
        operation === "create" ? "insert into residents" : operation === "sign" ? "insert into baseline_versions" : "insert into audit_events",
        () => {
          if (revoked === "account") db.raw.prepare("update accounts set status = 'SUSPENDED' where id = ?").run(account);
          else revoke(db, revoked === "profile" ? "profile_scopes" : revoked === "unit" ? "profile_unit_scopes" :
            revoked === "resident" ? "profile_resident_scopes" : "profile_permissions", scope,
            revoked !== "permission" ? undefined : isRead ? "CLINICAL_DETAIL_READ" : operation === "sign" ? "BASELINE_INITIAL_COMPLETE" : "RESIDENT_IDENTITY_CREATE");
        });
      const service = createResidentBaselineService(guarded, verified(isRead ? "synthetic-direction" : "synthetic-nurse-a"));
      const result = operation === "create" ? await service.createResident(createInput({ profileScopeId: scopes.nurse })) :
        operation === "sign" ? await service.signBaseline(signInput()) : await service.readDirectionBaseline(readInput());
      assert.deepEqual(result, denied);
      assert.deepEqual(counts(db), before);
    }));
  }
}

test("BOUNDARY-T25 token opaco inmutable; una copia no conserva autoridad", async () => withDb(async (db) => {
  const context = await resolveRequestContext({ database: db, session: verified("synthetic-admin"),
    selection: { profileScopeId: scopes.admin, centerId: IDS.centerA }, target: { kind: "CREATE", unitId: IDS.unitA } });
  assert.equal(Object.isFrozen(context), true);
  assert.equal(Object.getPrototypeOf(context), null);
  assert.deepEqual(Reflect.ownKeys(context), []);
  assert.throws(() => Object.assign(context, { centerId: IDS.centerB }), TypeError);
  await assert.rejects(executeResidentCreate({ ...context }, { ...createInput(), documentedSexCode: "unknown" }), /ACCESS_DENIED/u);
}));

test("BOUNDARY-T26 la selección queda copiada antes de esperar a la sesión", async () => withDb(async (db) => {
  const input: Record<string, unknown> = createInput();
  const provider: SessionIdentityProvider = { async getVerifiedIdentity() {
    input.centerId = IDS.centerB; input.unitId = IDS.unitB; input.displayName = "Mutación sintética tardía";
    return { externalSubject: "synthetic-admin" };
  } };
  const result = await createResidentBaselineService(db, provider).createResident(input);
  assert.ok(result.ok);
  const row = db.raw.prepare("select center_id, display_name from residents where id = ?").get(result.value.residentId);
  assert.deepEqual({ ...row }, { center_id: IDS.centerA, display_name: "Residente Sintético Nuevo" });
}));

test("BOUNDARY-T27 no devuelve un alta idempotente tras revocar el ámbito", async () => withDb(async (db) => {
  const service = createResidentBaselineService(db, verified("synthetic-admin"));
  assert.ok((await service.createResident(createInput())).ok);
  revoke(db, "profile_unit_scopes", scopes.admin);
  assert.deepEqual(await service.createResident(createInput()), denied);
}));

test("BOUNDARY-T28 D1 local real ejecuta los tres casos y revierte un guard revocado", async () => {
  const miniflare = new Miniflare(convertV4MiniflareOptions({
    modules: true, script: "export default { fetch() { return new Response('synthetic-local') } }",
    compatibilityDate: "2026-09-06", d1Databases: { DB: "synthetic-server-auth-boundary" },
  }));
  try {
    const db = await miniflare.getD1Database("DB") as unknown as D1DatabaseLike;
    for (const statement of readFileSync("db/migrations/0001_resident_baseline_foundation.sql", "utf8").split("--> statement-breakpoint")) {
      if (statement.trim()) await db.prepare(statement).run();
    }
    await seedFoundationD1(db);
    await createCompleteDraftD1(db);
    assert.ok((await createResidentBaselineService(db, verified("synthetic-admin")).createResident(createInput())).ok);
    assert.ok((await createResidentBaselineService(db, verified("synthetic-nurse-a")).signBaseline(signInput())).ok);
    assert.ok((await createResidentBaselineService(db, verified("synthetic-direction")).readDirectionBaseline(readInput())).ok);
    const guarded = beforeCriticalBatch(db, "insert into audit_events", async () => {
      await db.prepare("update profile_permissions set revoked_at = ?, revoked_by_account_id = ? where profile_scope_id = ?")
        .bind(now, IDS.admin, scopes.direction).run();
    });
    assert.deepEqual(await createResidentBaselineService(guarded, verified("synthetic-direction")).readDirectionBaseline(readInput()), denied);
    const row = await db.prepare("select count(*) n from audit_events where action_code = 'CLINICAL_DETAIL_READ'").first<{ n: number }>();
    assert.equal(row?.n, 1);
  } finally { await miniflare.dispose(); }
});

test("BOUNDARY-T29 revalida incluso entre resolución e intento de recuperar resultado idempotente", async () => withDb(async (db) => {
  assert.ok((await createResidentBaselineService(db, verified("synthetic-admin")).createResident(createInput())).ok);
  const before = counts(db);
  const guarded = beforeCriticalBatch(db, "select request_hash", () => revoke(db, "profile_scopes", scopes.admin));
  assert.deepEqual(await createResidentBaselineService(guarded, verified("synthetic-admin")).createResident(createInput()), denied);
  assert.deepEqual(counts(db), before);
}));

test("BOUNDARY-T30 dos firmas simultáneas con la misma clave recuperan un único resultado", async () => withDb(async (db) => {
  createCompleteDraft(db.raw);
  const service = createResidentBaselineService(db, verified("synthetic-nurse-a"));
  const results = await Promise.all([service.signBaseline(signInput()), service.signBaseline(signInput())]);
  assert.ok(results[0].ok);
  assert.deepEqual(results[1], results[0]);
  assert.equal(counts(db)[3], 1);
  assert.equal(counts(db)[6], 1);
}));

for (const table of ["centers", "units"]) {
  test(`BOUNDARY-T31 inactivar ${table} antes del batch revierte el alta`, async () => withDb(async (db) => {
    const before = counts(db);
    const guarded = beforeCriticalBatch(db, "insert into residents", () => {
      db.raw.prepare(`update ${table} set status = 'INACTIVE' where id = ?`)
        .run(table === "centers" ? IDS.centerA : IDS.unitA);
    });
    assert.deepEqual(await createResidentBaselineService(guarded, verified("synthetic-admin")).createResident(createInput()), denied);
    assert.deepEqual(counts(db), before);
  }));
}

test("BOUNDARY-T32 sesión expirada y error del proveedor se resuelven sin filtrar detalles", async () => withDb(async (db) => {
  let expired = false;
  const service = createResidentBaselineService(db, { async getVerifiedIdentity() {
    return expired ? null : { externalSubject: "synthetic-admin" };
  } });
  assert.ok((await service.createResident(createInput())).ok);
  expired = true;
  assert.deepEqual(await service.createResident(createInput()), denied);
  const failure = await createResidentBaselineService(db, { async getVerifiedIdentity() {
    throw new Error("SYNTHETIC_INTERNAL_PROVIDER_DETAIL");
  } }).createResident(createInput());
  assert.deepEqual(failure, { ok: false, error: { code: "UNAVAILABLE", message: "No se ha podido completar la operación." } });
}));

for (const sql of [
  "INSERT INTO centers VALUES ('synthetic-injected', 'INJECTED', 'Centro Sintético Inyectado', 'ACTIVE', '2026-09-07')",
  "UPDATE centers SET display_name = 'Mutación Sintética Inyectada'",
  "DELETE FROM centers WHERE code = 'CENTER-B'",
]) {
  test(`CAP-T01 READ no proporciona prepare/batch para ${sql.split(" ")[0]} arbitrario`, async () => withDb(async (db) => {
    const context = await capability(db, "READ");
    const before = db.raw.prepare("select * from centers order by id").all();
    assert.deepEqual(Object.keys(capabilityApi).sort(), [
      "executeBaselineSign", "executeDirectionBaselineRead", "executeResidentCreate", "resolveRequestContext",
    ]);
    assert.deepEqual(Reflect.ownKeys(context), []);
    await assert.rejects(async () => (context as unknown as D1DatabaseLike).prepare(sql).run(), TypeError);
    await assert.rejects(async () => (context as unknown as D1DatabaseLike).batch([db.prepare(sql)]), TypeError);
    assert.deepEqual(db.raw.prepare("select * from centers order by id").all(), before);
  }));
}

for (const granted of ["READ", "SIGN", "CREATE"] as const) {
  for (const attempted of ["READ", "SIGN", "CREATE"] as const) {
    if (granted === attempted) continue;
    test(`CAP-T02 capacidad ${granted} no se puede reutilizar para ${attempted}`, async () => withDb(async (db) => {
      createCompleteDraft(db.raw);
      const context = await capability(db, granted);
      const before = counts(db);
      // Incluso saltándose deliberadamente la marca TypeScript, falla antes de entrar al repositorio.
      const operation = attempted === "CREATE" ?
        executeResidentCreate(context as RequestAuthorizationContext<"CREATE">, { ...createInput(), documentedSexCode: "unknown" }) :
        attempted === "SIGN" ? executeBaselineSign(context as RequestAuthorizationContext<"SIGN">, signInput()) :
          executeDirectionBaselineRead(context as RequestAuthorizationContext<"READ">);
      await assert.rejects(operation, /ACCESS_DENIED/u);
      assert.deepEqual(counts(db), before);
    }));
  }
}

test("CAP-T03 SIGN fija borrador, autor, perfil, centro y unidad aunque el payload intente sustituirlos", async () => withDb(async (db) => {
  createCompleteDraft(db.raw, { id: IDS.draftB });
  assert.ok((await createResidentBaselineService(db, verified("synthetic-nurse-a")).signBaseline(signInput({ draftId: IDS.draftB }))).ok);
  createCompleteDraft(db.raw, { reasonCode: "REVISION_PROGRAMADA" });
  const context = await capability(db, "SIGN");
  const result = await executeBaselineSign(context, { ...signInput({ draftId: IDS.draftB, operationId: "70000000-0000-4000-8000-000000000002" }),
    accountId: IDS.admin, activeProfile: "ADMINISTRACION", centerId: IDS.centerB, unitId: IDS.unitB,
  } as Parameters<typeof executeBaselineSign>[1]);
  assert.equal(db.raw.prepare("select status from baseline_drafts where id = ?").get(IDS.draftA)?.status, "SIGNED");
  assert.equal(db.raw.prepare("select status from baseline_drafts where id = ?").get(IDS.draftB)?.status, "SIGNED");
  assert.equal(result.versionNumber, 2);
  const row = db.raw.prepare("select resident_id, center_id, signed_by_account_id, signed_by_profile from baseline_versions where id = ?").get(result.baselineVersionId);
  assert.deepEqual({ ...row }, { resident_id: IDS.residentA, center_id: IDS.centerA,
    signed_by_account_id: IDS.nurseA, signed_by_profile: "ENFERMERIA" });
}));

test("CAP-T04 READ conserva recurso/finalidad; CREATE conserva ámbito/autor frente a campos inyectados", async () => withDb(async (db) => {
  createCompleteDraft(db.raw);
  await executeBaselineSign(await capability(db, "SIGN"), signInput());
  const context = await capability(db, "READ");
  const headers = await Reflect.apply(executeDirectionBaselineRead, undefined, [context,
    { residentId: IDS.residentB, resourceType: "BASELINE_HISTORY", purpose: "INJECTED", sql: "DELETE FROM residents" }]);
  assert.equal(headers.length, 1);
  const audit = db.raw.prepare("select account_id, active_profile, center_id, unit_id, resident_id, resource_type, purpose_code from audit_events where action_code = 'CLINICAL_DETAIL_READ'").get();
  assert.deepEqual({ ...audit }, { account_id: IDS.direction, active_profile: "DIRECCION_CLINICA", center_id: IDS.centerA,
    unit_id: IDS.unitA, resident_id: IDS.residentA, resource_type: "BASELINE_CURRENT", purpose_code: "SUPERVISION_CLINICA" });
  const created = await executeResidentCreate(await capability(db, "CREATE"), {
    ...createInput({ accountId: IDS.direction, activeProfile: "DIRECCION_CLINICA", centerId: IDS.centerB, unitId: IDS.unitB }),
    documentedSexCode: "unknown",
  });
  const row = db.raw.prepare("select center_id, created_by_account_id, created_by_profile from residents where id = ?").get(created.residentId);
  assert.deepEqual({ ...row }, { center_id: IDS.centerA, created_by_account_id: IDS.nurseA, created_by_profile: "ENFERMERIA" });
}));

test("CAP-T05 las tres capacidades legítimas producen solo resultados y conservan separación de tipos", async () => withDb(async (db) => {
  createCompleteDraft(db.raw);
  const create = await resolveRequestContext({ database: db, session: verified("synthetic-admin"),
    selection: { profileScopeId: scopes.admin, centerId: IDS.centerA }, target: { kind: "CREATE", unitId: IDS.unitA } });
  const sign = await resolveRequestContext({ database: db, session: verified("synthetic-nurse-a"),
    selection: { profileScopeId: scopes.nurse, centerId: IDS.centerA }, target: { kind: "SIGN", residentId: IDS.residentA, draftId: IDS.draftA } });
  const read = await resolveRequestContext({ database: db, session: verified("synthetic-direction"),
    selection: { profileScopeId: scopes.direction, centerId: IDS.centerA }, target: { kind: "READ", residentId: IDS.residentA },
    resourceType: "BASELINE_CURRENT", purpose: "SUPERVISION_CLINICA" });
  // @ts-expect-error Una capacidad READ no es una capacidad SIGN (verificado por pnpm typecheck).
  await assert.rejects(executeBaselineSign(read, signInput()), /ACCESS_DENIED/u);
  // @ts-expect-error Una capacidad SIGN no es una capacidad READ.
  await assert.rejects(executeDirectionBaselineRead(sign), /ACCESS_DENIED/u);
  // @ts-expect-error Una capacidad SIGN no es una capacidad CREATE.
  await assert.rejects(executeResidentCreate(sign, { ...createInput(), documentedSexCode: "unknown" }), /ACCESS_DENIED/u);
  const created = await executeResidentCreate(create, { ...createInput(), documentedSexCode: "unknown" });
  const signed = await executeBaselineSign(sign, signInput());
  const headers = await executeDirectionBaselineRead(read);
  assert.deepEqual(Object.keys(created).sort(), ["episodeId", "locationIntervalId", "residentId"]);
  assert.deepEqual(Object.keys(signed).sort(), ["baselineVersionId", "versionNumber"]);
  assert.equal(headers.length, 1);
  assert.equal(headers[0].id, signed.baselineVersionId);
  assert.deepEqual(Object.keys(headers[0]).sort(), ["id", "reason_code", "signed_at", "version_number"]);
}));

for (const kind of ["CREATE", "SIGN", "READ"] as const) {
  test(`CAP-T06 ${kind}: revocación entre capacidad y batch revierte la operación completa`, async () => withDb(async (db) => {
    createCompleteDraft(db.raw);
    if (kind === "READ") await executeBaselineSign(await capability(db, "SIGN"), signInput());
    const before = counts(db);
    const guarded = beforeCriticalBatch(db, kind === "CREATE" ? "insert into residents" :
      kind === "SIGN" ? "insert into baseline_versions" : "insert into audit_events", () => {
      revoke(db, "profile_unit_scopes", kind === "READ" ? scopes.direction : scopes.nurse);
    });
    const context = await capability(guarded, kind);
    const operation = kind === "CREATE" ? executeResidentCreate(context as RequestAuthorizationContext<"CREATE">, { ...createInput(), documentedSexCode: "unknown" }) :
      kind === "SIGN" ? executeBaselineSign(context as RequestAuthorizationContext<"SIGN">, signInput()) :
        executeDirectionBaselineRead(context as RequestAuthorizationContext<"READ">);
    await assert.rejects(operation, /ACCESS_DENIED/u);
    assert.deepEqual(counts(db), before);
    if (kind !== "READ") assert.equal(db.raw.prepare("select status from baseline_drafts where id = ?").get(IDS.draftA)?.status, "ACTIVE");
  }));
}
