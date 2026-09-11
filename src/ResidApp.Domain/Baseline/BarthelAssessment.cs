using ResidApp.Shared;

namespace ResidApp.Domain.Baseline;

/// <summary>Traduce BARTHEL_ITEMS de db/repositories/baseline-repository.ts. Los 10 ítems del índice de Barthel.</summary>
public enum BarthelItemCode
{
    [Code("COMER")] Comer,
    [Code("LAVARSE")] Lavarse,
    [Code("VESTIRSE")] Vestirse,
    [Code("ARREGLARSE")] Arreglarse,
    [Code("DEPOSICION")] Deposicion,
    [Code("MICCION")] Miccion,
    [Code("USO_RETRETE")] UsoRetrete,
    [Code("TRASLADO_CAMA_SILLON")] TrasladoCamaSillon,
    [Code("DEAMBULACION")] Deambulacion,
    [Code("ESCALERAS")] Escaleras,
}

public sealed record BarthelItem(BarthelItemCode ItemCode, string SelectedOptionCode, int AwardedScore);

/// <summary>
/// Traduce assertBarthelComplete de db/repositories/baseline-repository.ts: instrumento fijo, los 10
/// ítems presentes sin duplicar, y la suma de puntuaciones coincidiendo con el total declarado.
/// </summary>
public sealed record BarthelAssessment
{
    public const string InstrumentVersionCode = "BARTHEL_COMUN_V0_1";

    public string AssessmentDate { get; }
    public int TotalScore { get; }
    public IReadOnlyList<BarthelItem> Items { get; }

    public BarthelAssessment(string assessmentDate, int totalScore, IReadOnlyList<BarthelItem> items)
    {
        var codes = items.Select(i => i.ItemCode).ToHashSet();
        var expected = Enum.GetValues<BarthelItemCode>();
        var sum = items.Sum(i => i.AwardedScore);
        if (items.Count != expected.Length || expected.Any(code => !codes.Contains(code)) || sum != totalScore)
        {
            throw new DomainValidationException("BASELINE_BARTHEL_INCOMPLETE");
        }

        AssessmentDate = assessmentDate;
        TotalScore = totalScore;
        Items = items;
    }
}
