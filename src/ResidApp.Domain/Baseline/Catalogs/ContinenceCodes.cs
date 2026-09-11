using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce CONTINENCE_VALUES y CONTINENCE_MANAGEMENT de validation.ts (área CONTINENCIA).</summary>
public enum ContinenceValueCode
{
    [Code("CONTINENTE")] Continente,
    [Code("INCONTINENCIA_OCASIONAL")] IncontinenciaOcasional,
    [Code("INCONTINENCIA_HABITUAL")] IncontinenciaHabitual,
    [Code("NO_DOCUMENTADO")] NoDocumentado,
}

public enum ContinenceManagementCode
{
    [Code("NINGUNO")] Ninguno,
    [Code("ABSORBENTE")] Absorbente,
    [Code("SONDA_URINARIA")] SondaUrinaria,
    [Code("UROSTOMIA")] Urostomia,
    [Code("COLOSTOMIA_ILEOSTOMIA")] ColostomiaIleostomia,
    [Code("OTRO")] Otro,
    [Code("NO_DOCUMENTADO")] NoDocumentado,
}
