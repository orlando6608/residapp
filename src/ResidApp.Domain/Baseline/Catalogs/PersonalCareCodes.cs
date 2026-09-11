using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce PERSONAL_CARE y BATHING de validation.ts (área ASEO_HIGIENE).</summary>
public enum PersonalCareCode
{
    [Code("INDEPENDIENTE")] Independiente,
    [Code("SUPERVISION_O_INDICACIONES")] SupervisionOIndicaciones,
    [Code("AYUDA_PARCIAL")] AyudaParcial,
    [Code("AYUDA_TOTAL")] AyudaTotal,
    [Code("NO_DOCUMENTADO")] NoDocumentado,
}

public enum BathingCode
{
    [Code("INDEPENDIENTE")] Independiente,
    [Code("SUPERVISION")] Supervision,
    [Code("AYUDA_PARCIAL")] AyudaParcial,
    [Code("AYUDA_TOTAL")] AyudaTotal,
    [Code("NO_DOCUMENTADO")] NoDocumentado,
}
