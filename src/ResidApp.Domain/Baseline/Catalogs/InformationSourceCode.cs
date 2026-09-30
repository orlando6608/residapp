using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce INFORMATION_SOURCES de lib/domain/baseline/validation.ts.</summary>
public enum InformationSourceCode
{
    [Code("VALORACION_DIRECTA")] [Display(Name = "Valoración directa")] ValoracionDirecta,
    [Code("HISTORIA_O_INFORME_CLINICO")] [Display(Name = "Historia o informe clínico")] HistoriaOInformeClinico,
    [Code("PERSONAL_DEL_CENTRO")] [Display(Name = "Personal del centro")] PersonalDelCentro,
    [Code("FAMILIAR_O_CUIDADOR")] [Display(Name = "Familiar o cuidador")] FamiliarOCuidador,
    [Code("FUENTES_COMBINADAS")] [Display(Name = "Fuentes combinadas")] FuentesCombinadas,
    [Code("OTRA")] [Display(Name = "Otra")] Otra,
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
}
