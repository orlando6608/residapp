import { readFileSync } from "node:fs";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";

import type {
  D1DatabaseLike,
  D1PreparedStatementLike,
  D1Primitive,
  D1ResultLike,
} from "../../db/repositories/d1.ts";

const MIGRATION_PATH = "db/migrations/0001_resident_baseline_foundation.sql";

export class SqliteD1Database implements D1DatabaseLike {
  readonly raw: DatabaseSync;

  constructor() {
    this.raw = new DatabaseSync(":memory:");
    this.raw.exec("PRAGMA foreign_keys = ON");
  }

  close(): void {
    this.raw.close();
  }

  prepare(query: string): SqliteD1Statement {
    return new SqliteD1Statement(this.raw, query);
  }

  async batch<Row = Record<string, unknown>>(
    statements: readonly D1PreparedStatementLike[],
  ): Promise<readonly D1ResultLike<Row>[]> {
    const concrete = statements.map((statement) => {
      if (!(statement instanceof SqliteD1Statement)) {
        throw new Error("SQLITE_D1_STATEMENT_REQUIRED");
      }
      return statement;
    });
    this.raw.exec("BEGIN IMMEDIATE");
    try {
      const results = concrete.map((statement) => statement.execute<Row>());
      this.raw.exec("COMMIT");
      return results;
    } catch (error) {
      this.raw.exec("ROLLBACK");
      throw error;
    }
  }
}

export class SqliteD1Statement implements D1PreparedStatementLike {
  private values: D1Primitive[] = [];
  private readonly database: DatabaseSync;
  private readonly query: string;

  constructor(database: DatabaseSync, query: string) {
    this.database = database;
    this.query = query;
  }

  bind(...values: D1Primitive[]): SqliteD1Statement {
    this.values = values;
    return this;
  }

  async first<Row = Record<string, unknown>>(): Promise<Row | null> {
    return (this.statement().get(...this.sqlValues()) as Row | undefined) ?? null;
  }

  async all<Row = Record<string, unknown>>(): Promise<D1ResultLike<Row>> {
    return Object.freeze({
      success: true,
      results: this.statement().all(...this.sqlValues()) as Row[],
    });
  }

  async run(): Promise<D1ResultLike> {
    const result = this.statement().run(...this.sqlValues());
    return Object.freeze({ success: true, meta: { changes: Number(result.changes) } });
  }

  execute<Row>(): D1ResultLike<Row> {
    if (/^\s*(select|pragma|with)\b/iu.test(this.query)) {
      return Object.freeze({
        success: true,
        results: this.statement().all(...this.sqlValues()) as Row[],
      });
    }
    const result = this.statement().run(...this.sqlValues());
    return Object.freeze({ success: true, meta: { changes: Number(result.changes) } });
  }

  private statement() {
    return this.database.prepare(this.query);
  }

  private sqlValues(): SQLInputValue[] {
    return this.values.map((value) => {
      if (typeof value === "boolean") {
        return value ? 1 : 0;
      }
      if (value instanceof ArrayBuffer) {
        return new Uint8Array(value);
      }
      return value;
    });
  }
}

export function applyInitialMigration(database: DatabaseSync): void {
  database.exec(readFileSync(MIGRATION_PATH, "utf8"));
}
