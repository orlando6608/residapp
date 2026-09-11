import assert from "node:assert/strict";
import test from "node:test";

import { denyByDefault } from "../lib/authorization/policy.ts";

test("[negativo] la política inicial deniega siempre por defecto", () => {
  const decision = denyByDefault();

  assert.deepEqual(decision, {
    allowed: false,
    reason: "DENY_BY_DEFAULT",
  });
});

test("[invariante] la decisión por defecto no puede mutarse", () => {
  assert.equal(Object.isFrozen(denyByDefault()), true);
});
