import assert from "node:assert/strict";
import { resolve } from "node:path";
import test from "node:test";
import { ESLint } from "eslint";

// Usa la configuración real: una regla declarada pero desconectada del lint no pasa.
const eslint = new ESLint();
async function violations(file: string, code: string) {
  const [result] = await eslint.lintText(code, { filePath: resolve(file) });
  assert.equal(result.fatalErrorCount, 0, JSON.stringify(result.messages));
  return result.messages.filter((message) => message.ruleId === "architecture/d1-boundary");
}

const forbidden = [
  "db/client", "db/repositories/resident-repository", "db/repositories/baseline-repository",
  "db/repositories/audit-repository", "db/repositories/authorization-subject-repository",
  "db/repositories/authorized-d1", "lib/authorization/request-context", "lib/session/synthetic-session-provider",
  "db/repositories/future-repository", "db/repositories/d1", "db/repositories/d1.js",
];

for (const file of ["app/api/simulated/route.ts", "components/simulated.tsx", "worker/simulated.ts",
  "transport/future/handler.ts", "lib/future-transport/handler.ts", "lib/application/unapproved-service.ts"]) {
  test(`CAP-ARCH-01 ${file}: deniega todos los módulos internos sin excepción de directorio`, async () => {
    for (const target of forbidden) {
      assert.equal((await violations(file, `import * as internal from "@/${target}"; void internal;`)).length, 1, target);
    }
  });
}

test("CAP-ARCH-02 rutas relativas, alias, absolutas, reexports, require y cargas dinámicas", async () => {
  const source = JSON.stringify(resolve("db/repositories/resident-repository.ts"));
  for (const code of [
    'import "../db/repositories/resident-repository.ts";',
    'import "@/DB/Repositories/Resident-Repository.ts";',
    'export * from "@/db/repositories/resident-repository";',
    'export { createResidentWithInitialLocation } from "@/db/repositories/resident-repository";',
    'import("@/db/repositories/resident-repository.ts");',
    'require("@/db/repositories/resident-repository");',
    'module.require("@/db/repositories/resident-repository");',
    'require.resolve("@/db/repositories/resident-repository");',
    'import { type CreateResidentInput } from "@/db/repositories/resident-repository";',
    'type Bypass = typeof import("@/db/repositories/resident-repository");',
    'import repository = require("@/db/repositories/resident-repository");',
    `import(${source});`,
    'import("@/db/../db/repositories/resident-repository.ts?raw");',
    'import(pathFromRequest);',
    'require(pathFromRequest);',
    'import.meta.glob("../db/**/*.ts");',
  ]) assert.equal((await violations("transport/simulated.ts", code)).length, 1, code);
});

test("CAP-ARCH-03 excepciones internas exactas; sin reexportar ni usar tests como puente", async () => {
  for (const [file, targets] of [
    ["lib/application/resident-baseline-service.ts", ["lib/authorization/request-context"]],
    ["lib/authorization/request-context.ts", ["db/repositories/authorized-d1", "db/repositories/authorization-subject-repository",
      "db/repositories/resident-repository", "db/repositories/baseline-repository", "db/repositories/audit-repository"]],
    ["db/repositories/authorized-d1.ts", ["db/repositories/authorization-subject-repository"]],
    ["tests/db-persistence.test.ts", ["db/repositories/resident-repository", "db/repositories/baseline-repository", "db/repositories/audit-repository"]],
    ["tests/server-auth-d1-boundary.test.ts", ["lib/authorization/request-context", "lib/session/synthetic-session-provider"]],
  ] as const) {
    for (const target of targets) assert.deepEqual(await violations(file, `import "@/${target}";`), [], `${file}: ${target}`);
  }
  for (const [file, code] of [
    ["lib/application/resident-baseline-service.ts", 'import "@/db/repositories/resident-repository";'],
    ["lib/application/resident-baseline-service.ts", 'export * from "@/lib/authorization/request-context";'],
    ["lib/authorization/request-context.ts", 'export * from "@/db/repositories/authorized-d1";'],
    ["tests/unapproved.test.ts", 'import "@/db/repositories/baseline-repository";'],
    ["lib/application/resident-baseline-service.js", 'import "@/lib/authorization/request-context";'],
    ["tests/db-persistence.test.js", 'import "@/db/repositories/resident-repository";'],
    ["app/api/simulated/route.ts", 'import "@/tests/fixtures/db-fixtures";'],
    ["transport/barrel.ts", 'export * from "@/lib/authorization/request-context";'],
    ["transport/simulated.ts", '/* eslint-disable architecture/d1-boundary */\nimport "@/db/client";'],
  ]) assert.equal((await violations(file, code)).length, 1, `${file}: ${code}`);
});

test("CAP-ARCH-04 el transporte conserva acceso al servicio y a tipos D1 sin ejecución", async () => {
  assert.deepEqual(await violations("transport/simulated.ts", `
    import { createResidentBaselineService } from "@/lib/application/resident-baseline-service";
    import type { D1DatabaseLike } from "@/db/repositories/d1";
  `), []);
});

test("CAP-ARCH-05 el contrato D1 público no puede convertirse en un repositorio ejecutable", async () => {
  assert.deepEqual(await violations("db/repositories/d1.ts", "export interface D1 { readonly name: string }"), []);
  for (const code of ["export function database() {}", "export const database = {};",
    'export * from "./authorized-d1";']) {
    assert.ok((await violations("db/repositories/d1.ts", code)).some((violation) => violation.messageId === "typesOnly"));
  }
});
