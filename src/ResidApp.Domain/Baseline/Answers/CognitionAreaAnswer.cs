using System.Text.Json.Serialization;
using ResidApp.Domain.Baseline.Catalogs;
using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Answers;

/// <summary>Traduce el caso "COGNICION" de assertCompleteBaselineAreaAnswer (validation.ts): la regla más
/// intrincada del catálogo (etiología condicionada a demencia documentada, referencia clínica
/// obligatoria/prohibida según haya o no dato de etiología/GDS).</summary>
public sealed record CognitionAreaAnswer : IBaselineAreaAnswer
{
    public CognitionCategoryCode CategoryCode { get; init; }
    public EtiologyCode? EtiologyCode { get; init; }
    public string? EtiologyOtherText { get; init; }
    public GdsCode? GdsCode { get; init; }
    public InformationSourceCode? ClinicalReferenceSourceCode { get; init; }
    public string? ClinicalReferenceSourceOtherText { get; init; }
    public string? ClinicalReferenceDate { get; init; }

    [JsonConstructor]
    public CognitionAreaAnswer(CognitionCategoryCode categoryCode, EtiologyCode? etiologyCode, string? etiologyOtherText,
        GdsCode? gdsCode, InformationSourceCode? clinicalReferenceSourceCode, string? clinicalReferenceSourceOtherText,
        string? clinicalReferenceDate)
    {
        if (etiologyCode is not null && categoryCode != CognitionCategoryCode.DemenciaDocumentada)
        {
            throw new DomainValidationException("BASELINE_COGNITION_ETIOLOGY_INVALID");
        }
        BaselineValidation.AssertOpenText(etiologyCode == Catalogs.EtiologyCode.Otra, etiologyOtherText);

        var hasClinicalReference = etiologyCode is not null || (gdsCode is not null && gdsCode != Catalogs.GdsCode.NoDocumentado);
        if (hasClinicalReference)
        {
            if (clinicalReferenceSourceCode is null || clinicalReferenceSourceCode == InformationSourceCode.NoDocumentado)
            {
                throw new DomainValidationException("BASELINE_COGNITION_SOURCE_INVALID");
            }
            BaselineValidation.AssertNonEmptyText(clinicalReferenceDate);
        }
        else if (clinicalReferenceSourceCode is not null || !string.IsNullOrWhiteSpace(clinicalReferenceDate))
        {
            throw new DomainValidationException("BASELINE_COGNITION_REFERENCE_WITHOUT_DATA");
        }
        BaselineValidation.AssertOpenText(clinicalReferenceSourceCode == InformationSourceCode.Otra, clinicalReferenceSourceOtherText);

        CategoryCode = categoryCode;
        EtiologyCode = etiologyCode;
        EtiologyOtherText = etiologyOtherText;
        GdsCode = gdsCode;
        ClinicalReferenceSourceCode = clinicalReferenceSourceCode;
        ClinicalReferenceSourceOtherText = clinicalReferenceSourceOtherText;
        ClinicalReferenceDate = clinicalReferenceDate;
    }
}
