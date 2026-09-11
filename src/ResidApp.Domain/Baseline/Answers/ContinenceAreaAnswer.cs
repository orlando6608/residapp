using System.Text.Json.Serialization;
using ResidApp.Domain.Baseline.Catalogs;

namespace ResidApp.Domain.Baseline.Answers;

/// <summary>Traduce el caso "CONTINENCIA" de assertCompleteBaselineAreaAnswer (validation.ts).</summary>
public sealed record ContinenceAreaAnswer : IBaselineAreaAnswer
{
    public ContinenceValueCode UrinationCode { get; init; }
    public ContinenceValueCode BowelCode { get; init; }
    public IReadOnlyList<ContinenceManagementCode> ManagementCodes { get; init; }
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
public sealed record PersonalCareAreaAnswer(PersonalCareCode PersonalCareAssistanceCode, BathingCode BathingAssistanceCode)
    : IBaselineAreaAnswer;
