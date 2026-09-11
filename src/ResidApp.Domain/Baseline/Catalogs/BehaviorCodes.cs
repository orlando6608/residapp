using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce BEHAVIOR_STATUS y BEHAVIOR_PATTERNS de validation.ts (área CONDUCTA).</summary>
public enum BehaviorStatusCode
{
    [Code("SIN_CONDUCTAS_RELEVANTES_CONOCIDAS")] SinConductasRelevantesConocidas,
    [Code("PATRONES_CONDUCTUALES_HABITUALES")] PatronesConductualesHabituales,
    [Code("NO_DOCUMENTADO")] NoDocumentado,
}

public enum BehaviorPatternCode
{
    [Code("APATIA_O_RETRAIMIENTO")] ApatiaORetraimiento,
    [Code("ANIMO_BAJO_HABITUAL")] AnimoBajoHabitual,
    [Code("ANSIEDAD_O_TEMOR")] AnsiedadOTemor,
    [Code("IRRITABILIDAD")] Irritabilidad,
    [Code("AGITACION_O_INQUIETUD")] AgitacionOInquietud,
    [Code("RESISTENCIA_A_LOS_CUIDADOS")] ResistenciaALosCuidados,
    [Code("CONDUCTAS_O_VOCALIZACIONES_REPETITIVAS")] ConductasOVocalizacionesRepetitivas,
    [Code("DEAMBULACION_ERRATICA_O_INTENTO_DE_SALIDA")] DeambulacionErraticaOIntentoDeSalida,
    [Code("AGRESIVIDAD_VERBAL")] AgresividadVerbal,
    [Code("AGRESIVIDAD_FISICA")] AgresividadFisica,
    [Code("DESINHIBICION")] Desinhibicion,
    [Code("IDEAS_DELIRANTES_O_ALUCINACIONES_DOCUMENTADAS")] IdeasDelirantesOAlucinacionesDocumentadas,
    [Code("OTRA")] Otra,
}
