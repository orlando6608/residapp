import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

test("[invariante] Node y pnpm quedan fijados mediante configuración estándar", async () => {
  const packageJson = JSON.parse(await readFile("package.json", "utf8")) as {
    packageManager?: string;
    engines?: { node?: string; pnpm?: string };
  };
  const nodeVersion = (await readFile(".node-version", "utf8")).trim();

  assert.equal(nodeVersion, "24.20.0");
  assert.equal(packageJson.packageManager, "pnpm@11.19.0");
  assert.equal(packageJson.engines?.node, ">=24.20.0 <25");
  assert.equal(packageJson.engines?.pnpm, "11.19.0");
});

test("[invariante] la puerta ejecuta los dos builds de forma secuencial", async () => {
  const packageJson = JSON.parse(await readFile("package.json", "utf8")) as {
    scripts?: { check?: string };
  };

  assert.equal(
    packageJson.scripts?.check,
    "pnpm typecheck && pnpm lint && pnpm test && pnpm build:next && pnpm build",
  );
});
