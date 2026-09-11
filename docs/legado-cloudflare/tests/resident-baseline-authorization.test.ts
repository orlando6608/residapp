import assert from "node:assert/strict";
import test from "node:test";

import {
  authorizeResidentBaseline,
  type AuthorizationDecision,
  type AuthorizationSubject,
  type ResidentBaselineAuthorizationRequest,
  type ResidentBaselinePermission,
  type ResidentBaselineProfileScope,
} from "../lib/authorization/policy.ts";
import { SYSTEM_PROFILES, type SystemProfile } from "../lib/domain/access/profiles.ts";
import type {
  AccountId,
  CenterId,
  ResidentId,
  UnitId,
} from "../lib/domain/shared/identifiers.ts";

const accountId = "10000000-0000-4000-8000-000000000001" as AccountId;
const centerId = "20000000-0000-4000-8000-000000000001" as CenterId;
const otherCenterId = "20000000-0000-4000-8000-000000000002" as CenterId;
const unitId = "30000000-0000-4000-8000-000000000001" as UnitId;
const otherUnitId = "30000000-0000-4000-8000-000000000002" as UnitId;
const residentId = "40000000-0000-4000-8000-000000000001" as ResidentId;
const otherResidentId = "40000000-0000-4000-8000-000000000002" as ResidentId;

const residentResource = Object.freeze({ centerId, unitId, residentId });

function profileScope(
  profile: SystemProfile,
  overrides: Partial<ResidentBaselineProfileScope> = {},
): ResidentBaselineProfileScope {
  return Object.freeze({
    profile,
    centerId,
    unitId,
    residentIds: [residentId],
    activeFamilyAuthorizationResidentIds: [],
    permissions: [],
    ...overrides,
  });
}

function subject(
  profile: SystemProfile | null = "ENFERMERIA",
  options: Readonly<{
    permissions?: readonly ResidentBaselinePermission[];
    familyAuthorizationActive?: boolean;
  }> = {},
  overrides: Partial<AuthorizationSubject> = {},
): AuthorizationSubject {
  const scopedProfile = profile ?? "ENFERMERIA";

  return {
    accountId,
    authenticated: true,
    accountActive: true,
    activeProfile: profile,
    assignedProfiles: [scopedProfile],
    profileScopes: [
      profileScope(scopedProfile, {
        permissions: options.permissions ?? [],
        activeFamilyAuthorizationResidentIds: options.familyAuthorizationActive ? [residentId] : [],
      }),
    ],
    ...overrides,
  };
}

function decide(
  action: ResidentBaselineAuthorizationRequest["action"],
  authorizationSubject: AuthorizationSubject,
  purpose?: string,
): AuthorizationDecision {
  const request = {
    action,
    subject: authorizationSubject,
    resource: action === "RESIDENT_IDENTITY_CREATE" ? { centerId, unitId } : residentResource,
    ...(purpose ? { purpose } : {}),
  } as ResidentBaselineAuthorizationRequest;

  return authorizeResidentBaseline(request);
}

function assertAllowed(decision: AuthorizationDecision): void {
  assert.equal(decision.allowed, true);
}

function assertDenied(decision: AuthorizationDecision, reason: string): void {
  assert.deepEqual(decision, { allowed: false, reason });
}

for (const profile of SYSTEM_PROFILES) {
  test(`[positivo] ${profile} lee la identidad mínima dentro de su ámbito`, () => {
    assertAllowed(
      decide(
        "RESIDENT_IDENTITY_READ",
        subject(profile, { familyAuthorizationActive: profile === "FAMILIAR" }),
      ),
    );
  });
}

for (const profile of ["AUXILIAR", "ENFERMERIA", "MEDICINA"] as const) {
  test(`[positivo] ${profile} lee el basal vigente dentro de su ámbito`, () => {
    assertAllowed(decide("BASELINE_CURRENT_READ", subject(profile)));
  });
}

for (const profile of ["ENFERMERIA", "MEDICINA"] as const) {
  test(`[positivo] ${profile} lee el historial basal dentro de su ámbito`, () => {
    assertAllowed(decide("BASELINE_HISTORY_READ", subject(profile)));
  });

  for (const action of ["BASELINE_INITIAL_COMPLETE", "BASELINE_REEVALUATE"] as const) {
    test(`[positivo] ${profile} ejecuta ${action} con permiso expreso del ámbito`, () => {
      assertAllowed(decide(action, subject(profile, { permissions: [action] })));
    });
  }
}

test("[positivo] Administración crea la identidad administrativa", () => {
  assertAllowed(decide("RESIDENT_IDENTITY_CREATE", subject("ADMINISTRACION")));
});

test("[positivo] Enfermería crea la identidad con permiso expreso", () => {
  assertAllowed(
    decide(
      "RESIDENT_IDENTITY_CREATE",
      subject("ENFERMERIA", { permissions: ["RESIDENT_IDENTITY_CREATE"] }),
    ),
  );
});

test("[positivo] Administración modifica la identidad administrativa", () => {
  assertAllowed(decide("RESIDENT_IDENTITY_UPDATE", subject("ADMINISTRACION")));
});

for (const action of ["BASELINE_CURRENT_READ", "BASELINE_HISTORY_READ"] as const) {
  test(`[positivo] Dirección consulta ${action} con finalidad y obligación de auditoría`, () => {
    const decision = decide(
      action,
      subject("DIRECCION_CLINICA", { permissions: ["CLINICAL_DETAIL_READ"] }),
      "SUPERVISION_CLINICA",
    );

    assert.deepEqual(decision, {
      allowed: true,
      obligations: [
        {
          type: "AUDIT_CLINICAL_DETAIL_ACCESS",
          resourceType:
            action === "BASELINE_CURRENT_READ" ? "BASELINE_CURRENT" : "BASELINE_HISTORY",
          accountId,
          activeProfile: "DIRECCION_CLINICA",
          centerId,
          unitId,
          residentId,
          purpose: "SUPERVISION_CLINICA",
        },
      ],
    });
  });
}

const roleDenials = [
  {
    action: "RESIDENT_IDENTITY_CREATE",
    profiles: ["AUXILIAR", "MEDICINA", "FAMILIAR", "DIRECCION_CLINICA"],
  },
  {
    action: "RESIDENT_IDENTITY_UPDATE",
    profiles: ["AUXILIAR", "ENFERMERIA", "MEDICINA", "FAMILIAR", "DIRECCION_CLINICA"],
  },
  {
    action: "BASELINE_CURRENT_READ",
    profiles: ["FAMILIAR", "ADMINISTRACION"],
  },
  {
    action: "BASELINE_HISTORY_READ",
    profiles: ["AUXILIAR", "FAMILIAR", "ADMINISTRACION"],
  },
  {
    action: "BASELINE_INITIAL_COMPLETE",
    profiles: ["AUXILIAR", "FAMILIAR", "ADMINISTRACION", "DIRECCION_CLINICA"],
  },
  {
    action: "BASELINE_REEVALUATE",
    profiles: ["AUXILIAR", "FAMILIAR", "ADMINISTRACION", "DIRECCION_CLINICA"],
  },
] as const satisfies readonly {
  action: ResidentBaselineAuthorizationRequest["action"];
  profiles: readonly SystemProfile[];
}[];

for (const row of roleDenials) {
  for (const profile of row.profiles) {
    test(`[negativo] ${profile} no puede ejecutar ${row.action}`, () => {
      assertDenied(decide(row.action, subject(profile)), "ACTION_NOT_ALLOWED");
    });
  }
}

for (const profile of ["ENFERMERIA", "MEDICINA"] as const) {
  for (const action of ["BASELINE_INITIAL_COMPLETE", "BASELINE_REEVALUATE"] as const) {
    test(`[negativo] ${profile} no ejecuta ${action} sin permiso expreso`, () => {
      assertDenied(decide(action, subject(profile)), "PERMISSION_REQUIRED");
    });
  }
}

test("[negativo] Enfermería no crea identidad sin permiso expreso", () => {
  assertDenied(
    decide("RESIDENT_IDENTITY_CREATE", subject("ENFERMERIA")),
    "PERMISSION_REQUIRED",
  );
});

test("[negativo] familiar sin autorización activa no lee la identidad", () => {
  assertDenied(
    decide("RESIDENT_IDENTITY_READ", subject("FAMILIAR")),
    "FAMILY_AUTHORIZATION_REQUIRED",
  );
});

for (const action of ["BASELINE_CURRENT_READ", "BASELINE_HISTORY_READ"] as const) {
  test(`[negativo] Dirección no consulta ${action} sin permiso clínico`, () => {
    assertDenied(decide(action, subject("DIRECCION_CLINICA")), "PERMISSION_REQUIRED");
  });

  test(`[negativo] Dirección no consulta ${action} sin finalidad explícita`, () => {
    assertDenied(
      decide(
        action,
        subject("DIRECCION_CLINICA", { permissions: ["CLINICAL_DETAIL_READ"] }),
      ),
      "ACCESS_PURPOSE_REQUIRED",
    );
  });
}

const contextDenials = [
  ["sin autenticación", { authenticated: false }, "NOT_AUTHENTICATED"],
  ["sin cuenta identificada", { accountId: null }, "ACCOUNT_ID_REQUIRED"],
  ["con cuenta inactiva", { accountActive: false }, "ACCOUNT_INACTIVE"],
  ["sin perfil activo", { activeProfile: null }, "ACTIVE_PROFILE_REQUIRED"],
  ["con perfil no asignado", { activeProfile: "MEDICINA" }, "PROFILE_NOT_ASSIGNED"],
] as const;

for (const [label, overrides, reason] of contextDenials) {
  test(`[negativo] deniega ${label} antes de evaluar la acción`, () => {
    assertDenied(
      decide("BASELINE_CURRENT_READ", subject("ENFERMERIA", {}, overrides)),
      reason,
    );
  });
}

test("[negativo] cambiar el centro no amplía el acceso", () => {
  assertDenied(
    authorizeResidentBaseline({
      action: "BASELINE_CURRENT_READ",
      subject: subject(),
      resource: { ...residentResource, centerId: otherCenterId },
    }),
    "CENTER_OUT_OF_SCOPE",
  );
});

test("[negativo] cambiar la unidad no amplía el acceso", () => {
  assertDenied(
    authorizeResidentBaseline({
      action: "BASELINE_CURRENT_READ",
      subject: subject(),
      resource: { ...residentResource, unitId: otherUnitId },
    }),
    "UNIT_OUT_OF_SCOPE",
  );
});

test("[negativo] cambiar el residente no amplía el acceso", () => {
  assertDenied(
    authorizeResidentBaseline({
      action: "BASELINE_CURRENT_READ",
      subject: subject(),
      resource: { ...residentResource, residentId: otherResidentId },
    }),
    "RESIDENT_OUT_OF_SCOPE",
  );
});

test("[negativo] no combina centro y unidad de grants distintos", () => {
  const multiCenter = subject("ENFERMERIA", {}, {
    profileScopes: [
      profileScope("ENFERMERIA"),
      profileScope("ENFERMERIA", {
        centerId: otherCenterId,
        unitId: otherUnitId,
        residentIds: [otherResidentId],
      }),
    ],
  });

  assertDenied(
    authorizeResidentBaseline({
      action: "BASELINE_CURRENT_READ",
      subject: multiCenter,
      resource: { centerId, unitId: otherUnitId, residentId: otherResidentId },
    }),
    "UNIT_OUT_OF_SCOPE",
  );
});

test("[negativo] no reutiliza en el perfil activo permisos de otro perfil", () => {
  const multiRole = subject("ENFERMERIA", {}, {
    assignedProfiles: ["ENFERMERIA", "MEDICINA"],
    profileScopes: [
      profileScope("ENFERMERIA"),
      profileScope("MEDICINA", { permissions: ["BASELINE_REEVALUATE"] }),
    ],
  });

  assertDenied(decide("BASELINE_REEVALUATE", multiRole), "PERMISSION_REQUIRED");
});

test("[negativo] no reutiliza en un centro el permiso concedido en otro", () => {
  const multiCenter = subject("ENFERMERIA", {}, {
    profileScopes: [
      profileScope("ENFERMERIA"),
      profileScope("ENFERMERIA", {
        centerId: otherCenterId,
        unitId: otherUnitId,
        residentIds: [otherResidentId],
        permissions: ["BASELINE_REEVALUATE"],
      }),
    ],
  });

  assertDenied(decide("BASELINE_REEVALUATE", multiCenter), "PERMISSION_REQUIRED");
});

test("[negativo] no reutiliza para un residente el permiso ligado a otro", () => {
  const sameUnit = subject("ENFERMERIA", {}, {
    profileScopes: [
      profileScope("ENFERMERIA"),
      profileScope("ENFERMERIA", {
        residentIds: [otherResidentId],
        permissions: ["BASELINE_REEVALUATE"],
      }),
    ],
  });

  assertDenied(decide("BASELINE_REEVALUATE", sameUnit), "PERMISSION_REQUIRED");
});

test("[negativo] no reutiliza el centro de un perfil para actuar con otro", () => {
  const multiRole = subject("ADMINISTRACION", {}, {
    assignedProfiles: ["ADMINISTRACION", "ENFERMERIA"],
    profileScopes: [
      profileScope("ADMINISTRACION", {
        centerId: otherCenterId,
        unitId: otherUnitId,
        residentIds: [otherResidentId],
      }),
      profileScope("ENFERMERIA"),
    ],
  });

  assertDenied(
    decide("RESIDENT_IDENTITY_CREATE", multiRole),
    "CENTER_OUT_OF_SCOPE",
  );
});

test("[negativo] una acción runtime desconocida cae en denegación por defecto", () => {
  assertDenied(
    authorizeResidentBaseline({
      action: "UNKNOWN_ACTION",
      subject: subject(),
      resource: residentResource,
    }),
    "DENY_BY_DEFAULT",
  );
});

test("[negativo] un perfil runtime desconocido se rechaza explícitamente", () => {
  assertDenied(
    authorizeResidentBaseline({
      action: "RESIDENT_IDENTITY_READ",
      subject: { ...subject(), activeProfile: "INJECTED_PROFILE" },
      resource: residentResource,
    }),
    "ACTIVE_PROFILE_INVALID",
  );
});

test("[negativo] Dirección rechaza una finalidad runtime fuera del catálogo", () => {
  assertDenied(
    decide(
      "BASELINE_CURRENT_READ",
      subject("DIRECCION_CLINICA", { permissions: ["CLINICAL_DETAIL_READ"] }),
      "FINALIDAD_ENVIADA_POR_CLIENTE",
    ),
    "ACCESS_PURPOSE_REQUIRED",
  );
});

test("[negativo] un identificador no opaco se rechaza en runtime", () => {
  assertDenied(
    authorizeResidentBaseline({
      action: "BASELINE_CURRENT_READ",
      subject: subject(),
      resource: { ...residentResource, residentId: "resident-1" },
    }),
    "DENY_BY_DEFAULT",
  );
});

test("[invariante] la obligación de Dirección y su payload quedan congelados", () => {
  const decision = decide(
    "BASELINE_CURRENT_READ",
    subject("DIRECCION_CLINICA", { permissions: ["CLINICAL_DETAIL_READ"] }),
    "SUPERVISION_CLINICA",
  );

  assert.equal(decision.allowed, true);
  if (decision.allowed) {
    assert.equal(Object.isFrozen(decision), true);
    assert.equal(Object.isFrozen(decision.obligations), true);
    assert.equal(Object.isFrozen(decision.obligations[0]), true);
  }
});
