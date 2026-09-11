import "server-only";

import type { D1DatabaseLike } from "../../db/repositories/d1.ts";
import {
  executeBaselineSign, executeDirectionBaselineRead, executeResidentCreate, resolveRequestContext,
} from "../authorization/request-context.ts";
import { isDocumentedSexCode } from "../domain/residents/resident.ts";
import { unavailableSessionProvider, type SessionIdentityProvider } from "../session/session-provider.ts";
import { AccessDeniedError, applicationResult } from "./errors.ts";

/** Composición interna por petición. No es una Server Action ni un endpoint público. */
export function createResidentBaselineService(
  database: D1DatabaseLike,
  session: SessionIdentityProvider = unavailableSessionProvider,
) {
  return Object.freeze({
    createResident(input: unknown) {
      return applicationResult(async () => {
        const fields = pickInput(input, ["profileScopeId", "centerId", "unitId", "displayName", "birthDate",
          "documentedSexCode", "internalReference", "buildingId", "floorId", "roomId", "placeId", "operationId"]);
        const selection = selectionFrom(fields);
        const unitId = stringField(fields, "unitId");
        const payload = Object.freeze({
          displayName: stringField(fields, "displayName"), birthDate: stringField(fields, "birthDate"),
          documentedSexCode: fields.documentedSexCode,
          operationId: stringField(fields, "operationId"),
          internalReference: optionalString(fields, "internalReference"),
          buildingId: optionalString(fields, "buildingId"), floorId: optionalString(fields, "floorId"),
          roomId: optionalString(fields, "roomId"), placeId: optionalString(fields, "placeId"),
        });
        const context = await resolveRequestContext({ database, session, selection, target: { kind: "CREATE", unitId } });
        if (!isDocumentedSexCode(payload.documentedSexCode)) throw new Error("APPLICATION_INPUT_INVALID");
        return executeResidentCreate(context, {
          ...payload, documentedSexCode: payload.documentedSexCode,
        });
      });
    },
    signBaseline(input: unknown) {
      return applicationResult(async () => {
        const fields = pickInput(input, ["profileScopeId", "centerId", "residentId", "draftId", "expectedDraftRevision", "operationId"]);
        const selection = selectionFrom(fields);
        const residentId = stringField(fields, "residentId");
        const draftId = stringField(fields, "draftId");
        const operationId = stringField(fields, "operationId");
        const revision = fields.expectedDraftRevision;
        if (typeof revision !== "number" || !Number.isSafeInteger(revision) || revision < 1) throw new Error("APPLICATION_INPUT_INVALID");
        const context = await resolveRequestContext({ database, session, selection, target: { kind: "SIGN", residentId, draftId } });
        return executeBaselineSign(context, { expectedDraftRevision: revision, operationId });
      });
    },
    readDirectionBaseline(input: unknown) {
      return applicationResult(async () => {
        const fields = pickInput(input, ["profileScopeId", "centerId", "residentId", "resourceType", "purpose"]);
        const selection = selectionFrom(fields);
        const residentId = stringField(fields, "residentId");
        const resourceType = fields.resourceType;
        const purpose = stringField(fields, "purpose");
        if (resourceType !== "BASELINE_CURRENT" && resourceType !== "BASELINE_HISTORY") throw new AccessDeniedError();
        const context = await resolveRequestContext({ database, session, selection, target: { kind: "READ", residentId }, resourceType, purpose });
        return executeDirectionBaselineRead(context);
      });
    },
  });
}

function pickInput(input: unknown, allowedKeys: readonly string[]): Readonly<Record<string, unknown>> {
  if (typeof input !== "object" || input === null || Array.isArray(input)) throw new Error("APPLICATION_INPUT_INVALID");
  if (Object.keys(input).some((key) => !allowedKeys.includes(key))) throw new AccessDeniedError();
  return Object.freeze({ ...input });
}

function stringField(fields: Readonly<Record<string, unknown>>, name: string): string {
  const value = fields[name];
  if (typeof value !== "string") throw new Error("APPLICATION_INPUT_INVALID");
  return value;
}

function optionalString(fields: Readonly<Record<string, unknown>>, name: string): string | undefined {
  return fields[name] === undefined ? undefined : stringField(fields, name);
}

function selectionFrom(fields: Readonly<Record<string, unknown>>) {
  return Object.freeze({ profileScopeId: stringField(fields, "profileScopeId"), centerId: stringField(fields, "centerId") });
}
