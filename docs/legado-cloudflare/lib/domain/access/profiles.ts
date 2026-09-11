export const SYSTEM_PROFILES = [
  "AUXILIAR",
  "ENFERMERIA",
  "MEDICINA",
  "FAMILIAR",
  "ADMINISTRACION",
  "DIRECCION_CLINICA",
] as const;

export type SystemProfile = (typeof SYSTEM_PROFILES)[number];
export type ClinicalProfessionalProfile = Extract<SystemProfile, "ENFERMERIA" | "MEDICINA">;
