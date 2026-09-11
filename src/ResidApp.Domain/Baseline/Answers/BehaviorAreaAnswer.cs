using System.Text.Json.Serialization;
using ResidApp.Domain.Baseline.Catalogs;
using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Answers;

/// <summary>Traduce el caso "CONDUCTA" de assertCompleteBaselineAreaAnswer (validation.ts): los patrones
/// solo son obligatorios (y no vacíos) cuando el estado es PATRONES_CONDUCTUALES_HABITUALES; en cualquier
/// otro estado, la lista debe quedar vacía.</summary>
public sealed record BehaviorAreaAnswer : IBaselineAreaAnswer
{
    public BehaviorStatusCode StatusCode { get; init; }
    public IReadOnlyList<BehaviorPatternCode> PatternCodes { get; init; }
    public string? PatternOtherText { get; init; }

    [JsonConstructor]
    public BehaviorAreaAnswer(BehaviorStatusCode statusCode, IReadOnlyList<BehaviorPatternCode> patternCodes, string? patternOtherText)
    {
        if (statusCode == BehaviorStatusCode.PatronesConductualesHabituales)
        {
            BaselineValidation.AssertMultiChoice(patternCodes);
        }
        else if (patternCodes.Count != 0)
        {
            throw new DomainValidationException("BASELINE_BEHAVIOR_PATTERNS_INVALID");
        }
        BaselineValidation.AssertArrayOpenText(patternCodes, BehaviorPatternCode.Otra, patternOtherText);

        StatusCode = statusCode;
        PatternCodes = patternCodes;
        PatternOtherText = patternOtherText;
    }
}

/// <summary>Traduce el caso "SUENO" de assertCompleteBaselineAreaAnswer (validation.ts). Sin opción de
/// texto libre: SLEEP_PATTERNS no tiene OTRA/OTRO.</summary>
public sealed record SleepAreaAnswer : IBaselineAreaAnswer
{
    public IReadOnlyList<SleepPatternCode> PatternCodes { get; init; }

    [JsonConstructor]
    public SleepAreaAnswer(IReadOnlyList<SleepPatternCode> patternCodes)
    {
        BaselineValidation.AssertMultiChoice(patternCodes);
        BaselineValidation.AssertExclusive(patternCodes, SleepPatternCode.PatronHabitualmenteConservado, SleepPatternCode.NoDocumentado);
        PatternCodes = patternCodes;
    }
}
