import assert from "node:assert/strict";
import test from "node:test";

import {
  createOpaqueEntityId,
  isOpaqueEntityId,
} from "../lib/domain/shared/identifiers.ts";

test("[positivo] genera identificadores UUID v4 opacos y no repetidos", () => {
  const first = createOpaqueEntityId<"Fixture">();
  const second = createOpaqueEntityId<"Fixture">();

  assert.equal(isOpaqueEntityId(first), true);
  assert.equal(isOpaqueEntityId(second), true);
  assert.notEqual(first, second);
});

for (const value of [
  "resident-1",
  "00000000-0000-0000-0000-000000000001",
  "10000000-0000-1000-8000-000000000001",
  "10000000-0000-4000-7000-000000000001",
  "",
  1,
  null,
]) {
  test(`[negativo] rechaza como identificador opaco: ${String(value)}`, () => {
    assert.equal(isOpaqueEntityId(value), false);
  });
}
