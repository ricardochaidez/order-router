using OrderRouter.Domain.Routing.PlanSearch;
using static OrderRouter.UnitTests.Domain.Routing.PlanSearch.PlanSearchTestData;

namespace OrderRouter.UnitTests.Domain.Routing.PlanSearch;

public class PlanSearcherTests
{
    private readonly PlanSearcher searcher = new();

    [Fact]
    public void Search_SingleSupplierCovers_ReturnsOnlyThatPlan()
    {
        // AC3: SUP-T007 covers all three, so the better-rated specialists never form a larger plan.
        var request = new PlanSearchRequest(
            [Demand("wheelchair"), Demand("cane"), Demand("walker")],
            [
                Local("SUP-T007", 6m, "cane", "walker", "wheelchair"),
                Local("SUP-T008", 10m, "wheelchair"),
                Local("SUP-T009", 10m, "cane"),
                Local("SUP-T010", 10m, "walker"),
            ]);

        var result = searcher.Search(request);

        Assert.Equal(1, result.MinimumShipments);
        Assert.Equal(SearchMode.Exact, result.Mode);
        Assert.Null(result.FallbackReason);
        var plan = Assert.Single(result.Plans);
        Assert.Equal(["SUP-T007"], Ids(plan));
        Assert.Equal(1, plan.ShipmentCount);
    }

    [Fact]
    public void Search_NoSingleCover_FindsTwoSupplierPlanWithLineWeightedQuality()
    {
        // AC4: ORD-002 shape. Quality is (3 x 9.2 + 1 x 9.0) / 4 = 9.15.
        var request = new PlanSearchRequest(
            [Demand("bed"), Demand("lift"), Demand("commode"), Demand("bp-monitor")],
            [
                Local("SUP-0375", 9.2m, "bed", "lift", "commode"),
                Local("SUP-042", 9.0m, "bp-monitor"),
            ]);

        var result = searcher.Search(request);

        Assert.Equal(2, result.MinimumShipments);
        var plan = Assert.Single(result.Plans);
        Assert.Equal(["SUP-0375", "SUP-042"], Ids(plan));
        Assert.Equal(9.15m, plan.Quality);
        Assert.Equal(4, plan.LocalLineCount);
    }

    [Fact]
    public void Search_CategoryWithTwoLines_CountsTwiceInQuality()
    {
        var request = new PlanSearchRequest(
            [Demand("cpap", 2), Demand("mask")],
            [Local("A", 10m, "cpap"), Local("B", 4m, "mask")]);

        Assert.Equal(8m, Assert.Single(searcher.Search(request).Plans).Quality);
    }

    [Fact]
    public void Search_EqualRatingTwins_ReturnsAPlanForEach()
    {
        // AC5 at search level.
        var request = new PlanSearchRequest(
            [Demand("cane")],
            [Local("SUP-T003", 8m, "cane"), Local("SUP-T004", 8m, "cane")]);

        var result = searcher.Search(request);

        Assert.Equal([["SUP-T003"], ["SUP-T004"]], result.Plans.Select(Ids));
    }

    [Fact]
    public void Search_SharedCategory_GoesToHigherRatedSupplier()
    {
        var request = new PlanSearchRequest(
            [Demand("a"), Demand("b"), Demand("c")],
            [Local("X", 9m, "a", "b"), Local("Y", 7m, "b", "c")]);

        var plan = Assert.Single(searcher.Search(request).Plans);

        Assert.Equal("X", plan.Assignments[new("b")]);
        Assert.Equal("Y", plan.Assignments[new("c")]);
    }

    [Fact]
    public void Search_SharedCategoryEqualRating_GoesToLocalSupplier()
    {
        var request = new PlanSearchRequest(
            [Demand("a"), Demand("b"), Demand("c")],
            [Mail("X", 8m, "a", "b"), Local("Y", 8m, "b", "c")]);

        var plan = Assert.Single(searcher.Search(request).Plans);

        Assert.Equal("Y", plan.Assignments[new("b")]);
        Assert.Equal("X", plan.Assignments[new("a")]);
    }

    [Fact]
    public void Search_SupplierWouldReceiveNothing_IsNotListed()
    {
        // Z adds no category the others lack and is worse everywhere, so it is never an unused extra shipment.
        var request = new PlanSearchRequest(
            [Demand("a"), Demand("b")],
            [Local("X", 9m, "a"), Local("Y", 9m, "b"), Mail("Z", 3m, "a")],
            maxExtraShipments: 1);

        var result = searcher.Search(request);

        Assert.All(result.Plans, plan => Assert.Equal(plan.ShipmentCount, Ids(plan).Length));
    }

    [Fact]
    public void Search_ExtraShipmentRequested_ReturnsMinimumAndMinimumPlusOne()
    {
        // AC7: all plans have equal quality so the band keeps them. The 2-shipment plans are the
        // three ways to split the order; a 3-shipment plan is never searched.
        var request = new PlanSearchRequest(
            [Demand("a"), Demand("b")],
            [Local("Both", 8m, "a", "b"), Local("OnlyA", 8m, "a"), Local("OnlyB", 8m, "b")],
            maxExtraShipments: 1);

        var result = searcher.Search(request);

        Assert.Equal(1, result.MinimumShipments);
        Assert.Equal([1, 2, 2, 2], result.Plans.Select(p => p.ShipmentCount));
    }

    [Fact]
    public void Search_NoExtraShipment_ReturnsMinimumOnly()
    {
        var request = new PlanSearchRequest(
            [Demand("a"), Demand("b")],
            [Local("Both", 8m, "a", "b"), Local("OnlyA", 8m, "a"), Local("OnlyB", 8m, "b")]);

        Assert.All(searcher.Search(request).Plans, plan => Assert.Equal(1, plan.ShipmentCount));
    }

    [Fact]
    public void Search_NoCover_ReturnsEmptyResult()
    {
        var request = new PlanSearchRequest([Demand("a"), Demand("b")], [Local("X", 9m, "a")]);

        var result = searcher.Search(request);

        Assert.Empty(result.Plans);
        Assert.Equal(0, result.MinimumShipments);
    }

    [Fact]
    public void Search_NoCandidates_ReturnsEmptyResult()
    {
        Assert.Empty(searcher.Search(new PlanSearchRequest([Demand("a")], [])).Plans);
    }

    [Fact]
    public void Search_BandBoundary_KeepsPlanExactlyBandBelowAndDropsOneBeyond()
    {
        // AC8: best 9.0, band 0.5. 8.5 stays (boundary), 8.4 goes.
        var request = new PlanSearchRequest(
            [Demand("a")],
            [Local("Best", 9m, "a"), Mail("Edge", 8.5m, "a"), Mail("Low", 8.4m, "a")]);

        var result = searcher.Search(request);

        Assert.Equal([["Best"], ["Edge"]], result.Plans.Select(Ids));
    }

    [Fact]
    public void Search_ExtraShipmentBandIsPerSize_KeepsBothSizes()
    {
        // AC9: the 1-shipment best is 8.0, the 2-shipment best is 9.0. The band is never applied across counts.
        var request = new PlanSearchRequest(
            [Demand("a"), Demand("b")],
            [Local("Both", 8m, "a", "b"), Local("OnlyA", 9m, "a"), Local("OnlyB", 9m, "b")],
            0.5m,
            1);

        var result = searcher.Search(request);

        Assert.Equal(8m, result.Plans.Single(p => p.ShipmentCount == 1).Quality);
        Assert.Equal(9m, result.Plans.Where(p => p.ShipmentCount == 2).Max(p => p.Quality));
    }

    [Fact]
    public void Search_LowerQualityLargerPlan_IsDroppedAgainstBestOfItsOwnSize()
    {
        // Band 0.25. The 2-shipment plans score 9.0, 8.5, 8.5 and 8.5, so only the 9.0 plan stays. The 1-shipment plan
        // (8.0) is far below 9.0 but stays: it is the best of its own size.
        var request = new PlanSearchRequest(
            [Demand("a"), Demand("b")],
            [
                Local("Both", 8m, "a", "b"),
                Local("A9", 9m, "a"),
                Mail("A8", 8m, "a"),
                Local("B9", 9m, "b"),
            ],
            0.25m,
            1);

        var result = searcher.Search(request);

        Assert.Equal([["Both"], ["A9", "B9"]], result.Plans.Select(Ids));
    }

    [Fact]
    public void Search_RunTwice_GivesIdenticalOrderedPlans()
    {
        var request = new PlanSearchRequest(
            [Demand("a"), Demand("b")],
            [Local("Z", 8m, "a", "b"), Local("Y", 8m, "a", "b"), Local("X", 8m, "a"), Local("W", 8m, "b")],
            0.5m,
            1);

        var first = searcher.Search(request).Plans.Select(Ids).ToList();
        var second = searcher.Search(request).Plans.Select(Ids).ToList();

        Assert.Equal(first, second);
        Assert.Equal([["Y"], ["Z"]], first.Take(2));
    }

    [Fact]
    public void Search_ResultPlanAssignments_CannotBeChangedByCaller()
    {
        var request = new PlanSearchRequest([Demand("a")], [Local("X", 8m, "a")]);

        var plan = Assert.Single(searcher.Search(request).Plans);

        Assert.Throws<NotSupportedException>(() => ((IDictionary<OrderRouter.Domain.ReferenceData.Category, string>)plan.Assignments).Clear());
    }
}
