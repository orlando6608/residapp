using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce USUAL_AIDS de validation.ts (área AYUDAS_HABITUALES). Dos opciones de texto libre
/// independientes: OTRO_PRODUCTO_DE_APOYO y OTRO.</summary>
public enum UsualAidCode
{
    [Code("GAFAS")] Gafas,
    [Code("AUDIFONO")] Audifono,
    [Code("TABLERO_O_DISPOSITIVO_COMUNICACION")] TableroODispositivoComunicacion,
    [Code("PROTESIS_DENTAL")] ProtesisDental,
    [Code("CUBIERTOS_O_VAJILLA_ADAPTADOS")] CubiertosOVajillaAdaptados,
    [Code("OTRO_PRODUCTO_DE_APOYO")] OtroProductoDeApoyo,
    [Code("OXIGENOTERAPIA_HABITUAL")] OxigenoterapiaHabitual,
    [Code("CPAP_BIPAP")] CpapBipap,
    [Code("OTRO")] Otro,
    [Code("NINGUNO")] Ninguno,
    [Code("NO_DOCUMENTADO")] NoDocumentado,
}
