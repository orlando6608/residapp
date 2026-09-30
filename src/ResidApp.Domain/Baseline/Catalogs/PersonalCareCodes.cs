using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce PERSONAL_CARE y BATHING de validation.ts (área ASEO_HIGIENE).</summary>
public enum PersonalCareCode
{
    [Code("INDEPENDIENTE")] [Display(Name = "Independiente")] Independiente,
    [Code("SUPERVISION_O_INDICACIONES")] [Display(Name = "Supervisión o indicaciones")] SupervisionOIndicaciones,
    [Code("AYUDA_PARCIAL")] [Display(Name = "Ayuda parcial")] AyudaParcial,
    [Code("AYUDA_TOTAL")] [Display(Name = "Ayuda total")] AyudaTotal,
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
}

public enum BathingCode
{
    [Code("INDEPENDIENTE")] [Display(Name = "Independiente")] Independiente,
    [Code("SUPERVISION")] [Display(Name = "Supervisión")] Supervision,
    [Code("AYUDA_PARCIAL")] [Display(Name = "Ayuda parcial")] AyudaParcial,
    [Code("AYUDA_TOTAL")] [Display(Name = "Ayuda total")] AyudaTotal,
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
}
