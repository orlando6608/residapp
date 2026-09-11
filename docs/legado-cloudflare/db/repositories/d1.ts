export type D1Primitive = string | number | boolean | null | ArrayBuffer;

export type D1ResultLike<Row = Record<string, unknown>> = Readonly<{
  success?: boolean;
  results?: readonly Row[];
  meta?: Readonly<{ changes?: number }>;
}>;

export interface D1PreparedStatementLike {
  bind(...values: D1Primitive[]): D1PreparedStatementLike;
  first<Row = Record<string, unknown>>(): Promise<Row | null>;
  all<Row = Record<string, unknown>>(): Promise<D1ResultLike<Row>>;
  run(): Promise<D1ResultLike>;
}

export interface D1DatabaseLike {
  prepare(query: string): D1PreparedStatementLike;
  batch<Row = Record<string, unknown>>(
    statements: readonly D1PreparedStatementLike[],
  ): Promise<readonly D1ResultLike<Row>[]>;
}
