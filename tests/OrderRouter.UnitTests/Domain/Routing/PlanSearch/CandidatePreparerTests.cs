using OrderRouter.Domain.Routing.PlanSearch;
using static OrderRouter.UnitTests.Domain.Routing.PlanSearch.PlanSearchTestData;

namespace OrderRouter.UnitTests.Domain.Routing.PlanSearch;

public class CandidatePreparerTests
{
    [Fact]
    public void Prepare_TwoLinesOfOneCategory_GivesOneCategoryWithTwoLines()
    {
        // AC1: the two CPAP lines are one demand, so they get one bit and stay together.
        var request = new PlanSearchRequest([Demand("cpap", 2), Demand("mask")], [Local("A", 8m, "cpap", "mask")]);

        var prepared = CandidatePreparer.Prepare(request);

        Assert.Equal(2, prepared.Categories.Count);
        Assert.Equal(2, prepared.LineCounts[prepared.Categories.ToList().FindIndex(c => c.Value == "cpap")]);
        Assert.Equal(3u, Assert.Single(prepared.Candidates).Mask);
    }

    [Fact]
    public void Prepare_QuantityThreeLine_IsOneDemandUnit()
    {
        // AC2: a quantity-3 line is one line; the search never splits it across suppliers.
        var request = new PlanSearchRequest([Demand("cane")], [Local("A", 8m, "cane")]);

        Assert.Equal(1, Assert.Single(CandidatePreparer.Prepare(request).LineCounts));
    }

    [Fact]
    public void Prepare_CandidateCoversNothingDemanded_IsDropped()
    {
        var request = new PlanSearchRequest([Demand("cane")], [Local("A", 8m, "walker"), Local("B", 8m, "cane", "walker")]);

        var prepared = CandidatePreparer.Prepare(request);

        Assert.Equal("B", Assert.Single(prepared.Candidates).SupplierId);
        Assert.Equal(1u, prepared.Candidates[0].Mask);
    }

    [Fact]
    public void Prepare_EqualRatingTwins_AreBothKept()
    {
        // AC5: SUP-T003 and SUP-T004 look alike but the tie-break needs both.
        var request = new PlanSearchRequest([Demand("cane")], [Local("SUP-T004", 8m, "cane"), Local("SUP-T003", 8m, "cane")]);

        var prepared = CandidatePreparer.Prepare(request);

        Assert.Equal(["SUP-T003", "SUP-T004"], prepared.Candidates.Select(c => c.SupplierId));
    }

    [Fact]
    public void Prepare_SameMaskAndModeLowerRating_IsDropped()
    {
        // AC6
        var request = new PlanSearchRequest([Demand("cane")], [Local("A", 8m, "cane"), Local("B", 9m, "cane")]);

        Assert.Equal("B", Assert.Single(CandidatePreparer.Prepare(request).Candidates).SupplierId);
    }

    [Fact]
    public void Prepare_SameMaskDifferentMode_IsNotDominated()
    {
        // AC6: local and mail are not look-alikes.
        var request = new PlanSearchRequest([Demand("cane")], [Local("A", 8m, "cane"), Mail("B", 9m, "cane")]);

        Assert.Equal(2, CandidatePreparer.Prepare(request).Candidates.Count);
    }

    [Fact]
    public void Prepare_DifferentMasks_AreNotDominated()
    {
        var request = new PlanSearchRequest(
            [Demand("cane"), Demand("walker")],
            [Local("A", 9m, "cane"), Local("B", 5m, "cane", "walker")]);

        Assert.Equal(2, CandidatePreparer.Prepare(request).Candidates.Count);
    }

    [Fact]
    public void Prepare_InputOrderChanged_GivesSameResult()
    {
        var forward = new PlanSearchRequest(
            [Demand("cane"), Demand("walker")],
            [Local("A", 9m, "cane"), Local("B", 5m, "walker")]);
        var reversed = new PlanSearchRequest(
            [Demand("walker"), Demand("cane")],
            [Local("B", 5m, "walker"), Local("A", 9m, "cane")]);

        var first = CandidatePreparer.Prepare(forward);
        var second = CandidatePreparer.Prepare(reversed);

        Assert.Equal(first.Categories, second.Categories);
        Assert.Equal(first.Candidates, second.Candidates);
    }
}
