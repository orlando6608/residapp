using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce COMPREHENSION, EXPRESSION y COMMUNICATION_FORMS de validation.ts (área COMUNICACION).</summary>
public enum ComprehensionCode
{
    [Code("COMPRENSION_FUNCIONAL")] ComprensionFuncional,
    [Code("NECESITA_FRASES_SENCILLAS_REPETICION_O_APOYO")] NecesitaFrasesSencillasRepeticionOApoyo,
    [Code("COMPRENSION_MUY_LIMITADA")] ComprensionMuyLimitada,
    [Code("NO_SE_HA_PODIDO_DETERMINAR")] NoSeHaPodidoDeterminar,
    [Code("NO_DOCUMENTADO")] NoDocumentado,
}

public enum ExpressionCode
{
    [Code("EXPRESA_NECESIDADES_EFICAZMENTE")] ExpresaNecesidadesEficazmente,
    [Code("EXPRESION_VERBAL_LIMITADA_PERO_COMUNICA_NECESIDADES_BASICAS")] ExpresionVerbalLimitadaPeroComunicaNecesidadesBasicas,
    [Code("COMUNICACION_PRINCIPALMENTE_NO_VERBAL")] ComunicacionPrincipalmenteNoVerbal,
    [Code("NO_EXPRESA_NECESIDADES_DE_FORMA_FIABLE")] NoExpresaNecesidadesDeFormaFiable,
    [Code("NO_DOCUMENTADO")] NoDocumentado,
}

public enum CommunicationFormCode
{
    [Code("LENGUAJE_ORAL")] LenguajeOral,
    [Code("GESTOS")] Gestos,
    [Code("ESCRITURA")] Escritura,
    [Code("TABLERO_O_DISPOSITIVO")] TableroODispositivo,
    [Code("OTRA")] Otra,
    [Code("NO_SE_IDENTIFICA_FORMA_EFECTIVA")] NoSeIdentificaFormaEfectiva,
    [Code("NO_DOCUMENTADO")] NoDocumentado,
}
