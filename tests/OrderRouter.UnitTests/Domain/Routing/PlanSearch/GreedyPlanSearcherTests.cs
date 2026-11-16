using OrderRouter.Domain.Routing.PlanSearch;
using static OrderRouter.UnitTests.Domain.Routing.PlanSearch.PlanSearchTestData;

namespace OrderRouter.UnitTests.Domain.Routing.PlanSearch;

public class GreedyPlanSearcherTests
{
    private readonly GreedyPlanSearcher searcher = new();

    [Fact]
    public void Search_SingleGeneralist_IsChosen()
    {
        var request = new PlanSearchRequest(
            [Demand("wheelchair"), Demand("cane"), Demand("walker")],
            [
                Local("SUP-T007", 6m, "cane", "walker", "wheelchair"),
                Local("SUP-T008", 10m, "wheelchair"),
                Local("SUP-T009", 10m, "cane"),
            ]);

        var result = searcher.Search(request);

        Assert.Equal(SearchMode.Greedy, result.Mode);
        Assert.Equal(1, result.MinimumShipments);
        Assert.Equal(["SUP-T007"], Ids(Assert.Single(result.Plans)));
    }

    [Fact]
    public void Search_Ord002Shape_UsesTwoShipmentsWithWeightedQuality()
    {
        var request = new PlanSearchRequest(
            [Demand("bed"), Demand("lift"), Demand("commode"), Demand("bp-monitor")],
            [
                Local("SUP-0375", 9.2m, "bed", "lift", "commode"),
                Local("SUP-042", 9.0m, "bp-monitor"),
            ]);

        var plan = Assert.Single(searcher.Search(request).Plans);

        Assert.Equal(2, plan.ShipmentCount);
        Assert.Equal(9.15m, plan.Quality);
    }

    [Fact]
    public void Search_CoverageTie_BrokenByRating()
    {
        var request = new PlanSearchRequest(
            [Demand("a"), Demand("b")],
            [Local("S1", 7m, "a", "b"), Local("S2", 9m, "a", "b")]);

        Assert.Equal(["S2"], Ids(Assert.Single(searcher.Search(request).Plans)));
    }

    [Fact]
    public void Search_CoverageAndRatingTie_BrokenByLocal()
    {
        var request = new PlanSearchRequest(
            [Demand("a"), Demand("b")],
            [Mail("S1", 8m, "a", "b"), Local("S2", 8m, "a", "b")]);

        var plan = Assert.Single(searcher.Search(request).Plans);

        Assert.Equal(["S2"], Ids(plan));
        Assert.Equal(2, plan.LocalLineCount);
    }

    [Fact]
    public void Search_FullTie_BrokenBySupplierIdOrdinal()
    {
        var request = new PlanSearchRequest(
            [Demand("a")],
            [Local("S2", 8m, "a"), Local("S1", 8m, "a")]);

        Assert.Equal(["S1"], Ids(Assert.Single(searcher.Search(request).Plans)));
    }

    [Fact]
    public void Search_NoCover_ReturnsNoPlans()
    {
        var request = new PlanSearchRequest([Demand("a"), Demand("b")], [Local("S1", 8m, "a")]);

        var result = searcher.Search(request);

        Assert.Empty(result.Plans);
        Assert.Equal(0, result.MinimumShipments);
        Assert.Equal(SearchMode.Greedy, result.Mode);
    }

    [Fact]
    public void Search_GreedyNeedsMoreShipmentsThanExact_DocumentsAdr0003TradeOff()
    {
        // Classic set-cover trap: greedy grabs BIG (3 of 4) first, then needs two more suppliers; the optimum is X + Y.
        var request = new PlanSearchRequest(
            [Demand("a"), Demand("b"), Demand("c"), Demand("d"), Demand("e"), Demand("f")],
            [
                Local("BIG", 8m, "a", "b", "c", "d"),
                Local("X", 8m, "a", "b", "e"),
                Local("Y", 8m, "c", "d", "f"),
            ]);

        var greedy = Assert.Single(searcher.Search(request).Plans);
        var exact = new PlanSearcher().Search(request);

        Assert.Equal(2, exact.MinimumShipments);
        Assert.Equal(3, greedy.ShipmentCount);
        Assert.Equal(["BIG", "X", "Y"], Ids(greedy));
    }

    [Fact]
    public void Search_RedundantEarlyPick_IsNotCountedAsShipment()
    {
        // BIG is picked first but the better-rated suppliers win all its categories, so it ships nothing.
        var request = new PlanSearchRequest(
            [Demand("a"), Demand("b"), Demand("c")],
            [
                Local("BIG", 5m, "a", "b"),
                Local("P", 9m, "a", "c"),
                Local("Q", 9m, "b", "c"),
            ]);

        var plan = Assert.Single(searcher.Search(request).Plans);

        Assert.DoesNotContain("BIG", plan.Assignments.Values);
        Assert.Equal(plan.Assignments.Values.Distinct().Count(), plan.ShipmentCount);
    }

    [Fact]
    public void Search_NineCategories_AreCovered()
    {
        var categories = Enumerable.Range(0, 9).Select(i => $"c{i}").ToArray();
        var request = new PlanSearchRequest(
            categories.Select(c => Demand(c)),
            [Local("S1", 8m, categories[..5]), Local("S2", 8m, categories[5..])]);

        var plan = Assert.Single(searcher.Search(request).Plans);

        Assert.Equal(9, plan.Assignments.Count);
        Assert.Equal(2, plan.ShipmentCount);
    }

    [Fact]
    public void Search_BandAndExtraShipments_AreIgnored()
    {
        var request = new PlanSearchRequest(
            [Demand("a")],
            [Local("S1", 9m, "a"), Local("S2", 9m, "a"), Local("S3", 8.9m, "a")],
            similarityBand: 10m,
            maxExtraShipments: 1);

        Assert.Single(searcher.Search(request).Plans);
    }
}
