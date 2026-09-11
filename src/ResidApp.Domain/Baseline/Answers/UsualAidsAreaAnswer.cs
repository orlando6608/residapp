using System.Text.Json.Serialization;
using ResidApp.Domain.Baseline.Catalogs;

namespace ResidApp.Domain.Baseline.Answers;

/// <summary>Traduce el caso "AYUDAS_HABITUALES" de assertCompleteBaselineAreaAnswer (validation.ts): dos
/// textos libres independientes, uno por cada opción abierta del catálogo.</summary>
public sealed record UsualAidsAreaAnswer : IBaselineAreaAnswer
{
    public IReadOnlyList<UsualAidCode> AidCodes { get; init; }
    public string? OtherSupportProductText { get; init; }
    public string? OtherSupportText { get; init; }

    [JsonConstructor]
    public UsualAidsAreaAnswer(IReadOnlyList<UsualAidCode> aidCodes, string? otherSupportProductText, string? otherSupportText)
    {
        BaselineValidation.AssertMultiChoice(aidCodes);
        BaselineValidation.AssertExclusive(aidCodes, UsualAidCode.Ninguno, UsualAidCode.NoDocumentado);
        BaselineValidation.AssertArrayOpenText(aidCodes, UsualAidCode.OtroProductoDeApoyo, otherSupportProductText);
        BaselineValidation.AssertArrayOpenText(aidCodes, UsualAidCode.Otro, otherSupportText);

        AidCodes = aidCodes;
        OtherSupportProductText = otherSupportProductText;
        OtherSupportText = otherSupportText;
    }
}
