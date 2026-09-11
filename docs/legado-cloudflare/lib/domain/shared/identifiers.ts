declare const entityIdBrand: unique symbol;

const UUID_V4_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu;

export type EntityId<Entity extends string> = string & {
  readonly [entityIdBrand]: Entity;
};

export type AccountId = EntityId<"Account">;
export type BaselineVersionId = EntityId<"BaselineVersion">;
export type CenterId = EntityId<"Center">;
export type ResidentId = EntityId<"Resident">;
export type UnitId = EntityId<"Unit">;

/**
 * Genera identificadores internos opacos y no secuenciales sin depender de una librería.
 * Conocer el identificador nunca sustituye una comprobación de autorización.
 */
export function createOpaqueEntityId<Entity extends string>(): EntityId<Entity> {
  return crypto.randomUUID() as EntityId<Entity>;
}

/**
 * Valida el formato técnico en límites runtime. La marca TypeScript por sí sola no valida datos externos.
 */
export function isOpaqueEntityId(value: unknown): value is EntityId<string> {
  return typeof value === "string" && UUID_V4_PATTERN.test(value);
}
