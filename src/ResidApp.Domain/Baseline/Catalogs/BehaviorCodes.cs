using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce BEHAVIOR_STATUS y BEHAVIOR_PATTERNS de validation.ts (área CONDUCTA).</summary>
public enum BehaviorStatusCode
{
    [Code("SIN_CONDUCTAS_RELEVANTES_CONOCIDAS")] [Display(Name = "Sin conductas relevantes conocidas")] SinConductasRelevantesConocidas,
    [Code("PATRONES_CONDUCTUALES_HABITUALES")] [Display(Name = "Patrones conductuales habituales")] PatronesConductualesHabituales,
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
}

public enum BehaviorPatternCode
{
    [Code("APATIA_O_RETRAIMIENTO")] [Display(Name = "Apatía o retraimiento")] ApatiaORetraimiento,
    [Code("ANIMO_BAJO_HABITUAL")] [Display(Name = "Ánimo bajo habitual")] AnimoBajoHabitual,
    [Code("ANSIEDAD_O_TEMOR")] [Display(Name = "Ansiedad o temor")] AnsiedadOTemor,
    [Code("IRRITABILIDAD")] [Display(Name = "Irritabilidad")] Irritabilidad,
    [Code("AGITACION_O_INQUIETUD")] [Display(Name = "Agitación o inquietud")] AgitacionOInquietud,
    [Code("RESISTENCIA_A_LOS_CUIDADOS")] [Display(Name = "Resistencia a los cuidados")] ResistenciaALosCuidados,
    [Code("CONDUCTAS_O_VOCALIZACIONES_REPETITIVAS")] [Display(Name = "Conductas o vocalizaciones repetitivas")] ConductasOVocalizacionesRepetitivas,
    [Code("DEAMBULACION_ERRATICA_O_INTENTO_DE_SALIDA")] [Display(Name = "Deambulación errática o intento de salida")] DeambulacionErraticaOIntentoDeSalida,
    [Code("AGRESIVIDAD_VERBAL")] [Display(Name = "Agresividad verbal")] AgresividadVerbal,
    [Code("AGRESIVIDAD_FISICA")] [Display(Name = "Agresividad física")] AgresividadFisica,
    [Code("DESINHIBICION")] [Display(Name = "Desinhibición")] Desinhibicion,
    [Code("IDEAS_DELIRANTES_O_ALUCINACIONES_DOCUMENTADAS")] [Display(Name = "Ideas delirantes o alucinaciones documentadas")] IdeasDelirantesOAlucinacionesDocumentadas,
    [Code("OTRA")] [Display(Name = "Otra")] Otra,
}
