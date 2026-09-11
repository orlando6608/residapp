using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce INFORMATION_SOURCES de lib/domain/baseline/validation.ts.</summary>
public enum InformationSourceCode
{
    [Code("VALORACION_DIRECTA")] ValoracionDirecta,
    [Code("HISTORIA_O_INFORME_CLINICO")] HistoriaOInformeClinico,
    [Code("PERSONAL_DEL_CENTRO")] PersonalDelCentro,
    [Code("FAMILIAR_O_CUIDADOR")] FamiliarOCuidador,
    [Code("FUENTES_COMBINADAS")] FuentesCombinadas,
    [Code("OTRA")] Otra,
    [Code("NO_DOCUMENTADO")] NoDocumentado,
}
