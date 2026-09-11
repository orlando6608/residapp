import assert from "node:assert/strict";
import test from "node:test";

import {
  activateBaselineVersion,
  type BaselineDraft,
  type CurrentBaselineVersion,
} from "../lib/domain/baseline/baseline.ts";
import type {
  AccountId,
  BaselineVersionId,
  CenterId,
  ResidentId,
  UnitId,
} from "../lib/domain/shared/identifiers.ts";

const accountId = "10000000-0000-4000-8000-000000000001" as AccountId;
const secondAccountId = "10000000-0000-4000-8000-000000000002" as AccountId;
const centerId = "20000000-0000-4000-8000-000000000001" as CenterId;
const otherCenterId = "20000000-0000-4000-8000-000000000002" as CenterId;
const unitId = "30000000-0000-4000-8000-000000000001" as UnitId;
const otherUnitId = "30000000-0000-4000-8000-000000000002" as UnitId;
const residentId = "40000000-0000-4000-8000-000000000001" as ResidentId;
const otherResidentId = "40000000-0000-4000-8000-000000000002" as ResidentId;
const firstVersionId = "50000000-0000-4000-8000-000000000001" as BaselineVersionId;
const secondVersionId = "50000000-0000-4000-8000-000000000002" as BaselineVersionId;

function draft(
  id: BaselineVersionId,
  versionNumber: number,
  overrides: Partial<BaselineDraft> = {},
): BaselineDraft {
  return {
    id,
    residentId,
    versionNumber,
    reason: versionNumber === 1 ? "ALTA" : "REVISION_PROGRAMADA",
    createdAt:
      versionNumber === 1 ? "2026-09-01T08:00:00.000Z" : "2026-09-03T08:00:00.000Z",
    status: "BORRADOR",
    createdBy: {
      accountId,
      activeProfile: "ENFERMERIA",
      centerId,
      unitId,
    },
    ...overrides,
  };
}

const signature = Object.freeze({
  accountId,
  activeProfile: "ENFERMERIA" as const,
  centerId,
  unitId,
  signedAt: "2026-09-01T08:15:00.000Z",
});

function firstCurrent(): CurrentBaselineVersion {
  return activateBaselineVersion(draft(firstVersionId, 1), signature).current;
}

test("[positivo] firma la primera versión basal sin inventar un histórico", () => {
  const activation = activateBaselineVersion(draft(firstVersionId, 1), signature);

  assert.equal(activation.current.status, "FIRMADO_VIGENTE");
  assert.equal(activation.current.createdBy.accountId, accountId);
  assert.equal(activation.current.signature.accountId, accountId);
  assert.equal(activation.previous, undefined);
});

test("[positivo] una versión posterior archiva la vigente y conserva su firma", () => {
  const secondSignature = {
    ...signature,
    accountId: secondAccountId,
    activeProfile: "MEDICINA" as const,
    signedAt: "2026-09-03T09:30:00.000Z",
  };

  const activation = activateBaselineVersion(
    draft(secondVersionId, 2, {
      createdBy: {
        accountId: secondAccountId,
        activeProfile: "MEDICINA",
        centerId,
        unitId,
      },
    }),
    secondSignature,
    firstCurrent(),
  );

  assert.equal(activation.current.status, "FIRMADO_VIGENTE");
  assert.equal(activation.previous?.status, "HISTORICO");
  assert.equal(activation.previous?.createdBy.accountId, accountId);
  assert.equal(activation.previous?.signature.accountId, accountId);
  assert.equal(activation.previous?.supersededByVersionId, activation.current.id);
  assert.equal(activation.previous?.supersededAt, secondSignature.signedAt);
});

test("[invariante] la activación copia y congela autoría, firma y contenedores", () => {
  const mutableAuthor = {
    accountId,
    activeProfile: "ENFERMERIA" as const,
    centerId,
    unitId,
  };
  const mutableSignature = { ...signature };
  const activation = activateBaselineVersion(
    draft(firstVersionId, 1, { createdBy: mutableAuthor }),
    mutableSignature,
  );

  mutableAuthor.accountId = secondAccountId;
  mutableSignature.accountId = secondAccountId;

  assert.equal(activation.current.createdBy.accountId, accountId);
  assert.equal(activation.current.signature.accountId, accountId);
  assert.equal(Object.isFrozen(activation), true);
  assert.equal(Object.isFrozen(activation.current), true);
  assert.equal(Object.isFrozen(activation.current.createdBy), true);
  assert.equal(Object.isFrozen(activation.current.signature), true);
});

test("[negativo] rechaza activar un estado distinto de BORRADOR", () => {
  assert.throws(
    () =>
      activateBaselineVersion(
        { ...draft(firstVersionId, 1), status: "HISTORICO" } as unknown as BaselineDraft,
        signature,
      ),
    /BASELINE_TRANSITION_INVALID/,
  );
});

for (const invalidVersion of [0, -1, 1.5, Number.NaN, Number.POSITIVE_INFINITY]) {
  test(`[negativo] rechaza el número de versión inválido ${String(invalidVersion)}`, () => {
    assert.throws(
      () => activateBaselineVersion(draft(firstVersionId, invalidVersion), signature),
      /BASELINE_VERSION_NUMBER_INVALID/,
    );
  });
}

test("[negativo] rechaza un motivo basal fuera del catálogo", () => {
  assert.throws(
    () =>
      activateBaselineVersion(
        draft(firstVersionId, 1, {
          reason: "MOTIVO_CLIENTE",
        } as unknown as Partial<BaselineDraft>),
        signature,
      ),
    /BASELINE_REASON_INVALID/,
  );
});

test("[negativo] rechaza identificadores de versión o residente no opacos", () => {
  assert.throws(
    () => activateBaselineVersion(draft("baseline-1" as BaselineVersionId, 1), signature),
    /BASELINE_IDENTIFIER_INVALID/,
  );
  assert.throws(
    () =>
      activateBaselineVersion(
        draft(firstVersionId, 1, { residentId: "resident-1" as ResidentId }),
        signature,
      ),
    /BASELINE_IDENTIFIER_INVALID/,
  );
});

test("[negativo] rechaza una autoría o firma con identificador no opaco", () => {
  assert.throws(
    () =>
      activateBaselineVersion(
        draft(firstVersionId, 1, {
          createdBy: { ...signature, accountId: "account-1" as AccountId },
        }),
        signature,
      ),
    /BASELINE_AUTHORSHIP_INVALID/,
  );
  assert.throws(
    () =>
      activateBaselineVersion(draft(firstVersionId, 1), {
        ...signature,
        accountId: "account-1" as AccountId,
      }),
    /BASELINE_AUTHORSHIP_INVALID/,
  );
});

test("[negativo] rechaza fechas de creación o firma inválidas", () => {
  assert.throws(
    () =>
      activateBaselineVersion(
        draft(firstVersionId, 1, { createdAt: "fecha-enviada-por-cliente" }),
        signature,
      ),
    /BASELINE_CREATED_AT_INVALID/,
  );
  assert.throws(
    () =>
      activateBaselineVersion(draft(firstVersionId, 1), {
        ...signature,
        signedAt: "fecha-enviada-por-cliente",
      }),
    /BASELINE_SIGNED_AT_INVALID/,
  );
});

test("[negativo] rechaza una firma anterior a la creación del borrador", () => {
  assert.throws(
    () =>
      activateBaselineVersion(draft(firstVersionId, 1), {
        ...signature,
        signedAt: "2026-09-01T07:59:59.000Z",
      }),
    /BASELINE_SIGNATURE_BEFORE_CREATION/,
  );
});

test("[negativo] rechaza una firma fuera del centro o unidad del borrador", () => {
  assert.throws(
    () =>
      activateBaselineVersion(draft(firstVersionId, 1), {
        ...signature,
        centerId: otherCenterId,
      }),
    /BASELINE_SIGNATURE_SCOPE_MISMATCH/,
  );
  assert.throws(
    () =>
      activateBaselineVersion(draft(firstVersionId, 1), {
        ...signature,
        unitId: otherUnitId,
      }),
    /BASELINE_SIGNATURE_SCOPE_MISMATCH/,
  );
});

test("[negativo] rechaza firma por cuenta o perfil distinto del creador", () => {
  assert.throws(
    () =>
      activateBaselineVersion(draft(firstVersionId, 1), {
        ...signature,
        accountId: secondAccountId,
      }),
    /BASELINE_SIGNATURE_AUTHOR_MISMATCH/,
  );
  assert.throws(
    () =>
      activateBaselineVersion(draft(firstVersionId, 1), {
        ...signature,
        activeProfile: "MEDICINA",
      }),
    /BASELINE_SIGNATURE_AUTHOR_MISMATCH/,
  );
});

test("[negativo] rechaza reemplazar el basal de otro residente", () => {
  assert.throws(
    () =>
      activateBaselineVersion(
        draft(secondVersionId, 2, { residentId: otherResidentId }),
        { ...signature, signedAt: "2026-09-03T09:30:00.000Z" },
        firstCurrent(),
      ),
    /BASELINE_RESIDENT_MISMATCH/,
  );
});

test("[negativo] rechaza reutilizar el identificador de la versión vigente", () => {
  assert.throws(
    () =>
      activateBaselineVersion(
        draft(firstVersionId, 2),
        { ...signature, signedAt: "2026-09-03T09:30:00.000Z" },
        firstCurrent(),
      ),
    /BASELINE_VERSION_ID_REUSED/,
  );
});

test("[negativo] rechaza sustituir con una versión no creciente", () => {
  assert.throws(
    () =>
      activateBaselineVersion(
        draft(secondVersionId, 1),
        { ...signature, signedAt: "2026-09-03T09:30:00.000Z" },
        firstCurrent(),
      ),
    /BASELINE_VERSION_NOT_NEWER/,
  );
});

test("[negativo] rechaza como previa una versión que no esté vigente", () => {
  const invalidPrevious = {
    ...firstCurrent(),
    status: "HISTORICO",
  } as unknown as CurrentBaselineVersion;

  assert.throws(
    () =>
      activateBaselineVersion(
        draft(secondVersionId, 2),
        { ...signature, signedAt: "2026-09-03T09:30:00.000Z" },
        invalidPrevious,
      ),
    /BASELINE_PREVIOUS_STATUS_INVALID/,
  );
});

test("[negativo] rechaza una versión previa cuya firma rompe tiempo o ámbito", () => {
  const timeInvalid = {
    ...firstCurrent(),
    signature: { ...signature, signedAt: "2026-09-01T07:00:00.000Z" },
  };
  const scopeInvalid = {
    ...firstCurrent(),
    signature: { ...signature, unitId: otherUnitId },
  };
  const newSignature = { ...signature, signedAt: "2026-09-03T09:30:00.000Z" };

  assert.throws(
    () => activateBaselineVersion(draft(secondVersionId, 2), newSignature, timeInvalid),
    /BASELINE_PREVIOUS_SIGNATURE_BEFORE_CREATION/,
  );
  assert.throws(
    () => activateBaselineVersion(draft(secondVersionId, 2), newSignature, scopeInvalid),
    /BASELINE_PREVIOUS_SIGNATURE_SCOPE_MISMATCH/,
  );
});

test("[negativo] rechaza una nueva firma que no sea posterior a la vigente", () => {
  assert.throws(
    () =>
      activateBaselineVersion(
        draft(secondVersionId, 2, { createdAt: "2026-09-01T08:05:00.000Z" }),
        { ...signature, signedAt: signature.signedAt },
        firstCurrent(),
      ),
    /BASELINE_SIGNATURE_NOT_AFTER_PREVIOUS/,
  );
});
