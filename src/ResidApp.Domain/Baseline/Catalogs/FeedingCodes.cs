using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce FEEDING_ROUTES, FOOD_TEXTURES, LIQUID_CONSISTENCIES, FEEDING_ASSISTANCE y
/// SWALLOWING_PRECAUTIONS de validation.ts (área ALIMENTACION).</summary>
public enum FeedingRouteCode
{
    [Code("ORAL")] [Display(Name = "Oral")] Oral,
    [Code("ENTERAL")] [Display(Name = "Enteral")] Enteral,
    [Code("MIXTA")] [Display(Name = "Mixta")] Mixta,
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
}

public enum FoodTextureCode
{
    [Code("NORMAL")] [Display(Name = "Normal")] Normal,
    [Code("TROCEADA")] [Display(Name = "Troceada")] Troceada,
    [Code("TRITURADA")] [Display(Name = "Triturada")] Triturada,
    [Code("PURE")] [Display(Name = "Puré")] Pure,
    [Code("OTRA_TEXTURA_ADAPTADA")] [Display(Name = "Otra textura adaptada")] OtraTexturaAdaptada,
    [Code("NO_APLICA")] [Display(Name = "No aplica")] NoAplica,
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
}

public enum LiquidConsistencyCode
{
    [Code("IDDSI_0_FINO_SIN_ESPESAR")] [Display(Name = "IDDSI 0: fino, sin espesar")] Iddsi0FinoSinEspesar,
    [Code("IDDSI_1_LIGERAMENTE_ESPESO")] [Display(Name = "IDDSI 1: ligeramente espeso")] Iddsi1LigeramenteEspeso,
    [Code("IDDSI_2_POCO_ESPESO")] [Display(Name = "IDDSI 2: poco espeso")] Iddsi2PocoEspeso,
    [Code("IDDSI_3_MODERADAMENTE_ESPESO")] [Display(Name = "IDDSI 3: moderadamente espeso")] Iddsi3ModeradamenteEspeso,
    [Code("IDDSI_4_EXTREMADAMENTE_ESPESO")] [Display(Name = "IDDSI 4: extremadamente espeso")] Iddsi4ExtremadamenteEspeso,
    [Code("NO_APLICA")] [Display(Name = "No aplica")] NoAplica,
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
}

public enum FeedingAssistanceCode
{
    [Code("INDEPENDIENTE")] [Display(Name = "Independiente")] Independiente,
    [Code("PREPARAR_O_CORTAR_ALIMENTOS")] [Display(Name = "Preparar o cortar alimentos")] PrepararOCortarAlimentos,
    [Code("SUPERVISION_O_INDICACIONES")] [Display(Name = "Supervisión o indicaciones")] SupervisionOIndicaciones,
    [Code("AYUDA_FISICA_PARCIAL")] [Display(Name = "Ayuda física parcial")] AyudaFisicaParcial,
    [Code("AYUDA_TOTAL")] [Display(Name = "Ayuda total")] AyudaTotal,
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
}

public enum SwallowingPrecautionsCode
{
    [Code("NINGUNA_DOCUMENTADA")] [Display(Name = "Ninguna documentada")] NingunaDocumentada,
    [Code("PRECAUCIONES_DOCUMENTADAS")] [Display(Name = "Precauciones documentadas")] PrecaucionesDocumentadas,
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
}
