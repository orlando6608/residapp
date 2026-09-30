using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce MOBILITY_DISPLACEMENT, MOBILITY_AIDS y MOBILITY_TRANSFERS de validation.ts (área MOVILIDAD).</summary>
public enum MobilityDisplacementCode
{
    [Code("DEAMBULA_INDEPENDIENTE_SIN_AYUDA")] [Display(Name = "Deambula independiente, sin ayuda")] DeambulaIndependienteSinAyuda,
    [Code("DEAMBULA_CON_AYUDA_TECNICA")] [Display(Name = "Deambula con ayuda técnica")] DeambulaConAyudaTecnica,
    [Code("DEAMBULA_CON_SUPERVISION")] [Display(Name = "Deambula con supervisión")] DeambulaConSupervision,
    [Code("DEAMBULA_CON_AYUDA_FISICA_1_PERSONA")] [Display(Name = "Deambula con ayuda física de 1 persona")] DeambulaConAyudaFisica1Persona,
    [Code("DEAMBULA_CON_AYUDA_FISICA_2_PERSONAS")] [Display(Name = "Deambula con ayuda física de 2 personas")] DeambulaConAyudaFisica2Personas,
    [Code("SILLA_RUEDAS_AUTOPROPULSADA")] [Display(Name = "Silla de ruedas autopropulsada")] SillaRuedasAutopropulsada,
    [Code("SILLA_RUEDAS_IMPULSADA_POR_OTRA_PERSONA")] [Display(Name = "Silla de ruedas impulsada por otra persona")] SillaRuedasImpulsadaPorOtraPersona,
    [Code("SIN_DESPLAZAMIENTO_FUNCIONAL")] [Display(Name = "Sin desplazamiento funcional")] SinDesplazamientoFuncional,
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
}

public enum MobilityAidCode
{
    [Code("NINGUNA")] [Display(Name = "Ninguna")] Ninguna,
    [Code("BASTON")] [Display(Name = "Bastón")] Baston,
    [Code("MULETA_O_MULETAS")] [Display(Name = "Muleta o muletas")] MuletaOMuletas,
    [Code("ANDADOR_4_RUEDAS")] [Display(Name = "Andador de 4 ruedas")] Andador4Ruedas,
    [Code("ANDADOR_2_RUEDAS")] [Display(Name = "Andador de 2 ruedas")] Andador2Ruedas,
    [Code("ANDADOR_FIJO_SIN_RUEDAS")] [Display(Name = "Andador fijo, sin ruedas")] AndadorFijoSinRuedas,
    [Code("OTRA")] [Display(Name = "Otra")] Otra,
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
}

public enum MobilityTransferCode
{
    [Code("INDEPENDIENTE")] [Display(Name = "Independiente")] Independiente,
    [Code("SUPERVISION")] [Display(Name = "Supervisión")] Supervision,
    [Code("AYUDA_1_PERSONA")] [Display(Name = "Ayuda de 1 persona")] Ayuda1Persona,
    [Code("AYUDA_2_PERSONAS")] [Display(Name = "Ayuda de 2 personas")] Ayuda2Personas,
    [Code("GRUA")] [Display(Name = "Grúa")] Grua,
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
}
