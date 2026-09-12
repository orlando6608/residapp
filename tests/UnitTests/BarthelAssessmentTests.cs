using ResidApp.Domain.Baseline;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

public class BarthelAssessmentTests
{
    private static IReadOnlyList<BarthelItem> CompleteItems() =>
        Enum.GetValues<BarthelItemCode>().Select(code => new BarthelItem(code, "SOME_OPTION", 5)).ToList();

    [Fact]
    public void WithAllTenItemsAndMatchingTotal_Succeeds()
    {
        var items = CompleteItems();

        var assessment = new BarthelAssessment("2026-01-01", items.Sum(i => i.AwardedScore), items);

        Assert.Equal(10, assessment.Items.Count);
    }

    [Fact]
    public void WithMissingItem_ThrowsIncomplete()
    {
        var items = CompleteItems().SkipLast(1).ToList();

        var ex = Assert.Throws<DomainValidationException>(() => new BarthelAssessment("2026-01-01", items.Sum(i => i.AwardedScore), items));
        Assert.Equal("BASELINE_BARTHEL_INCOMPLETE", ex.Code);
    }

    [Fact]
    public void WithDuplicateItemInsteadOfAMissingOne_ThrowsIncomplete()
    {
        var items = CompleteItems().SkipLast(1).ToList();
        items.Add(items[0]);

        var ex = Assert.Throws<DomainValidationException>(() => new BarthelAssessment("2026-01-01", items.Sum(i => i.AwardedScore), items));
        Assert.Equal("BASELINE_BARTHEL_INCOMPLETE", ex.Code);
    }

    [Fact]
    public void WithTotalScoreNotMatchingSum_ThrowsIncomplete()
    {
        var items = CompleteItems();

        var ex = Assert.Throws<DomainValidationException>(() => new BarthelAssessment("2026-01-01", items.Sum(i => i.AwardedScore) + 1, items));
        Assert.Equal("BASELINE_BARTHEL_INCOMPLETE", ex.Code);
    }
}
