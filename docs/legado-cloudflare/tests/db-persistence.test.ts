import assert from "node:assert/strict";
import { spawnSync, type SpawnSyncReturns } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { after, before, test } from "node:test";
import { convertV4MiniflareOptions, Miniflare } from "miniflare";

import { readBaselineAsClinicalDirection } from "../db/repositories/audit-repository.ts";
import {
  signBaselineDraft,
  type SignBaselineDraftInput,
} from "../db/repositories/baseline-repository.ts";
import type {
  D1DatabaseLike,
  D1PreparedStatementLike,
  D1ResultLike,
} from "../db/repositories/d1.ts";
import {
  createResidentWithInitialLocation,
  type CreateResidentInput,
} from "../db/repositories/resident-repository.ts";
import {
  createCompleteDraft,
  createCompleteDraftD1,
  IDS,
  seedFoundation,
  seedFoundationD1,
} from "./fixtures/db-fixtures.ts";
import { applyInitialMigration, SqliteD1Database } from "./support/sqlite-d1.ts";

let wranglerTemp = "";
let firstMigration!: SpawnSyncReturns<string>;
let secondMigration!: SpawnSyncReturns<string>;
let foreignKeyCheck!: SpawnSyncReturns<string>;

before(() => {
  wranglerTemp = mkdtempSync(join(tmpdir(), "connect-d1-0001-"));
  firstMigration = runWrangler([
    "d1",
    "migrations",
    "apply",
    "DB",
    "--local",
    "--persist-to",
    wranglerTemp,
  ]);
  secondMigration = runWrangler([
    "d1",
    "migrations",
    "apply",
    "DB",
    "--local",
    "--persist-to",
    wranglerTemp,
  ]);
  foreignKeyCheck = runWrangler([
    "d1",
    "execute",
    "DB",
    "--local",
    "--persist-to",
    wranglerTemp,
    "--command",
    "PRAGMA foreign_key_check;",
    "--json",
  ]);
});

after(() => {
  if (wranglerTemp.startsWith(tmpdir())) {
    rmSync(wranglerTemp, { recursive: true, force: true });
  }
});

test("DB-T01 migra una D1 local vacía de forma reproducible", () => {
  assert.equal(firstMigration.status, 0, commandOutput(firstMigration));
  assert.match(commandOutput(firstMigration), /0001_resident_baseline_foundation\.sql/u);
  assert.match(commandOutput(firstMigration), /143 commands executed successfully/u);
});

test("DB-T02 una segunda aplicación no crea cambios ni duplicados", () => {
  assert.equal(secondMigration.status, 0, commandOutput(secondMigration));
  assert.match(commandOutput(secondMigration), /No migrations to apply/u);
});

test("DB-T03 PRAGMA foreign_key_check devuelve cero violaciones", () => {
  assert.equal(foreignKeyCheck.status, 0, commandOutput(foreignKeyCheck));
  assert.match(commandOutput(foreignKeyCheck), /"results":\s*\[\]/u);
});

test("DB-T04 rechaza combinar centro y unidad de ámbitos distintos", () => {
  withDatabase((database) => {
    database.raw
      .prepare("update resident_location_intervals set valid_until = ? where id = ?")
      .run("2026-09-06T09:00:00.000Z", IDS.locationA);
    assert.throws(
      () =>
        database.raw
          .prepare(
            `insert into resident_location_intervals
              (id, resident_id, center_id, episode_id, unit_id, valid_from, changed_at,
               changed_by_account_id, changed_by_profile)
             values (?, ?, ?, ?, ?, ?, ?, ?, 'ADMINISTRACION')`,
          )
          .run(
            "42000000-0000-4000-8000-000000000002",
            IDS.residentA,
            IDS.centerA,
            IDS.episodeA,
            IDS.unitB,
            "2026-09-06T10:00:00.000Z",
            "2026-09-06T10:00:00.000Z",
            IDS.admin,
          ),
      /RESIDENT_CREATE_NOT_AUTHORIZED|FOREIGN KEY constraint failed/u,
    );
  });
});

test("DB-T05 impide dos ubicaciones activas para un residente", () => {
  withDatabase((database) => {
    assert.throws(
      () =>
        database.raw
          .prepare(
            `insert into resident_location_intervals
              (id, resident_id, center_id, episode_id, unit_id, valid_from, changed_at,
               changed_by_account_id, changed_by_profile)
             values (?, ?, ?, ?, ?, ?, ?, ?, 'ADMINISTRACION')`,
          )
          .run(
            "42000000-0000-4000-8000-000000000002",
            IDS.residentA,
            IDS.centerA,
            IDS.episodeA,
            IDS.unitA,
            "2026-09-06T10:00:00.000Z",
            "2026-09-06T10:00:00.000Z",
            IDS.admin,
          ),
      /RESIDENT_LOCATION_OVERLAP|UNIQUE constraint failed/u,
    );
  });
});

test("DB-T06 impide dos borradores basales activos", () => {
  withDatabase((database) => {
    createCompleteDraft(database.raw);
    assert.throws(
      () =>
        database.raw
          .prepare(
            `insert into baseline_drafts
              (id, resident_id, center_id, created_in_unit_id, status,
               created_by_account_id, created_by_profile, created_at,
               updated_by_account_id, updated_by_profile, updated_at, draft_revision)
             values (?, ?, ?, ?, 'ACTIVE', ?, 'ENFERMERIA', ?, ?, 'ENFERMERIA', ?, 1)`,
          )
          .run(
            IDS.draftB,
            IDS.residentA,
            IDS.centerA,
            IDS.unitA,
            IDS.nurseA,
            "2026-09-06T09:00:00.000Z",
            IDS.nurseA,
            "2026-09-06T09:00:00.000Z",
          ),
      /UNIQUE constraint failed/u,
    );
  });
});

test("DB-T07 rechaza firma por cuenta o perfil distinto del creador", async () => {
  await withDatabaseAsync(async (database) => {
    createCompleteDraft(database.raw);
    await assert.rejects(
      signBaselineDraft(database, signInput({ accountId: IDS.nurseB })),
      /BASELINE_SIGN_NOT_AUTHORIZED/u,
    );
    await assert.rejects(
      signBaselineDraft(database, signInput({ activeProfile: "MEDICINA" })),
      /BASELINE_SIGN_NOT_AUTHORIZED/u,
    );
    assert.equal(rowCount(database, "baseline_versions"), 0);
  });
});

test("DB-T08 rechaza área o Barthel incompletos sin estado parcial", async () => {
  await withDatabaseAsync(async (database) => {
    createCompleteDraft(database.raw, { omitArea: "SUENO" });
    await assert.rejects(signBaselineDraft(database, signInput()), /BASELINE_AREAS_INCOMPLETE/u);
    assertNoPartialSignature(database);
  });
  await withDatabaseAsync(async (database) => {
    createCompleteDraft(database.raw, { omitBarthelItem: "ESCALERAS" });
    await assert.rejects(
      signBaselineDraft(database, signInput()),
      /BASELINE_BARTHEL_INCOMPLETE/u,
    );
    assertNoPartialSignature(database);
  });
});

test("DB-T09 dos accesos D1 locales independientes compiten y dejan una sola vigente", async () => {
  const miniflare = new Miniflare(
    convertV4MiniflareOptions({
      workers: [miniflareWorker("db-t09-a"), miniflareWorker("db-t09-b")],
    }),
  );
  try {
    const firstAccess = (await miniflare.getD1Database(
      "DB",
      "db-t09-a",
    )) as unknown as LocalD1Database;
    await applyMigrationToD1(firstAccess);
    await seedFoundationD1(firstAccess);
    await createCompleteDraftD1(firstAccess);
    const secondAccess = (await miniflare.getD1Database(
      "DB",
      "db-t09-b",
    )) as unknown as LocalD1Database;
    assert.notEqual(firstAccess, secondAccess);
    const barrier = new BatchBarrier(2);
    const attempts = await Promise.allSettled([
      signBaselineDraft(
        waitAtBatch(firstAccess, barrier),
        signInput({ operationId: "70000000-0000-4000-8000-000000000001" }),
      ),
      signBaselineDraft(
        waitAtBatch(secondAccess, barrier),
        signInput({ operationId: "70000000-0000-4000-8000-000000000002" }),
      ),
    ]);
    assert.equal(attempts.filter((attempt) => attempt.status === "fulfilled").length, 1);
    assert.equal(attempts.filter((attempt) => attempt.status === "rejected").length, 1);
    assert.equal(await d1RowCount(firstAccess, "baseline_versions"), 1);
    assert.equal(await d1RowCount(secondAccess, "resident_current_baselines"), 1);
    assert.equal(await d1RowCount(firstAccess, "idempotency_operations"), 1);
  } finally {
    await miniflare.dispose();
  }
});

test("DB-T10 reintentar la misma firma devuelve el mismo resultado", async () => {
  await withDatabaseAsync(async (database) => {
    createCompleteDraft(database.raw);
    const input = signInput();
    const first = await signBaselineDraft(database, input);
    const second = await signBaselineDraft(database, input);
    assert.deepEqual(second, first);
    assert.equal(rowCount(database, "baseline_versions"), 1);
    assert.equal(rowCount(database, "idempotency_operations"), 1);
  });
});

test("DB-T11 la base rechaza modificar o borrar una versión firmada", async () => {
  await withDatabaseAsync(async (database) => {
    createCompleteDraft(database.raw);
    const signed = await signBaselineDraft(database, signInput());
    assert.throws(
      () =>
        database.raw
          .prepare("update baseline_versions set reason_code = 'REVISION_PROGRAMADA' where id = ?")
          .run(signed.baselineVersionId),
      /BASELINE_VERSION_IMMUTABLE/u,
    );
    assert.throws(
      () => database.raw.prepare("delete from baseline_versions where id = ?").run(signed.baselineVersionId),
      /BASELINE_VERSION_IMMUTABLE/u,
    );
  });
});

test("DB-T12 sustituye la vigente conservando intacta la anterior", async () => {
  await withDatabaseAsync(async (database) => {
    createCompleteDraft(database.raw);
    const first = await signBaselineDraft(database, signInput());
    createCompleteDraft(database.raw, {
      id: IDS.draftB,
      reasonCode: "REVISION_PROGRAMADA",
    });
    const second = await signBaselineDraft(
      database,
      signInput({
        draftId: IDS.draftB,
        operationId: "70000000-0000-4000-8000-000000000002",
      }),
    );
    assert.equal(second.versionNumber, 2);
    const current = database.raw
      .prepare("select baseline_version_id from resident_current_baselines where resident_id = ?")
      .get(IDS.residentA) as { baseline_version_id: string };
    assert.equal(current.baseline_version_id, second.baselineVersionId);
    const link = database.raw
      .prepare(
        "select new_version_id from baseline_supersessions where previous_version_id = ?",
      )
      .get(first.baselineVersionId) as { new_version_id: string };
    assert.equal(link.new_version_id, second.baselineVersionId);
    assert.equal(
      Number(
        (database.raw
          .prepare("select count(*) count from baseline_version_areas where baseline_version_id = ?")
          .get(first.baselineVersionId) as { count: number }).count,
      ),
      9,
    );
  });
});

test("DB-T13 Dirección falla cerrada si no puede escribir auditoría", async () => {
  await withDatabaseAsync(async (database) => {
    createCompleteDraft(database.raw);
    await signBaselineDraft(database, signInput());
    database.raw.exec(
      `create trigger audit_events_block_test before insert on audit_events
       begin select raise(abort, 'AUDIT_WRITE_BLOCKED'); end`,
    );
    await assert.rejects(
      readBaselineAsClinicalDirection(database, {
        accountId: IDS.direction,
        centerId: IDS.centerA,
        unitId: IDS.unitA,
        residentId: IDS.residentA,
        resourceType: "BASELINE_CURRENT",
        purposeCode: "SUPERVISION_CLINICA",
      }),
      /AUDIT_WRITE_BLOCKED/u,
    );
    database.raw.exec("drop trigger audit_events_block_test");
    const rows = await readBaselineAsClinicalDirection(database, {
      accountId: IDS.direction,
      centerId: IDS.centerA,
      unitId: IDS.unitA,
      residentId: IDS.residentA,
      resourceType: "BASELINE_CURRENT",
      purposeCode: "SUPERVISION_CLINICA",
    });
    assert.equal(rows.length, 1);
    assert.equal(
      Number(
        (database.raw
          .prepare("select count(*) count from audit_events where action_code = 'CLINICAL_DETAIL_READ'")
          .get() as { count: number }).count,
      ),
      1,
    );
  });
});

test("DB-T14 auditoría y aportaciones son append-only", async () => {
  await withDatabaseAsync(async (database) => {
    createCompleteDraft(database.raw);
    database.raw
      .prepare(
        `insert into baseline_draft_contributions
          (id, draft_id, resident_id, center_id, unit_id, area_code, component_code,
           change_payload, account_id, active_profile, draft_revision, contributed_at)
         values (?, ?, ?, ?, ?, 'MOVILIDAD', 'technicalAidCode', '{}', ?, 'ENFERMERIA', 1, ?)`,
      )
      .run(
        "80000000-0000-4000-8000-000000000001",
        IDS.draftA,
        IDS.residentA,
        IDS.centerA,
        IDS.unitA,
        IDS.nurseB,
        "2026-09-06T08:05:00.000Z",
      );
    await signBaselineDraft(database, signInput());
    assert.throws(
      () => database.raw.exec("update baseline_draft_contributions set component_code = 'x'"),
      /BASELINE_CONTRIBUTION_IMMUTABLE/u,
    );
    assert.throws(
      () => database.raw.exec("delete from baseline_draft_contributions"),
      /BASELINE_CONTRIBUTION_IMMUTABLE/u,
    );
    assert.throws(
      () => database.raw.exec("update audit_events set resource_type = 'x'"),
      /AUDIT_EVENT_IMMUTABLE/u,
    );
    assert.throws(
      () => database.raw.exec("delete from audit_events"),
      /AUDIT_EVENT_IMMUTABLE/u,
    );
  });
});

test("DB-T15 manipular centro, unidad, residente o perfil deniega sin revelar existencia", async () => {
  await withDatabaseAsync(async (database) => {
    createCompleteDraft(database.raw);
    const attempts = [
      signInput({ centerId: IDS.centerB, operationId: "70000000-0000-4000-8000-000000000011" }),
      signInput({ unitId: IDS.unitB, operationId: "70000000-0000-4000-8000-000000000012" }),
      signInput({ residentId: IDS.residentB, operationId: "70000000-0000-4000-8000-000000000013" }),
      signInput({ activeProfile: "MEDICINA", operationId: "70000000-0000-4000-8000-000000000014" }),
    ];
    for (const attempt of attempts) {
      await assert.rejects(
        signBaselineDraft(database, attempt),
        /BASELINE_SIGN_NOT_AUTHORIZED/u,
      );
    }
    assert.equal(rowCount(database, "baseline_versions"), 0);
  });
});

test("DB-T16 la puerta del repositorio incluye typecheck, lint, tests y ambos builds", () => {
  const packageJson = JSON.parse(readFileSync("package.json", "utf8")) as {
    scripts?: Record<string, string>;
  };
  assert.equal(
    packageJson.scripts?.check,
    "pnpm typecheck && pnpm lint && pnpm test && pnpm build:next && pnpm build",
  );
  assert.equal(packageJson.scripts?.dbTest, undefined);
  assert.equal(packageJson.scripts?.["db:test"], "node --conditions=react-server --experimental-strip-types --test tests/db-persistence.test.ts");
});

test("CHAIN-T01 rechaza establecer inicialmente como vigente una versión distinta de 1", () => {
  withDatabase((database) => {
    const versions = createStoredVersionChain(database, 2);
    assert.throws(
      () => insertCurrentPointer(database, versions[1]),
      /BASELINE_CURRENT_INITIAL_INVALID/u,
    );
    assert.equal(rowCount(database, "resident_current_baselines"), 0);
    assert.equal(rowCount(database, "baseline_supersessions"), 0);
  });
});

test("CHAIN-T02 rechaza enlazar versiones no consecutivas sin estado parcial", () => {
  withDatabase((database) => {
    const versions = createStoredVersionChain(database, 3);
    insertCurrentPointer(database, versions[0]);
    assert.throws(
      () => insertSupersession(database, versions[0], versions[2], versions[2].signedAt),
      /BASELINE_SUPERSESSION_INVALID/u,
    );
    assertCurrentChainState(database, versions[0].id, 0);
  });
});

test("CHAIN-T03 rechaza sustituir una versión que no es la vigente", () => {
  withDatabase((database) => {
    const versions = createStoredVersionChain(database, 3);
    insertCurrentPointer(database, versions[0]);
    assert.throws(
      () => insertSupersession(database, versions[1], versions[2], versions[2].signedAt),
      /BASELINE_SUPERSESSION_INVALID/u,
    );
    assertCurrentChainState(database, versions[0].id, 0);
  });
});

test("CHAIN-T04 rechaza relaciones invertidas o temporalmente incoherentes", () => {
  withDatabase((database) => {
    const versions = createStoredVersionChain(database, 2);
    insertCurrentPointer(database, versions[0]);
    assert.throws(
      () => insertSupersession(database, versions[0], versions[1], versions[0].signedAt),
      /BASELINE_SUPERSESSION_INVALID/u,
    );
    assertCurrentChainState(database, versions[0].id, 0);

    insertSupersession(database, versions[0], versions[1], versions[1].signedAt);
    updateCurrentPointer(database, versions[1]);
    assert.throws(
      () => insertSupersession(database, versions[1], versions[0], versions[0].signedAt),
      /BASELINE_SUPERSESSION_INVALID/u,
    );
    assertCurrentChainState(database, versions[1].id, 1);
  });
});

test("CHAIN-T05 rechaza actualizar el puntero sin la sustitución válida correspondiente", () => {
  withDatabase((database) => {
    const versions = createStoredVersionChain(database, 2);
    insertCurrentPointer(database, versions[0]);
    assert.throws(
      () => updateCurrentPointer(database, versions[1]),
      /BASELINE_CURRENT_TRANSITION_INVALID/u,
    );
    assertCurrentChainState(database, versions[0].id, 0);
  });
});

test("RES-T01 alta autorizada sin referencia interna es atómica e idempotente", async () => {
  await withDatabaseAsync(async (database) => {
    const input = residentInput();
    const before = residentWriteCounts(database);
    const first = await createResidentWithInitialLocation(database, input);
    const second = await createResidentWithInitialLocation(database, input);
    assert.deepEqual(second, first);
    assert.deepEqual(residentWriteCounts(database), {
      residents: before.residents + 1,
      episodes: before.episodes + 1,
      locations: before.locations + 1,
      audits: before.audits + 1,
      operations: before.operations + 1,
    });
    const episode = database.raw
      .prepare("select internal_reference from resident_center_episodes where id = ?")
      .get(first.episodeId) as { internal_reference: string | null };
    assert.equal(episode.internal_reference, null);
    const residentLocation = database.raw
      .prepare(
        `select resident.id resident_id, episode.id episode_id, location.id location_id
           from residents resident
           join resident_center_episodes episode on episode.resident_id = resident.id
           join resident_location_intervals location on location.episode_id = episode.id
          where resident.id = ?`,
      )
      .get(first.residentId) as {
        resident_id: string;
        episode_id: string;
        location_id: string;
      };
    assert.equal(residentLocation.resident_id, first.residentId);
    assert.equal(residentLocation.episode_id, first.episodeId);
    assert.equal(residentLocation.location_id, first.locationIntervalId);
  });
});

test("RES-T02 misma clave idempotente con contenido diferente se rechaza", async () => {
  await withDatabaseAsync(async (database) => {
    const input = residentInput();
    await createResidentWithInitialLocation(database, input);
    await assert.rejects(
      createResidentWithInitialLocation(database, {
        ...input,
        displayName: "Residente Sintético Diferente",
      }),
      /IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST/u,
    );
    assert.equal(rowCount(database, "residents"), 2);
    assert.equal(rowCount(database, "idempotency_operations"), 1);
  });
});

test("RES-T03 revocar permiso justo antes del batch deniega el alta sin estado parcial", async () => {
  await withDatabaseAsync(async (database) => {
    const before = residentWriteCounts(database);
    const guarded = beforeBatch(database, () => {
      revokePermission(database, IDS.nurseA, "RESIDENT_IDENTITY_CREATE");
    });
    await assert.rejects(
      createResidentWithInitialLocation(guarded, residentInput()),
      /RESIDENT_CREATE_NOT_AUTHORIZED/u,
    );
    assert.deepEqual(residentWriteCounts(database), before);
  });
});

test("RES-T04 revocar unidad justo antes del batch deniega el alta sin estado parcial", async () => {
  await withDatabaseAsync(async (database) => {
    const before = residentWriteCounts(database);
    const guarded = beforeBatch(database, () => {
      revokeUnitScope(database, IDS.nurseA);
    });
    await assert.rejects(
      createResidentWithInitialLocation(guarded, residentInput()),
      /RESIDENT_CREATE_NOT_AUTHORIZED/u,
    );
    assert.deepEqual(residentWriteCounts(database), before);
  });
});

test("RES-T05 un fallo de sentencia revierte residente, episodio, ubicación, auditoría e idempotencia", async () => {
  await withDatabaseAsync(async (database) => {
    const before = residentWriteCounts(database);
    database.raw.exec(
      `create trigger resident_location_block_test before insert on resident_location_intervals
       begin select raise(abort, 'RESIDENT_LOCATION_TEST_FAILURE'); end`,
    );
    await assert.rejects(
      createResidentWithInitialLocation(database, residentInput()),
      /RESIDENT_LOCATION_TEST_FAILURE/u,
    );
    assert.deepEqual(residentWriteCounts(database), before);
  });
});

test("TOCTOU-T01 revocar firma justo antes del batch no firma ni deja estado parcial", async () => {
  await withDatabaseAsync(async (database) => {
    createCompleteDraft(database.raw);
    const guarded = beforeBatch(database, () => {
      revokePermission(database, IDS.nurseA, "BASELINE_INITIAL_COMPLETE");
    });
    await assert.rejects(signBaselineDraft(guarded, signInput()), /BASELINE_SIGN_NOT_AUTHORIZED/u);
    assertNoPartialSignature(database);
  });
});

test("TOCTOU-T02 Dirección revocada justo antes del batch no recibe datos ni deja auditoría", async () => {
  await withDatabaseAsync(async (database) => {
    createCompleteDraft(database.raw);
    await signBaselineDraft(database, signInput());
    const auditCount = clinicalReadAuditCount(database);
    let received: readonly unknown[] | undefined;
    const guarded = beforeBatch(database, () => {
      revokePermission(database, IDS.direction, "CLINICAL_DETAIL_READ");
    });
    await assert.rejects(
      async () => {
        received = await readBaselineAsClinicalDirection(guarded, directionReadInput());
      },
      /CLINICAL_DETAIL_READ_NOT_AUTHORIZED/u,
    );
    assert.equal(received, undefined);
    assert.equal(clinicalReadAuditCount(database), auditCount);
  });
});

function withDatabase(run: (database: SqliteD1Database) => void): void {
  const database = preparedDatabase();
  try {
    run(database);
  } finally {
    database.close();
  }
}

type StoredVersion = Readonly<{
  id: string;
  draftId: string;
  versionNumber: number;
  signedAt: string;
}>;

function createStoredVersionChain(
  database: SqliteD1Database,
  count: 2 | 3,
): readonly StoredVersion[] {
  const drafts = [IDS.draftA, IDS.draftB, IDS.draftC] as const;
  const versionIds = [
    "72000000-0000-4000-8000-000000000001",
    "72000000-0000-4000-8000-000000000002",
    "72000000-0000-4000-8000-000000000003",
  ] as const;
  const signedTimes = [
    "2026-09-06T09:00:00.000Z",
    "2026-09-06T10:00:00.000Z",
    "2026-09-06T11:00:00.000Z",
  ] as const;
  const versions: StoredVersion[] = [];
  for (let index = 0; index < count; index += 1) {
    const version = {
      id: versionIds[index],
      draftId: drafts[index],
      versionNumber: index + 1,
      signedAt: signedTimes[index],
    };
    createCompleteDraft(database.raw, {
      id: version.draftId,
      reasonCode: index === 0 ? "ALTA" : "REVISION_PROGRAMADA",
    });
    materializeStoredVersion(database, version);
    versions.push(version);
  }
  return versions;
}

function materializeStoredVersion(database: SqliteD1Database, version: StoredVersion): void {
  database.raw
    .prepare(
      `insert into baseline_versions
        (id, source_draft_id, resident_id, center_id, created_in_unit_id,
         version_number, reason_code, common_information_source_code,
         common_information_source_other_text, common_information_date,
         created_by_account_id, created_by_profile, created_at,
         signed_by_account_id, signed_by_profile, signed_at, valid_from,
         activation_operation_id)
       select ?, draft.id, draft.resident_id, draft.center_id, draft.created_in_unit_id,
              ?, draft.reason_code, draft.common_information_source_code,
              draft.common_information_source_other_text, draft.common_information_date,
              draft.created_by_account_id, draft.created_by_profile, draft.created_at,
              draft.created_by_account_id, draft.created_by_profile, ?, ?, ?
         from baseline_drafts draft where draft.id = ?`,
    )
    .run(
      version.id,
      version.versionNumber,
      version.signedAt,
      version.signedAt,
      `${version.id}:activation`,
      version.draftId,
    );
  database.raw
    .prepare(
      `insert into baseline_version_areas
        (id, baseline_version_id, resident_id, center_id, area_code,
         catalog_version_code, answer_payload, observation,
         information_source_override_code, information_source_override_other_text,
         information_date_override, recorded_by_account_id, recorded_by_profile, recorded_at)
       select ? || ':' || area.id, ?, area.resident_id, area.center_id, area.area_code,
              area.catalog_version_code, area.answer_payload, area.observation,
              area.information_source_override_code, area.information_source_override_other_text,
              area.information_date_override, area.recorded_by_account_id,
              area.recorded_by_profile, area.recorded_at
         from baseline_draft_areas area where area.draft_id = ?`,
    )
    .run(version.id, version.id, version.draftId);
  database.raw
    .prepare(
      `insert into baseline_version_barthel
        (id, baseline_version_id, resident_id, center_id, instrument_version_code,
         assessment_date, total_score, recorded_by_account_id, recorded_by_profile, recorded_at)
       select ? || ':barthel', ?, assessment.resident_id, assessment.center_id,
              assessment.instrument_version_code, assessment.assessment_date,
              assessment.total_score, assessment.recorded_by_account_id,
              assessment.recorded_by_profile, assessment.recorded_at
         from baseline_draft_barthel assessment where assessment.draft_id = ?`,
    )
    .run(version.id, version.id, version.draftId);
  database.raw
    .prepare(
      `insert into baseline_version_barthel_items
        (id, barthel_id, baseline_version_id, resident_id, center_id,
         instrument_version_code, item_code, selected_option_code, awarded_score)
       select ? || ':' || item.id, ? || ':barthel', ?, item.resident_id, item.center_id,
              item.instrument_version_code, item.item_code, item.selected_option_code,
              item.awarded_score
         from baseline_draft_barthel_items item where item.draft_id = ?`,
    )
    .run(version.id, version.id, version.id, version.draftId);
  database.raw
    .prepare(
      `update baseline_drafts
          set status = 'SIGNED', updated_at = ?, updated_by_account_id = created_by_account_id,
              updated_by_profile = created_by_profile
        where id = ?`,
    )
    .run(version.signedAt, version.draftId);
}

function insertCurrentPointer(database: SqliteD1Database, version: StoredVersion): void {
  database.raw
    .prepare(
      `insert into resident_current_baselines
        (resident_id, center_id, baseline_version_id, activated_at)
       values (?, ?, ?, ?)`,
    )
    .run(IDS.residentA, IDS.centerA, version.id, version.signedAt);
}

function insertSupersession(
  database: SqliteD1Database,
  previous: StoredVersion,
  next: StoredVersion,
  supersededAt: string,
): void {
  database.raw
    .prepare(
      `insert into baseline_supersessions
        (previous_version_id, new_version_id, resident_id, center_id, superseded_at)
       values (?, ?, ?, ?, ?)`,
    )
    .run(previous.id, next.id, IDS.residentA, IDS.centerA, supersededAt);
}

function updateCurrentPointer(database: SqliteD1Database, version: StoredVersion): void {
  database.raw
    .prepare(
      `update resident_current_baselines
          set baseline_version_id = ?, activated_at = ?
        where resident_id = ? and center_id = ?`,
    )
    .run(version.id, version.signedAt, IDS.residentA, IDS.centerA);
}

function assertCurrentChainState(
  database: SqliteD1Database,
  expectedCurrentId: string,
  expectedSupersessions: number,
): void {
  const current = database.raw
    .prepare("select baseline_version_id from resident_current_baselines where resident_id = ?")
    .get(IDS.residentA) as { baseline_version_id: string };
  assert.equal(current.baseline_version_id, expectedCurrentId);
  assert.equal(rowCount(database, "baseline_supersessions"), expectedSupersessions);
}

type LocalD1Database = D1DatabaseLike & {
  exec(query: string): Promise<unknown>;
};

function miniflareWorker(name: string) {
  return {
    name,
    modules: true,
    script: "export default { fetch() { return new Response('local-only') } }",
    compatibilityDate: "2026-09-06",
    d1Databases: { DB: "resident-baseline-db-t09" },
  };
}

class BatchBarrier {
  private arrivals = 0;
  private readonly expected: number;
  private release!: () => void;
  private readonly released: Promise<void>;

  constructor(expected: number) {
    this.expected = expected;
    this.released = new Promise((resolve) => {
      this.release = resolve;
    });
  }

  async arrive(): Promise<void> {
    this.arrivals += 1;
    if (this.arrivals === this.expected) {
      this.release();
    }
    await this.released;
  }
}

function waitAtBatch(database: D1DatabaseLike, barrier: BatchBarrier): D1DatabaseLike {
  return {
    prepare(query: string): D1PreparedStatementLike {
      return database.prepare(query);
    },
    async batch<Row = Record<string, unknown>>(
      statements: readonly D1PreparedStatementLike[],
    ): Promise<readonly D1ResultLike<Row>[]> {
      await barrier.arrive();
      return database.batch<Row>(statements);
    },
  };
}

function beforeBatch(database: D1DatabaseLike, action: () => void): D1DatabaseLike {
  let pending = true;
  return {
    prepare(query: string): D1PreparedStatementLike {
      return database.prepare(query);
    },
    async batch<Row = Record<string, unknown>>(
      statements: readonly D1PreparedStatementLike[],
    ): Promise<readonly D1ResultLike<Row>[]> {
      if (pending) {
        pending = false;
        action();
      }
      return database.batch<Row>(statements);
    },
  };
}

async function d1RowCount(database: D1DatabaseLike, table: string): Promise<number> {
  if (!/^[a-z_]+$/u.test(table)) {
    throw new Error("TEST_TABLE_INVALID");
  }
  const row = await database.prepare(`select count(*) count from ${table}`).first<{ count: number }>();
  return Number(row?.count ?? 0);
}

async function applyMigrationToD1(database: D1DatabaseLike): Promise<void> {
  const migration = readFileSync("db/migrations/0001_resident_baseline_foundation.sql", "utf8");
  for (const statement of migration.split("--> statement-breakpoint")) {
    if (statement.trim().length > 0) {
      await database.prepare(statement).run();
    }
  }
}

async function withDatabaseAsync(
  run: (database: SqliteD1Database) => Promise<void>,
): Promise<void> {
  const database = preparedDatabase();
  try {
    await run(database);
  } finally {
    database.close();
  }
}

function preparedDatabase(): SqliteD1Database {
  const database = new SqliteD1Database();
  applyInitialMigration(database.raw);
  seedFoundation(database.raw);
  return database;
}

function signInput(
  overrides: Partial<SignBaselineDraftInput> = {},
): SignBaselineDraftInput {
  return {
    accountId: IDS.nurseA,
    activeProfile: "ENFERMERIA",
    centerId: IDS.centerA,
    unitId: IDS.unitA,
    residentId: IDS.residentA,
    draftId: IDS.draftA,
    expectedDraftRevision: 1,
    operationId: "70000000-0000-4000-8000-000000000001",
    ...overrides,
  };
}

function residentInput(overrides: Partial<CreateResidentInput> = {}): CreateResidentInput {
  return {
    accountId: IDS.nurseA,
    activeProfile: "ENFERMERIA",
    centerId: IDS.centerA,
    unitId: IDS.unitA,
    displayName: "Residente Sintético Nuevo",
    birthDate: "1945-02-03",
    documentedSexCode: "unknown",
    operationId: "71000000-0000-4000-8000-000000000001",
    ...overrides,
  };
}

function directionReadInput() {
  return {
    accountId: IDS.direction,
    centerId: IDS.centerA,
    unitId: IDS.unitA,
    residentId: IDS.residentA,
    resourceType: "BASELINE_CURRENT" as const,
    purposeCode: "SUPERVISION_CLINICA" as const,
  };
}

function revokePermission(database: SqliteD1Database, accountId: string, code: string): void {
  database.raw
    .prepare(
      `update profile_permissions
          set revoked_at = '2026-09-06T08:30:00.000Z', revoked_by_account_id = ?
        where permission_code = ? and revoked_at is null
          and profile_scope_id = (select id from profile_scopes where account_id = ? and status = 'ACTIVE')`,
    )
    .run(IDS.admin, code, accountId);
}

function revokeUnitScope(database: SqliteD1Database, accountId: string): void {
  database.raw
    .prepare(
      `update profile_unit_scopes
          set revoked_at = '2026-09-06T08:30:00.000Z', revoked_by_account_id = ?
        where revoked_at is null
          and profile_scope_id = (select id from profile_scopes where account_id = ? and status = 'ACTIVE')`,
    )
    .run(IDS.admin, accountId);
}

function residentWriteCounts(database: SqliteD1Database) {
  return {
    residents: rowCount(database, "residents"),
    episodes: rowCount(database, "resident_center_episodes"),
    locations: rowCount(database, "resident_location_intervals"),
    audits: Number(
      (
        database.raw
          .prepare("select count(*) count from audit_events where action_code = 'RESIDENT_CREATE'")
          .get() as { count: number }
      ).count,
    ),
    operations: Number(
      (
        database.raw
          .prepare("select count(*) count from idempotency_operations where action_code = 'RESIDENT_CREATE'")
          .get() as { count: number }
      ).count,
    ),
  };
}

function clinicalReadAuditCount(database: SqliteD1Database): number {
  return Number(
    (
      database.raw
        .prepare("select count(*) count from audit_events where action_code = 'CLINICAL_DETAIL_READ'")
        .get() as { count: number }
    ).count,
  );
}

function rowCount(database: SqliteD1Database, table: string): number {
  if (!/^[a-z_]+$/u.test(table)) {
    throw new Error("TEST_TABLE_INVALID");
  }
  return Number(
    (database.raw.prepare(`select count(*) count from ${table}`).get() as { count: number }).count,
  );
}

function assertNoPartialSignature(database: SqliteD1Database): void {
  assert.equal(rowCount(database, "baseline_versions"), 0);
  assert.equal(rowCount(database, "resident_current_baselines"), 0);
  assert.equal(rowCount(database, "idempotency_operations"), 0);
  const draft = database.raw
    .prepare("select status from baseline_drafts where id = ?")
    .get(IDS.draftA) as { status: string };
  assert.equal(draft.status, "ACTIVE");
}

function runWrangler(args: readonly string[]): SpawnSyncReturns<string> {
  const executable = join(process.cwd(), "node_modules", "wrangler", "bin", "wrangler.js");
  return spawnSync(process.execPath, [executable, ...args], {
    cwd: process.cwd(),
    encoding: "utf8",
    env: { ...process.env, XDG_CONFIG_HOME: wranglerTemp },
  });
}

function commandOutput(result: SpawnSyncReturns<string>): string {
  return `${result.stdout ?? ""}\n${result.stderr ?? ""}`;
}
