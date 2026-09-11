import type { CenterId, ResidentId, UnitId } from "../shared/identifiers.ts";

export type ResidentAdministrativeLocation = Readonly<{
  building?: string;
  floor?: string;
  room?: string;
  place?: string;
}>;

export const DOCUMENTED_SEX_CODES = ["male", "female", "other", "unknown"] as const;

export type DocumentedSexCode = (typeof DOCUMENTED_SEX_CODES)[number];

export const RESIDENT_STATUSES = ["ACTIVO", "INACTIVO"] as const;

export type ResidentStatus = (typeof RESIDENT_STATUSES)[number];

/**
 * Identidad administrativa mínima exigida por RES-01.
 * El sexo procede de documentación administrativa y nunca se infiere ni admite texto libre.
 */
export type ResidentAdministrativeIdentity = Readonly<{
  id: ResidentId;
  centerId: CenterId;
  unitId: UnitId;
  name: string;
  birthDate: string;
  documentedSex: DocumentedSexCode;
  status: ResidentStatus;
  location: ResidentAdministrativeLocation;
}>;

export function isDocumentedSexCode(value: unknown): value is DocumentedSexCode {
  return (
    typeof value === "string" &&
    (DOCUMENTED_SEX_CODES as readonly string[]).includes(value)
  );
}

export type ResidentScope = Readonly<{
  centerId: CenterId;
  unitId: UnitId;
  residentId: ResidentId;
}>;
