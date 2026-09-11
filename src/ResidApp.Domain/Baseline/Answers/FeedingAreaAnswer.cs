using System.Text.Json.Serialization;
using ResidApp.Domain.Baseline.Catalogs;
using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Answers;

/// <summary>Traduce el caso "ALIMENTACION" de assertCompleteBaselineAreaAnswer (validation.ts). Incluye la
/// regla no negociable de AGENTS.md: NO_APLICA en textura/líquidos solo es válido si la vía es ENTERAL.</summary>
public sealed record FeedingAreaAnswer : IBaselineAreaAnswer
{
    public FeedingRouteCode RouteCode { get; init; }
    public FoodTextureCode FoodTextureCode { get; init; }
    public string? FoodTextureOtherText { get; init; }
    public LiquidConsistencyCode LiquidConsistencyCode { get; init; }
    public FeedingAssistanceCode AssistanceCode { get; init; }
    public SwallowingPrecautionsCode SwallowingPrecautionsCode { get; init; }
    public string? SwallowingPrecautionsText { get; init; }

    [JsonConstructor]
    public FeedingAreaAnswer(FeedingRouteCode routeCode, FoodTextureCode foodTextureCode, string? foodTextureOtherText,
        LiquidConsistencyCode liquidConsistencyCode, FeedingAssistanceCode assistanceCode,
        SwallowingPrecautionsCode swallowingPrecautionsCode, string? swallowingPrecautionsText)
    {
        var notApplicable = foodTextureCode == FoodTextureCode.NoAplica || liquidConsistencyCode == LiquidConsistencyCode.NoAplica;
        if (notApplicable && routeCode != FeedingRouteCode.Enteral)
        {
            throw new DomainValidationException("BASELINE_FEEDING_NOT_APPLICABLE_INVALID");
        }
        BaselineValidation.AssertOpenText(foodTextureCode == FoodTextureCode.OtraTexturaAdaptada, foodTextureOtherText);
        BaselineValidation.AssertOpenText(
            swallowingPrecautionsCode == SwallowingPrecautionsCode.PrecaucionesDocumentadas, swallowingPrecautionsText);

        RouteCode = routeCode;
        FoodTextureCode = foodTextureCode;
        FoodTextureOtherText = foodTextureOtherText;
        LiquidConsistencyCode = liquidConsistencyCode;
        AssistanceCode = assistanceCode;
        SwallowingPrecautionsCode = swallowingPrecautionsCode;
        SwallowingPrecautionsText = swallowingPrecautionsText;
    }
}
