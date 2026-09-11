import "server-only";

/** El adaptador debe verificar autenticidad, caducidad y revocación antes de devolver el sujeto. */
export type SessionIdentity = Readonly<{ externalSubject: string }>;

/** Puerto server-side, ligado a una petición. Nunca recibe claims de autorización del cliente. */
export interface SessionIdentityProvider {
  getVerifiedIdentity(): Promise<SessionIdentity | null>;
}

/** Composición por defecto hasta integrar un proveedor de sesión verificada. */
export const unavailableSessionProvider: SessionIdentityProvider = Object.freeze({
  async getVerifiedIdentity() {
    return null;
  },
});
