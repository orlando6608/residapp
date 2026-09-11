import "server-only";

import { isOpaqueEntityId } from "../../lib/domain/shared/identifiers.ts";
import type { D1DatabaseLike } from "./d1.ts";

export type ClinicalDirectionReadInput = Readonly<{
  accountId: string;
  centerId: string;
  unitId: string;
  residentId: string;
  resourceType: "BASELINE_CURRENT" | "BASELINE_HISTORY";
  purposeCode: "SUPERVISION_CLINICA";
}>;

export type AuditedBaselineHeader = Readonly<{
  id: string;
  version_number: number;
  reason_code: string;
  signed_at: string;
}>;

/**
 * Único gateway de lectura basal para Dirección. El INSERT condicionado vuelve a
 * resolver cuenta, perfil, permiso, unidad, residente y ubicación dentro del mismo
 * D1Database.batch() que la lectura. La segunda sentencia solo puede ver versiones
 * para las que ese batch acaba de crear su auditoría.
 */
export async function readBaselineAsClinicalDirection(
  database: D1DatabaseLike,
  input: ClinicalDirectionReadInput,
): Promise<readonly AuditedBaselineHeader[]> {
  assertInput(input);
  const accessId = crypto.randomUUID();
  const occurredAt = new Date().toISOString();
  const resourceSql =
    input.resourceType === "BASELINE_CURRENT"
      ? `select version.id
           from resident_current_baselines current
           join baseline_versions version on version.id = current.baseline_version_id
          where current.center_id = ? and current.resident_id = ?`
      : `select version.id
           from baseline_versions version
          where version.center_id = ? and version.resident_id = ?`;

  const auditStatement = database
    .prepare(
      `insert into audit_events
        (id, account_id, active_profile, center_id, unit_id, resident_id,
         resource_type, resource_id, action_code, purpose_code, occurred_at)
       select ? || ':' || resource.id, ?, 'DIRECCION_CLINICA', ?, ?, ?, ?, resource.id,
              'CLINICAL_DETAIL_READ', 'SUPERVISION_CLINICA', ?
         from (${resourceSql}) resource
        where exists (
          select 1
            from accounts account
            join profile_scopes profile
              on profile.account_id = account.id
             and profile.center_id = ?
             and profile.profile_code = 'DIRECCION_CLINICA'
             and profile.status = 'ACTIVE'
            join profile_unit_scopes unit_scope
              on unit_scope.profile_scope_id = profile.id
             and unit_scope.center_id = profile.center_id
             and unit_scope.unit_id = ?
             and unit_scope.revoked_at is null
            join profile_permissions permission
              on permission.profile_scope_id = profile.id
             and permission.center_id = profile.center_id
             and permission.permission_code = 'CLINICAL_DETAIL_READ'
             and permission.revoked_at is null
            join residents resident
              on resident.id = ? and resident.center_id = profile.center_id
             and resident.status = 'ACTIVE'
            join resident_location_intervals location
              on location.resident_id = resident.id
             and location.center_id = resident.center_id
             and location.unit_id = unit_scope.unit_id
             and location.valid_until is null
           where account.id = ? and account.status = 'ACTIVE'
        )`,
    )
    .bind(
      accessId,
      input.accountId,
      input.centerId,
      input.unitId,
      input.residentId,
      input.resourceType,
      occurredAt,
      input.centerId,
      input.residentId,
      input.centerId,
      input.unitId,
      input.residentId,
      input.accountId,
    );

  const readSql =
    input.resourceType === "BASELINE_CURRENT"
      ? `select version.id, version.version_number, version.reason_code, version.signed_at
           from resident_current_baselines current
           join baseline_versions version on version.id = current.baseline_version_id
           join audit_events audit on audit.id = ? || ':' || version.id
          where current.center_id = ? and current.resident_id = ?`
      : `select version.id, version.version_number, version.reason_code, version.signed_at
           from baseline_versions version
           join audit_events audit on audit.id = ? || ':' || version.id
          where version.center_id = ? and version.resident_id = ?
          order by version.version_number desc`;
  const results = await database.batch<AuditedBaselineHeader>([
    auditStatement,
    database.prepare(readSql).bind(accessId, input.centerId, input.residentId),
  ]);
  const auditedCount = results[0]?.meta?.changes ?? 0;
  if (auditedCount < 1) {
    throw new Error("CLINICAL_DETAIL_READ_NOT_AUTHORIZED");
  }
  return results[1]?.results ?? [];
}

function assertInput(input: ClinicalDirectionReadInput): void {
  if (
    !isOpaqueEntityId(input.accountId) ||
    !isOpaqueEntityId(input.centerId) ||
    !isOpaqueEntityId(input.unitId) ||
    !isOpaqueEntityId(input.residentId) ||
    input.purposeCode !== "SUPERVISION_CLINICA"
  ) {
    throw new Error("CLINICAL_DETAIL_READ_INPUT_INVALID");
  }
}
