using System.Text.Json.Serialization;
using ResidApp.Domain.Baseline.Catalogs;

namespace ResidApp.Domain.Baseline.Answers;

/// <summary>Traduce el caso "COMUNICACION" de assertCompleteBaselineAreaAnswer (validation.ts).</summary>
public sealed record CommunicationAreaAnswer : IBaselineAreaAnswer
{
    public ComprehensionCode ComprehensionCode { get; init; }
    public ExpressionCode ExpressionCode { get; init; }
    public IReadOnlyList<CommunicationFormCode> UsualFormsCodes { get; init; }
    public string? UsualFormOtherText { get; init; }

    [JsonConstructor]
    public CommunicationAreaAnswer(ComprehensionCode comprehensionCode, ExpressionCode expressionCode,
        IReadOnlyList<CommunicationFormCode> usualFormsCodes, string? usualFormOtherText)
    {
        BaselineValidation.AssertMultiChoice(usualFormsCodes);
        BaselineValidation.AssertExclusive(usualFormsCodes,
            CommunicationFormCode.NoSeIdentificaFormaEfectiva, CommunicationFormCode.NoDocumentado);
        BaselineValidation.AssertArrayOpenText(usualFormsCodes, CommunicationFormCode.Otra, usualFormOtherText);

        ComprehensionCode = comprehensionCode;
        ExpressionCode = expressionCode;
        UsualFormsCodes = usualFormsCodes;
        UsualFormOtherText = usualFormOtherText;
    }
}
