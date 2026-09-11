using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce MOBILITY_DISPLACEMENT, MOBILITY_AIDS y MOBILITY_TRANSFERS de validation.ts (área MOVILIDAD).</summary>
public enum MobilityDisplacementCode
{
    [Code("DEAMBULA_INDEPENDIENTE_SIN_AYUDA")] DeambulaIndependienteSinAyuda,
    [Code("DEAMBULA_CON_AYUDA_TECNICA")] DeambulaConAyudaTecnica,
    [Code("DEAMBULA_CON_SUPERVISION")] DeambulaConSupervision,
    [Code("DEAMBULA_CON_AYUDA_FISICA_1_PERSONA")] DeambulaConAyudaFisica1Persona,
    [Code("DEAMBULA_CON_AYUDA_FISICA_2_PERSONAS")] DeambulaConAyudaFisica2Personas,
    [Code("SILLA_RUEDAS_AUTOPROPULSADA")] SillaRuedasAutopropulsada,
    [Code("SILLA_RUEDAS_IMPULSADA_POR_OTRA_PERSONA")] SillaRuedasImpulsadaPorOtraPersona,
    [Code("SIN_DESPLAZAMIENTO_FUNCIONAL")] SinDesplazamientoFuncional,
    [Code("NO_DOCUMENTADO")] NoDocumentado,
}

public enum MobilityAidCode
{
    [Code("NINGUNA")] Ninguna,
    [Code("BASTON")] Baston,
    [Code("MULETA_O_MULETAS")] MuletaOMuletas,
    [Code("ANDADOR_4_RUEDAS")] Andador4Ruedas,
    [Code("ANDADOR_2_RUEDAS")] Andador2Ruedas,
    [Code("ANDADOR_FIJO_SIN_RUEDAS")] AndadorFijoSinRuedas,
    [Code("OTRA")] Otra,
    [Code("NO_DOCUMENTADO")] NoDocumentado,
}

public enum MobilityTransferCode
{
    [Code("INDEPENDIENTE")] Independiente,
    [Code("SUPERVISION")] Supervision,
    [Code("AYUDA_1_PERSONA")] Ayuda1Persona,
    [Code("AYUDA_2_PERSONAS")] Ayuda2Personas,
    [Code("GRUA")] Grua,
    [Code("NO_DOCUMENTADO")] NoDocumentado,
}
