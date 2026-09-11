import "server-only";

import type { SessionIdentityProvider } from "./session-provider.ts";

/** Solo configuración del proceso local; ni cabeceras, ni cookies, ni parámetros de petición. */
export function createSyntheticSessionProvider(externalSubject: string): SessionIdentityProvider {
  return Object.freeze({
    async getVerifiedIdentity() {
      const enabled = process.env.CONNECT_SYNTHETIC_SESSION_ENABLED === "1";
      const permittedEnvironment = process.env.NODE_ENV === "test" ||
        (process.env.NODE_ENV === "development" && process.env.CONNECT_LOCAL_DEVELOPMENT === "1");
      if (!enabled || !permittedEnvironment) {
        throw new Error("SYNTHETIC_SESSION_DISABLED");
      }
      if (!externalSubject.startsWith("synthetic-") || externalSubject.trim() !== externalSubject) {
        throw new Error("SYNTHETIC_SUBJECT_INVALID");
      }
      return Object.freeze({ externalSubject });
    },
  });
}
