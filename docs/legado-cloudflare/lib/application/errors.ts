import "server-only";

export class AccessDeniedError extends Error {
  constructor() {
    super("ACCESS_DENIED");
  }
}

export type ApplicationFailure = Readonly<{
  ok: false;
  error: Readonly<{ code: "ACCESS_DENIED" | "INVALID_INPUT" | "CONFLICT" | "UNAVAILABLE"; message: string }>;
}>;

export type ApplicationResult<T> = Readonly<{ ok: true; value: T }> | ApplicationFailure;

/** Sin causas, SQL, identificadores ni distinción entre recurso ausente y ajeno. */
export function normalizeApplicationError(error: unknown): ApplicationFailure {
  const message = error instanceof Error ? error.message : "";
  let code: ApplicationFailure["error"]["code"] = "UNAVAILABLE";
  if (error instanceof AccessDeniedError || /(?:RESIDENT_CREATE|BASELINE_SIGN|CLINICAL_DETAIL_READ)_NOT_AUTHORIZED/u.test(message)) {
    code = "ACCESS_DENIED";
  } else if (/^(?:RESIDENT_CREATE_INPUT_INVALID|BASELINE_SIGN_INPUT_INVALID|CLINICAL_DETAIL_READ_INPUT_INVALID|APPLICATION_INPUT_INVALID|BASELINE_AREAS_INCOMPLETE|BASELINE_BARTHEL_INCOMPLETE)$/u.test(message)) {
    code = "INVALID_INPUT";
  } else if (/^(?:IDEMPOTENCY_KEY_REUSED(?:_WITH_DIFFERENT_REQUEST)?|IDEMPOTENCY_OPERATION_IN_PROGRESS)$/u.test(message)) {
    code = "CONFLICT";
  }
  const messages = {
    ACCESS_DENIED: "No se puede acceder a esta operación.",
    INVALID_INPUT: "Revisa los datos de la operación.",
    CONFLICT: "La operación entra en conflicto con una solicitud anterior.",
    UNAVAILABLE: "No se ha podido completar la operación.",
  };
  return Object.freeze({ ok: false, error: Object.freeze({ code, message: messages[code] }) });
}

export async function applicationResult<T>(run: () => Promise<T>): Promise<ApplicationResult<T>> {
  try {
    return Object.freeze({ ok: true, value: await run() });
  } catch (error) {
    return normalizeApplicationError(error);
  }
}
