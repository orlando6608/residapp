import { BASELINE_AREAS, type BaselineArea } from "./baseline.ts";

type UnknownRecord = Record<string, unknown>;

const INFORMATION_SOURCES = [
  "VALORACION_DIRECTA",
  "HISTORIA_O_INFORME_CLINICO",
  "PERSONAL_DEL_CENTRO",
  "FAMILIAR_O_CUIDADOR",
  "FUENTES_COMBINADAS",
  "OTRA",
  "NO_DOCUMENTADO",
] as const;

const MOBILITY_DISPLACEMENT = [
  "DEAMBULA_INDEPENDIENTE_SIN_AYUDA",
  "DEAMBULA_CON_AYUDA_TECNICA",
  "DEAMBULA_CON_SUPERVISION",
  "DEAMBULA_CON_AYUDA_FISICA_1_PERSONA",
  "DEAMBULA_CON_AYUDA_FISICA_2_PERSONAS",
  "SILLA_RUEDAS_AUTOPROPULSADA",
  "SILLA_RUEDAS_IMPULSADA_POR_OTRA_PERSONA",
  "SIN_DESPLAZAMIENTO_FUNCIONAL",
  "NO_DOCUMENTADO",
] as const;
const MOBILITY_AIDS = [
  "NINGUNA",
  "BASTON",
  "MULETA_O_MULETAS",
  "ANDADOR_4_RUEDAS",
  "ANDADOR_2_RUEDAS",
  "ANDADOR_FIJO_SIN_RUEDAS",
  "OTRA",
  "NO_DOCUMENTADO",
] as const;
const MOBILITY_TRANSFERS = [
  "INDEPENDIENTE",
  "SUPERVISION",
  "AYUDA_1_PERSONA",
  "AYUDA_2_PERSONAS",
  "GRUA",
  "NO_DOCUMENTADO",
] as const;

const FEEDING_ROUTES = ["ORAL", "ENTERAL", "MIXTA", "NO_DOCUMENTADO"] as const;
const FOOD_TEXTURES = [
  "NORMAL",
  "TROCEADA",
  "TRITURADA",
  "PURE",
  "OTRA_TEXTURA_ADAPTADA",
  "NO_APLICA",
  "NO_DOCUMENTADO",
] as const;
const LIQUID_CONSISTENCIES = [
  "IDDSI_0_FINO_SIN_ESPESAR",
  "IDDSI_1_LIGERAMENTE_ESPESO",
  "IDDSI_2_POCO_ESPESO",
  "IDDSI_3_MODERADAMENTE_ESPESO",
  "IDDSI_4_EXTREMADAMENTE_ESPESO",
  "NO_APLICA",
  "NO_DOCUMENTADO",
] as const;
const FEEDING_ASSISTANCE = [
  "INDEPENDIENTE",
  "PREPARAR_O_CORTAR_ALIMENTOS",
  "SUPERVISION_O_INDICACIONES",
  "AYUDA_FISICA_PARCIAL",
  "AYUDA_TOTAL",
  "NO_DOCUMENTADO",
] as const;
const SWALLOWING_PRECAUTIONS = [
  "NINGUNA_DOCUMENTADA",
  "PRECAUCIONES_DOCUMENTADAS",
  "NO_DOCUMENTADO",
] as const;

const CONTINENCE_VALUES = [
  "CONTINENTE",
  "INCONTINENCIA_OCASIONAL",
  "INCONTINENCIA_HABITUAL",
  "NO_DOCUMENTADO",
] as const;
const CONTINENCE_MANAGEMENT = [
  "NINGUNO",
  "ABSORBENTE",
  "SONDA_URINARIA",
  "UROSTOMIA",
  "COLOSTOMIA_ILEOSTOMIA",
  "OTRO",
  "NO_DOCUMENTADO",
] as const;

const PERSONAL_CARE = [
  "INDEPENDIENTE",
  "SUPERVISION_O_INDICACIONES",
  "AYUDA_PARCIAL",
  "AYUDA_TOTAL",
  "NO_DOCUMENTADO",
] as const;
const BATHING = [
  "INDEPENDIENTE",
  "SUPERVISION",
  "AYUDA_PARCIAL",
  "AYUDA_TOTAL",
  "NO_DOCUMENTADO",
] as const;

const COGNITION = [
  "SIN_DETERIORO_CONOCIDO_O_DOCUMENTADO",
  "DETERIORO_COGNITIVO_LEVE_DOCUMENTADO",
  "DEMENCIA_DOCUMENTADA",
  "SITUACION_NO_DETERMINADA",
] as const;
const ETIOLOGIES = [
  "ENFERMEDAD_ALZHEIMER",
  "DEMENCIA_VASCULAR",
  "DEMENCIA_MIXTA",
  "DEMENCIA_CON_CUERPOS_DE_LEWY",
  "DEMENCIA_FRONTOTEMPORAL",
  "DEMENCIA_ASOCIADA_ENFERMEDAD_PARKINSON",
  "SINDROME_CORTICOBASAL",
  "OTRA",
  "ETIOLOGIA_NO_ESPECIFICADA",
] as const;
const GDS = ["NO_DOCUMENTADO", "GDS_1", "GDS_2", "GDS_3", "GDS_4", "GDS_5", "GDS_6", "GDS_7"] as const;

const COMPREHENSION = [
  "COMPRENSION_FUNCIONAL",
  "NECESITA_FRASES_SENCILLAS_REPETICION_O_APOYO",
  "COMPRENSION_MUY_LIMITADA",
  "NO_SE_HA_PODIDO_DETERMINAR",
  "NO_DOCUMENTADO",
] as const;
const EXPRESSION = [
  "EXPRESA_NECESIDADES_EFICAZMENTE",
  "EXPRESION_VERBAL_LIMITADA_PERO_COMUNICA_NECESIDADES_BASICAS",
  "COMUNICACION_PRINCIPALMENTE_NO_VERBAL",
  "NO_EXPRESA_NECESIDADES_DE_FORMA_FIABLE",
  "NO_DOCUMENTADO",
] as const;
const COMMUNICATION_FORMS = [
  "LENGUAJE_ORAL",
  "GESTOS",
  "ESCRITURA",
  "TABLERO_O_DISPOSITIVO",
  "OTRA",
  "NO_SE_IDENTIFICA_FORMA_EFECTIVA",
  "NO_DOCUMENTADO",
] as const;

const BEHAVIOR_STATUS = [
  "SIN_CONDUCTAS_RELEVANTES_CONOCIDAS",
  "PATRONES_CONDUCTUALES_HABITUALES",
  "NO_DOCUMENTADO",
] as const;
const BEHAVIOR_PATTERNS = [
  "APATIA_O_RETRAIMIENTO",
  "ANIMO_BAJO_HABITUAL",
  "ANSIEDAD_O_TEMOR",
  "IRRITABILIDAD",
  "AGITACION_O_INQUIETUD",
  "RESISTENCIA_A_LOS_CUIDADOS",
  "CONDUCTAS_O_VOCALIZACIONES_REPETITIVAS",
  "DEAMBULACION_ERRATICA_O_INTENTO_DE_SALIDA",
  "AGRESIVIDAD_VERBAL",
  "AGRESIVIDAD_FISICA",
  "DESINHIBICION",
  "IDEAS_DELIRANTES_O_ALUCINACIONES_DOCUMENTADAS",
  "OTRA",
] as const;

const SLEEP_PATTERNS = [
  "PATRON_HABITUALMENTE_CONSERVADO",
  "DIFICULTAD_INICIO_SUENO",
  "DESPERTARES_FRECUENTES",
  "DESPERTAR_PRECOZ",
  "INVERSION_SUENO_VIGILIA",
  "SOMNOLENCIA_DIURNA_HABITUAL",
  "PATRON_IRREGULAR_VARIABLE",
  "NO_DOCUMENTADO",
] as const;
const USUAL_AIDS = [
  "GAFAS",
  "AUDIFONO",
  "TABLERO_O_DISPOSITIVO_COMUNICACION",
  "PROTESIS_DENTAL",
  "CUBIERTOS_O_VAJILLA_ADAPTADOS",
  "OTRO_PRODUCTO_DE_APOYO",
  "OXIGENOTERAPIA_HABITUAL",
  "CPAP_BIPAP",
  "OTRO",
  "NINGUNO",
  "NO_DOCUMENTADO",
] as const;

export function assertCompleteBaselineAreaAnswer(area: BaselineArea, value: unknown): void {
  if (!isRecord(value)) {
    throw new Error("BASELINE_AREA_PAYLOAD_INVALID");
  }

  switch (area) {
    case "MOVILIDAD":
      assertChoice(value.displacementModeCode, MOBILITY_DISPLACEMENT);
      assertChoice(value.technicalAidCode, MOBILITY_AIDS);
      assertOpenText(value.technicalAidCode, "OTRA", value.technicalAidOtherText);
      assertChoice(value.transferCode, MOBILITY_TRANSFERS);
      return;
    case "ALIMENTACION":
      assertChoice(value.routeCode, FEEDING_ROUTES);
      assertChoice(value.foodTextureCode, FOOD_TEXTURES);
      assertChoice(value.liquidConsistencyCode, LIQUID_CONSISTENCIES);
      if (
        (value.foodTextureCode === "NO_APLICA" || value.liquidConsistencyCode === "NO_APLICA") &&
        value.routeCode !== "ENTERAL"
      ) {
        throw new Error("BASELINE_FEEDING_NOT_APPLICABLE_INVALID");
      }
      assertOpenText(
        value.foodTextureCode,
        "OTRA_TEXTURA_ADAPTADA",
        value.foodTextureOtherText,
      );
      assertChoice(value.assistanceCode, FEEDING_ASSISTANCE);
      assertChoice(value.swallowingPrecautionsCode, SWALLOWING_PRECAUTIONS);
      assertOpenText(
        value.swallowingPrecautionsCode,
        "PRECAUCIONES_DOCUMENTADAS",
        value.swallowingPrecautionsText,
      );
      return;
    case "CONTINENCIA":
      assertChoice(value.urinationCode, CONTINENCE_VALUES);
      assertChoice(value.bowelCode, CONTINENCE_VALUES);
      assertMultiChoice(value.managementCodes, CONTINENCE_MANAGEMENT);
      assertExclusive(value.managementCodes, ["NINGUNO", "NO_DOCUMENTADO"]);
      assertArrayOpenText(value.managementCodes, "OTRO", value.managementOtherText);
      return;
    case "ASEO_HIGIENE":
      assertChoice(value.personalCareAssistanceCode, PERSONAL_CARE);
      assertChoice(value.bathingAssistanceCode, BATHING);
      return;
    case "COGNICION": {
      assertChoice(value.categoryCode, COGNITION);
      assertOptionalChoice(value.etiologyCode, ETIOLOGIES);
      assertOptionalChoice(value.gdsCode, GDS);
      if (value.etiologyCode !== undefined && value.categoryCode !== "DEMENCIA_DOCUMENTADA") {
        throw new Error("BASELINE_COGNITION_ETIOLOGY_INVALID");
      }
      assertOpenText(value.etiologyCode, "OTRA", value.etiologyOtherText);
      const hasClinicalReference =
        value.etiologyCode !== undefined ||
        (typeof value.gdsCode === "string" && value.gdsCode !== "NO_DOCUMENTADO");
      if (hasClinicalReference) {
        assertChoice(value.clinicalReferenceSourceCode, INFORMATION_SOURCES);
        if (value.clinicalReferenceSourceCode === "NO_DOCUMENTADO") {
          throw new Error("BASELINE_COGNITION_SOURCE_INVALID");
        }
        assertNonEmptyText(value.clinicalReferenceDate);
      } else if (
        value.clinicalReferenceSourceCode !== undefined ||
        hasText(value.clinicalReferenceDate)
      ) {
        throw new Error("BASELINE_COGNITION_REFERENCE_WITHOUT_DATA");
      }
      assertOpenText(
        value.clinicalReferenceSourceCode,
        "OTRA",
        value.clinicalReferenceSourceOtherText,
      );
      return;
    }
    case "COMUNICACION":
      assertChoice(value.comprehensionCode, COMPREHENSION);
      assertChoice(value.expressionCode, EXPRESSION);
      assertMultiChoice(value.usualFormsCodes, COMMUNICATION_FORMS);
      assertExclusive(value.usualFormsCodes, [
        "NO_SE_IDENTIFICA_FORMA_EFECTIVA",
        "NO_DOCUMENTADO",
      ]);
      assertArrayOpenText(value.usualFormsCodes, "OTRA", value.usualFormOtherText);
      return;
    case "CONDUCTA":
      assertChoice(value.statusCode, BEHAVIOR_STATUS);
      if (value.statusCode === "PATRONES_CONDUCTUALES_HABITUALES") {
        assertMultiChoice(value.patternCodes, BEHAVIOR_PATTERNS);
      } else if (!Array.isArray(value.patternCodes) || value.patternCodes.length !== 0) {
        throw new Error("BASELINE_BEHAVIOR_PATTERNS_INVALID");
      }
      assertArrayOpenText(value.patternCodes, "OTRA", value.patternOtherText);
      return;
    case "SUENO":
      assertMultiChoice(value.patternCodes, SLEEP_PATTERNS);
      assertExclusive(value.patternCodes, [
        "PATRON_HABITUALMENTE_CONSERVADO",
        "NO_DOCUMENTADO",
      ]);
      return;
    case "AYUDAS_HABITUALES":
      assertMultiChoice(value.aidCodes, USUAL_AIDS);
      assertExclusive(value.aidCodes, ["NINGUNO", "NO_DOCUMENTADO"]);
      assertArrayOpenText(
        value.aidCodes,
        "OTRO_PRODUCTO_DE_APOYO",
        value.otherSupportProductText,
      );
      assertArrayOpenText(value.aidCodes, "OTRO", value.otherSupportText);
      return;
  }
}

export function assertAllBaselineAreasComplete(
  entries: readonly Readonly<{ areaCode: string; answerPayload: unknown }>[],
): void {
  if (
    entries.length !== BASELINE_AREAS.length ||
    new Set(entries.map((entry) => entry.areaCode)).size !== BASELINE_AREAS.length
  ) {
    throw new Error("BASELINE_AREAS_INCOMPLETE");
  }

  for (const area of BASELINE_AREAS) {
    const entry = entries.find((candidate) => candidate.areaCode === area);
    if (!entry) {
      throw new Error("BASELINE_AREAS_INCOMPLETE");
    }
    assertCompleteBaselineAreaAnswer(area, entry.answerPayload);
  }
}

function assertChoice<const T extends readonly string[]>(value: unknown, catalog: T): void {
  if (typeof value !== "string" || !(catalog as readonly string[]).includes(value)) {
    throw new Error("BASELINE_CATALOG_VALUE_INVALID");
  }
}

function assertOptionalChoice<const T extends readonly string[]>(
  value: unknown,
  catalog: T,
): void {
  if (value !== undefined && value !== null) {
    assertChoice(value, catalog);
  }
}

function assertMultiChoice<const T extends readonly string[]>(value: unknown, catalog: T): void {
  if (
    !Array.isArray(value) ||
    value.length === 0 ||
    new Set(value).size !== value.length ||
    !value.every((item) => typeof item === "string" && (catalog as readonly string[]).includes(item))
  ) {
    throw new Error("BASELINE_MULTI_VALUE_INVALID");
  }
}

function assertExclusive(value: unknown, exclusiveCodes: readonly string[]): void {
  if (!Array.isArray(value)) {
    throw new Error("BASELINE_MULTI_VALUE_INVALID");
  }
  if (value.length > 1 && value.some((item) => exclusiveCodes.includes(String(item)))) {
    throw new Error("BASELINE_MULTI_VALUE_EXCLUSIVE");
  }
}

function assertOpenText(selected: unknown, openCode: string, text: unknown): void {
  if (selected === openCode) {
    assertNonEmptyText(text);
  } else if (hasText(text)) {
    throw new Error("BASELINE_OPEN_TEXT_WITHOUT_OPTION");
  }
}

function assertArrayOpenText(selected: unknown, openCode: string, text: unknown): void {
  assertOpenText(Array.isArray(selected) && selected.includes(openCode) ? openCode : undefined, openCode, text);
}

function assertNonEmptyText(value: unknown): void {
  if (!hasText(value)) {
    throw new Error("BASELINE_OPEN_TEXT_REQUIRED");
  }
}

function hasText(value: unknown): boolean {
  return typeof value === "string" && value.trim().length > 0;
}

function isRecord(value: unknown): value is UnknownRecord {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
