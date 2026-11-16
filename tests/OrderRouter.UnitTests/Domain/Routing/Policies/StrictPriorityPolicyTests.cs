using System.Collections.ObjectModel;
using OrderRouter.Domain.Common;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Domain.Routing.PlanSearch;
using OrderRouter.Domain.Routing.Policies;
using static OrderRouter.UnitTests.Domain.Routing.PlanSearch.PlanSearchTestData;

namespace OrderRouter.UnitTests.Domain.Routing.Policies;

public class StrictPriorityPolicyTests
{
    private readonly StrictPriorityPolicy policy = new(0.5m);

    private static RoutingPlan Plan(string supplier, int shipments, decimal quality, int local)
        => new(new ReadOnlyDictionary<Category, string>(new Dictionary<Category, string> { [new Category("cane")] = supplier }), shipments, quality, local);

    [Fact]
    public void MaxExtraShipments_IsZero()
        => Assert.Equal(0, policy.MaxExtraShipments);

    [Fact]
    public void SelectBest_FewerShipmentsBeatsHigherQuality()
    {
        // AC2: one shipment at 6.0 beats three shipments at 10.0.
        var request = new PlanSearchRequest(
            [Demand("wheelchair"), Demand("cane"), Demand("walker")],
            [
                Local("SUP-T007", 6m, "cane", "walker", "wheelchair"),
                Local("SUP-T008", 10m, "wheelchair"),
                Local("SUP-T009", 10m, "cane"),
                Local("SUP-T010", 10m, "walker"),
            ]);
        var plans = new PlanSearcher().Search(request).Plans;

        var best = Assert.Single(policy.SelectBest([.. plans, Plan("THREE", 3, 10m, 3)]));

        Assert.Equal(1, best.ShipmentCount);
        Assert.Equal(["SUP-T007"], Ids(best));
    }

    [Fact]
    public void SelectBest_SameShipmentsAndLocality_HigherQualityWins()
    {
        // AC3
        var best = Assert.Single(policy.SelectBest([Plan("A", 1, 8m, 1), Plan("B", 1, 9m, 1)]));

        Assert.Equal(9m, best.Quality);
    }

    [Fact]
    public void SelectBest_LocalWithinBand_LocalWins()
    {
        // AC4: 8.7 is within 0.5 of 9.0.
        var best = Assert.Single(policy.SelectBest([Plan("MAIL", 1, 9.0m, 0), Plan("LOCAL", 1, 8.7m, 1)]));

        Assert.Equal("LOCAL", best.Assignments.Values.Single());
    }

    [Fact]
    public void SelectBest_LocalOutsideBand_QualityWins()
    {
        // AC5
        var best = Assert.Single(policy.SelectBest([Plan("MAIL", 1, 9.0m, 0), Plan("LOCAL", 1, 8.0m, 1)]));

        Assert.Equal("MAIL", best.Assignments.Values.Single());
    }

    [Fact]
    public void SelectBest_ExactlyOnBandEdge_CountsAsSimilar()
    {
        var best = Assert.Single(policy.SelectBest([Plan("MAIL", 1, 9.0m, 0), Plan("LOCAL", 1, 8.5m, 1)]));

        Assert.Equal("LOCAL", best.Assignments.Values.Single());
    }

    [Fact]
    public void SelectBest_BandZero_IsPureQuality()
    {
        var best = Assert.Single(new StrictPriorityPolicy(0m).SelectBest([Plan("MAIL", 1, 9.0m, 0), Plan("LOCAL", 1, 8.99m, 1)]));

        Assert.Equal("MAIL", best.Assignments.Values.Single());
    }

    [Fact]
    public void SelectBest_SameLocalCountInsideBand_HigherQualityWins()
    {
        var best = Assert.Single(policy.SelectBest([Plan("A", 1, 8.8m, 1), Plan("B", 1, 9.0m, 1)]));

        Assert.Equal("B", best.Assignments.Values.Single());
    }

    [Fact]
    public void SelectBest_BandIsNotTransitive_ComparesToBestOnly()
    {
        // 9.0 / 8.6 / 8.2: 8.2 is within 0.5 of 8.6 but not of 9.0, so it is out.
        var plans = new[] { Plan("A", 1, 9.0m, 0), Plan("B", 1, 8.6m, 1), Plan("C", 1, 8.2m, 2) };

        var best = Assert.Single(policy.SelectBest(plans));

        Assert.Equal("B", best.Assignments.Values.Single());
    }

    [Fact]
    public void SelectBest_FullTie_ReturnsEveryTiedPlanInInputOrder()
    {
        var best = policy.SelectBest([Plan("A", 1, 9m, 1), Plan("B", 1, 9m, 1)]);

        Assert.Equal(["A", "B"], best.Select(p => p.Assignments.Values.Single()));
    }

    [Fact]
    public void SelectBest_Empty_ReturnsEmpty()
        => Assert.Empty(policy.SelectBest([]));

    [Fact]
    public void SelectBest_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => policy.SelectBest(null!));

    [Fact]
    public void Constructor_NegativeBand_Throws()
        => Assert.Throws<DomainValidationException>(() => new StrictPriorityPolicy(-0.1m));
}
