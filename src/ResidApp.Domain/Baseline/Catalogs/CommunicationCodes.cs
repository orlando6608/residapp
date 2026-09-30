using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce COMPREHENSION, EXPRESSION y COMMUNICATION_FORMS de validation.ts (área COMUNICACION).</summary>
public enum ComprehensionCode
{
    [Code("COMPRENSION_FUNCIONAL")] [Display(Name = "Comprensión funcional")] ComprensionFuncional,
    [Code("NECESITA_FRASES_SENCILLAS_REPETICION_O_APOYO")] [Display(Name = "Necesita frases sencillas, repetición o apoyo")] NecesitaFrasesSencillasRepeticionOApoyo,
    [Code("COMPRENSION_MUY_LIMITADA")] [Display(Name = "Comprensión muy limitada")] ComprensionMuyLimitada,
    [Code("NO_SE_HA_PODIDO_DETERMINAR")] [Display(Name = "No se ha podido determinar")] NoSeHaPodidoDeterminar,
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
}

public enum ExpressionCode
{
    [Code("EXPRESA_NECESIDADES_EFICAZMENTE")] [Display(Name = "Expresa necesidades eficazmente")] ExpresaNecesidadesEficazmente,
    [Code("EXPRESION_VERBAL_LIMITADA_PERO_COMUNICA_NECESIDADES_BASICAS")] [Display(Name = "Expresión verbal limitada, pero comunica necesidades básicas")] ExpresionVerbalLimitadaPeroComunicaNecesidadesBasicas,
    [Code("COMUNICACION_PRINCIPALMENTE_NO_VERBAL")] [Display(Name = "Comunicación principalmente no verbal")] ComunicacionPrincipalmenteNoVerbal,
    [Code("NO_EXPRESA_NECESIDADES_DE_FORMA_FIABLE")] [Display(Name = "No expresa necesidades de forma fiable")] NoExpresaNecesidadesDeFormaFiable,
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
}

public enum CommunicationFormCode
{
    [Code("LENGUAJE_ORAL")] [Display(Name = "Lenguaje oral")] LenguajeOral,
    [Code("GESTOS")] [Display(Name = "Gestos")] Gestos,
    [Code("ESCRITURA")] [Display(Name = "Escritura")] Escritura,
    [Code("TABLERO_O_DISPOSITIVO")] [Display(Name = "Tablero o dispositivo")] TableroODispositivo,
    [Code("OTRA")] [Display(Name = "Otra")] Otra,
    [Code("NO_SE_IDENTIFICA_FORMA_EFECTIVA")] [Display(Name = "No se identifica forma efectiva")] NoSeIdentificaFormaEfectiva,
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
}
