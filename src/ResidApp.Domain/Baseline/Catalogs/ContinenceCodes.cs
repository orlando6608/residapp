using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce CONTINENCE_VALUES y CONTINENCE_MANAGEMENT de validation.ts (área CONTINENCIA).</summary>
public enum ContinenceValueCode
{
    [Code("CONTINENTE")] [Display(Name = "Continente")] Continente,
    [Code("INCONTINENCIA_OCASIONAL")] [Display(Name = "Incontinencia ocasional")] IncontinenciaOcasional,
    [Code("INCONTINENCIA_HABITUAL")] [Display(Name = "Incontinencia habitual")] IncontinenciaHabitual,
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
}

public enum ContinenceManagementCode
{
    [Code("NINGUNO")] [Display(Name = "Ninguno")] Ninguno,
    [Code("ABSORBENTE")] [Display(Name = "Absorbente")] Absorbente,
    [Code("SONDA_URINARIA")] [Display(Name = "Sonda urinaria")] SondaUrinaria,
    [Code("UROSTOMIA")] [Display(Name = "Urostomía")] Urostomia,
    [Code("COLOSTOMIA_ILEOSTOMIA")] [Display(Name = "Colostomía o ileostomía")] ColostomiaIleostomia,
    [Code("OTRO")] [Display(Name = "Otro")] Otro,
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
}
