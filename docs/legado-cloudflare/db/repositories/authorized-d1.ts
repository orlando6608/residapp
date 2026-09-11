import "server-only";

import { AccessDeniedError } from "../../lib/application/errors.ts";
import { loadAuthorizationEvidence, type AuthorizationQuery } from "./authorization-subject-repository.ts";
import type { D1DatabaseLike, D1PreparedStatementLike, D1Primitive, D1ResultLike } from "./d1.ts";

/**
 * Conserva la política pura: SQL vuelve a comprobar los mismos hechos autorizados,
 * sin implementar otra tabla de permisos. El guard y la operación comparten batch.
 * También protege lecturas preliminares y recuperaciones idempotentes.
 */
export function bindAuthorizedDatabase(
  database: D1DatabaseLike,
  query: AuthorizationQuery,
  evidence: string,
): D1DatabaseLike {
  const statements = new WeakMap<D1PreparedStatementLike, D1PreparedStatementLike>();

  async function execute<Row = Record<string, unknown>>(raw: readonly D1PreparedStatementLike[]): Promise<readonly D1ResultLike<Row>[]> {
    const guard = database.prepare(
      `select json(case when coalesce((${query.sql}), '') = ? then 'true'
         else 'AUTHORIZATION_CONTEXT_CHANGED' end) as authorization_guard`,
    ).bind(...query.values, evidence);
    try {
      const results = await database.batch<Row>([guard, ...raw]);
      if (results.length !== raw.length + 1 || results.some((result) => result.success === false)) {
        throw new Error("D1_BATCH_FAILED");
      }
      return results.slice(1);
    } catch (error) {
      if (await loadAuthorizationEvidence(database, query) !== evidence) {
        throw new AccessDeniedError();
      }
      throw error;
    }
  }

  function wrap(raw: D1PreparedStatementLike): D1PreparedStatementLike {
    const statement: D1PreparedStatementLike = Object.freeze({
      bind(...values: D1Primitive[]) { return wrap(raw.bind(...values)); },
      async first<Row>() { return (await execute<Row>([raw]))[0]?.results?.[0] ?? null; },
      async all<Row>() { return (await execute<Row>([raw]))[0]; },
      async run() { return (await execute([raw]))[0]; },
    });
    statements.set(statement, raw);
    return statement;
  }

  return Object.freeze({
    prepare(sql: string) { return wrap(database.prepare(sql)); },
    async batch<Row>(batch: readonly D1PreparedStatementLike[]) {
      return execute<Row>(batch.map((statement) => {
        const raw = statements.get(statement);
        if (!raw) throw new AccessDeniedError();
        return raw;
      }));
    },
  });
}
