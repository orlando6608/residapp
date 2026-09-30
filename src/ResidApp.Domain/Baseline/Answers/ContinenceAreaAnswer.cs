using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ResidApp.Domain.Baseline.Catalogs;

namespace ResidApp.Domain.Baseline.Answers;

/// <summary>Traduce el caso "CONTINENCIA" de assertCompleteBaselineAreaAnswer (validation.ts).</summary>
public sealed record ContinenceAreaAnswer : IBaselineAreaAnswer
{
    [Display(Name = "Continencia urinaria")]
    public ContinenceValueCode UrinationCode { get; init; }
    [Display(Name = "Continencia fecal")]
    public ContinenceValueCode BowelCode { get; init; }
    [Display(Name = "Manejo")]
    public IReadOnlyList<ContinenceManagementCode> ManagementCodes { get; init; }
    [Display(Name = "Otro manejo")]
    public string? ManagementOtherText { get; init; }

    [JsonConstructor]
    public ContinenceAreaAnswer(ContinenceValueCode urinationCode, ContinenceValueCode bowelCode,
        IReadOnlyList<ContinenceManagementCode> managementCodes, string? managementOtherText)
    {
        BaselineValidation.AssertMultiChoice(managementCodes);
        BaselineValidation.AssertExclusive(managementCodes, ContinenceManagementCode.Ninguno, ContinenceManagementCode.NoDocumentado);
        BaselineValidation.AssertArrayOpenText(managementCodes, ContinenceManagementCode.Otro, managementOtherText);

        UrinationCode = urinationCode;
        BowelCode = bowelCode;
        ManagementCodes = managementCodes;
        ManagementOtherText = managementOtherText;
    }
}

/// <summary>Traduce el caso "ASEO_HIGIENE" de assertCompleteBaselineAreaAnswer (validation.ts). Sin reglas cruzadas.</summary>
public sealed record PersonalCareAreaAnswer(
    [property: Display(Name = "Aseo personal")] PersonalCareCode PersonalCareAssistanceCode,
    [property: Display(Name = "Baño o ducha")] BathingCode BathingAssistanceCode)
    : IBaselineAreaAnswer;
