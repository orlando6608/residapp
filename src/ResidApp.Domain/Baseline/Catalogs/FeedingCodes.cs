using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce FEEDING_ROUTES, FOOD_TEXTURES, LIQUID_CONSISTENCIES, FEEDING_ASSISTANCE y
/// SWALLOWING_PRECAUTIONS de validation.ts (área ALIMENTACION).</summary>
public enum FeedingRouteCode
{
    [Code("ORAL")] Oral,
    [Code("ENTERAL")] Enteral,
    [Code("MIXTA")] Mixta,
    [Code("NO_DOCUMENTADO")] NoDocumentado,
}

public enum FoodTextureCode
{
    [Code("NORMAL")] Normal,
    [Code("TROCEADA")] Troceada,
    [Code("TRITURADA")] Triturada,
    [Code("PURE")] Pure,
    [Code("OTRA_TEXTURA_ADAPTADA")] OtraTexturaAdaptada,
    [Code("NO_APLICA")] NoAplica,
    [Code("NO_DOCUMENTADO")] NoDocumentado,
}

public enum LiquidConsistencyCode
{
    [Code("IDDSI_0_FINO_SIN_ESPESAR")] Iddsi0FinoSinEspesar,
    [Code("IDDSI_1_LIGERAMENTE_ESPESO")] Iddsi1LigeramenteEspeso,
    [Code("IDDSI_2_POCO_ESPESO")] Iddsi2PocoEspeso,
    [Code("IDDSI_3_MODERADAMENTE_ESPESO")] Iddsi3ModeradamenteEspeso,
    [Code("IDDSI_4_EXTREMADAMENTE_ESPESO")] Iddsi4ExtremadamenteEspeso,
    [Code("NO_APLICA")] NoAplica,
    [Code("NO_DOCUMENTADO")] NoDocumentado,
}

public enum FeedingAssistanceCode
{
    [Code("INDEPENDIENTE")] Independiente,
    [Code("PREPARAR_O_CORTAR_ALIMENTOS")] PrepararOCortarAlimentos,
    [Code("SUPERVISION_O_INDICACIONES")] SupervisionOIndicaciones,
    [Code("AYUDA_FISICA_PARCIAL")] AyudaFisicaParcial,
    [Code("AYUDA_TOTAL")] AyudaTotal,
    [Code("NO_DOCUMENTADO")] NoDocumentado,
}

public enum SwallowingPrecautionsCode
{
    [Code("NINGUNA_DOCUMENTADA")] NingunaDocumentada,
    [Code("PRECAUCIONES_DOCUMENTADAS")] PrecaucionesDocumentadas,
    [Code("NO_DOCUMENTADO")] NoDocumentado,
}
