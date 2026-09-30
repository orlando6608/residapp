using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Baseline;

/// <summary>Traduce BARTHEL_ITEMS de db/repositories/baseline-repository.ts. Los 10 ítems del índice de Barthel.</summary>
public enum BarthelItemCode
{
    [Code("COMER"), Display(Name = "Comer")] Comer,
    [Code("LAVARSE"), Display(Name = "Lavarse")] Lavarse,
    [Code("VESTIRSE"), Display(Name = "Vestirse")] Vestirse,
    [Code("ARREGLARSE"), Display(Name = "Arreglarse")] Arreglarse,
    [Code("DEPOSICION"), Display(Name = "Deposición")] Deposicion,
    [Code("MICCION"), Display(Name = "Micción")] Miccion,
    [Code("USO_RETRETE"), Display(Name = "Uso del retrete")] UsoRetrete,
    [Code("TRASLADO_CAMA_SILLON"), Display(Name = "Traslado cama-sillón")] TrasladoCamaSillon,
    [Code("DEAMBULACION"), Display(Name = "Deambulación")] Deambulacion,
    [Code("ESCALERAS"), Display(Name = "Escaleras")] Escaleras,
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
