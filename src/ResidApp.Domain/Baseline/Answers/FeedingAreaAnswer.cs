using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ResidApp.Domain.Baseline.Catalogs;
using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Answers;

/// <summary>Traduce el caso "ALIMENTACION" de assertCompleteBaselineAreaAnswer (validation.ts). Incluye la
/// regla no negociable de AGENTS.md: NO_APLICA en textura/líquidos solo es válido si la vía es ENTERAL.</summary>
public sealed record FeedingAreaAnswer : IBaselineAreaAnswer
{
    [Display(Name = "Vía de alimentación")]
    public FeedingRouteCode RouteCode { get; init; }
    [Display(Name = "Textura de los alimentos")]
    public FoodTextureCode FoodTextureCode { get; init; }
    [Display(Name = "Otra textura adaptada")]
    public string? FoodTextureOtherText { get; init; }
    [Display(Name = "Consistencia de líquidos")]
    public LiquidConsistencyCode LiquidConsistencyCode { get; init; }
    [Display(Name = "Ayuda para alimentarse")]
    public FeedingAssistanceCode AssistanceCode { get; init; }
    [Display(Name = "Precauciones de deglución")]
    public SwallowingPrecautionsCode SwallowingPrecautionsCode { get; init; }
    [Display(Name = "Precauciones documentadas")]
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
