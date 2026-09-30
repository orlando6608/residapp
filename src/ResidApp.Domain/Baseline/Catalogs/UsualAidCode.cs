using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce USUAL_AIDS de validation.ts (área AYUDAS_HABITUALES). Dos opciones de texto libre
/// independientes: OTRO_PRODUCTO_DE_APOYO y OTRO.</summary>
public enum UsualAidCode
{
    [Code("GAFAS")] [Display(Name = "Gafas")] Gafas,
    [Code("AUDIFONO")] [Display(Name = "Audífono")] Audifono,
    [Code("TABLERO_O_DISPOSITIVO_COMUNICACION")] [Display(Name = "Tablero o dispositivo de comunicación")] TableroODispositivoComunicacion,
    [Code("PROTESIS_DENTAL")] [Display(Name = "Prótesis dental")] ProtesisDental,
    [Code("CUBIERTOS_O_VAJILLA_ADAPTADOS")] [Display(Name = "Cubiertos o vajilla adaptados")] CubiertosOVajillaAdaptados,
    [Code("OTRO_PRODUCTO_DE_APOYO")] [Display(Name = "Otro producto de apoyo")] OtroProductoDeApoyo,
    [Code("OXIGENOTERAPIA_HABITUAL")] [Display(Name = "Oxigenoterapia habitual")] OxigenoterapiaHabitual,
    [Code("CPAP_BIPAP")] [Display(Name = "CPAP o BiPAP")] CpapBipap,
    [Code("OTRO")] [Display(Name = "Otro")] Otro,
    [Code("NINGUNO")] [Display(Name = "Ninguno")] Ninguno,
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
}
