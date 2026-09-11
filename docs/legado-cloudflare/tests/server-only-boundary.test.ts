import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join, relative, resolve } from "node:path";
import { pathToFileURL } from "node:url";
import test from "node:test";

const serverModules = [
  "db/client.ts", "db/repositories/resident-repository.ts", "db/repositories/baseline-repository.ts",
  "db/repositories/audit-repository.ts", "db/repositories/authorization-subject-repository.ts",
  "db/repositories/authorized-d1.ts", "lib/authorization/policy.ts", "lib/authorization/server.ts",
  "lib/authorization/request-context.ts", "lib/session/session-provider.ts",
  "lib/session/synthetic-session-provider.ts", "lib/application/errors.ts",
  "lib/application/resident-baseline-service.ts",
];

async function command(args: string[], cwd = process.cwd()) {
  return new Promise<{ code: number | null; output: string }>((resolveCommand, reject) => {
    // No hereda react-server ni habilita el proveedor sintético en el proceso cliente.
    const child = spawn(process.execPath, args, { cwd, windowsHide: true,
      env: { ...process.env, NODE_OPTIONS: "", NODE_ENV: "production", NEXT_TELEMETRY_DISABLED: "1", CONNECT_SYNTHETIC_SESSION_ENABLED: "0" },
    });
    let output = "";
    child.stdout.on("data", (data) => { output += data; });
    child.stderr.on("data", (data) => { output += data; });
    child.on("error", reject);
    child.on("close", (code) => resolveCommand({ code, output }));
  });
}

test("BOUNDARY-T24a todos los módulos sensibles rechazan condiciones cliente", async () => {
  for (const file of serverModules) assert.match(readFileSync(file, "utf8"), /^import "server-only";/u, file);
  for (const file of serverModules) {
    // Proceso nuevo: no reutilizar la caché de un paquete CommonJS cuya evaluación falló.
    const url = pathToFileURL(resolve(file)).href;
    const result = await command(["--experimental-strip-types", "--input-type=module", "-e", `
      import assert from 'node:assert/strict';
      await assert.rejects(import(${JSON.stringify(url)}), /cannot be imported from a Client Component module/);
    `]);
    assert.equal(result.code, 0, `${file}: ${result.output}`);
  }
});

for (const bundler of ["next", "vinext"] as const) {
  test(`BOUNDARY-T24b ${bundler}: build real rechaza importar el servicio desde use client`, async () => {
    const parent = resolve(".tmp");
    mkdirSync(parent, { recursive: true });
    const root = mkdtempSync(join(parent, "server-only-build-"));
    try {
      mkdirSync(join(root, "app"));
      const servicePath = relative(join(root, "app"), resolve("lib/application/resident-baseline-service.ts")).replaceAll("\\", "/");
      writeFileSync(join(root, "package.json"), '{"private":true,"type":"module"}');
      writeFileSync(join(root, "app/layout.jsx"), 'export default function Layout({children}) { return <html><body>{children}</body></html> }');
      writeFileSync(join(root, "app/page.jsx"), `"use client";\nimport { createResidentBaselineService } from ${JSON.stringify(servicePath)};\nexport default function Page() { return <div>{String(createResidentBaselineService)}</div> }`);
      writeFileSync(join(root, "next.config.mjs"), `export default { turbopack: { root: ${JSON.stringify(process.cwd())} } };`);
      writeFileSync(join(root, "vite.config.mjs"), 'import { defineConfig } from "vite"; import vinext from "vinext"; export default defineConfig({ plugins: [vinext()] });');
      const result = await command([resolve(bundler === "next" ? "node_modules/next/dist/bin/next" : "node_modules/vite/bin/vite.js"), "build"], root);
      assert.notEqual(result.code, 0, result.output);
      assert.match(result.output, /server-only/iu);
      assert.match(result.output, /Client Component|client.*(?:import|server)|(?:import|server).*client/iu);
    } finally {
      // Ruta absoluta verificada antes de borrar únicamente la fixture desechable creada aquí.
      assert.ok(root.startsWith(parent + (process.platform === "win32" ? "\\" : "/")));
      rmSync(root, { recursive: true, force: true });
    }
  });
}
