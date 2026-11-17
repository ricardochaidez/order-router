using System.Collections.ObjectModel;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Models;
using OrderRouter.Domain.Services.PlanSearch;
using OrderRouter.Domain.Services.Policies;
using static OrderRouter.Tests.TestData.PlanSearchTestData;

namespace OrderRouter.Tests.Domain.Services.Policies;

public class BoundedTradeOffPolicyTests
{
    private readonly BoundedTradeOffPolicy policy = new(0.5m, 2.0m);

    private static RoutingPlan Plan(string supplier, int shipments, decimal quality, int local = 0)
        => new(new ReadOnlyDictionary<Category, string>(new Dictionary<Category, string> { [new Category("cane")] = supplier }), shipments, quality, local);

    private static string[] Names(IEnumerable<RoutingPlan> plans)
        => [.. plans.Select(p => p.Assignments.Values.Single())];

    [Fact]
    public void MaxExtraShipments_IsOne_WhileControlIsZero()
    {
        Assert.Equal(1, policy.MaxExtraShipments);
        Assert.Equal(0, new StrictPriorityPolicy(0.5m).MaxExtraShipments);
    }

    [Fact]
    public void SelectBest_BigUplift_ExtraShipmentWins()
    {
        // AC1
        var best = Assert.Single(policy.SelectBest([Plan("ONE", 1, 6m), Plan("TWO", 2, 9m)]));

        Assert.Equal("TWO", best.Assignments.Values.Single());
    }

    [Fact]
    public void SelectBest_SmallUplift_FewerShipmentsWins()
    {
        // AC2
        var best = Assert.Single(policy.SelectBest([Plan("ONE", 1, 8m), Plan("TWO", 2, 9m)]));

        Assert.Equal("ONE", best.Assignments.Values.Single());
    }

    [Fact]
    public void SelectBest_ControlWithSamePlans_PicksFewerShipments()
    {
        // AC5
        var best = Assert.Single(new StrictPriorityPolicy(0.5m).SelectBest([Plan("ONE", 1, 6m), Plan("TWO", 2, 9m)]));

        Assert.Equal("ONE", best.Assignments.Values.Single());
    }

    [Fact]
    public void SelectBest_ThreeShipmentPlan_IsNeverSelected()
    {
        // AC3
        Assert.Equal(["TWO"], Names(policy.SelectBest([Plan("ONE", 1, 6m), Plan("TWO", 2, 9m), Plan("THREE", 3, 10m)])));
        Assert.Equal(["ONE"], Names(policy.SelectBest([Plan("ONE", 1, 6m), Plan("THREE", 3, 10m)])));
        Assert.Equal(["ONE"], Names(policy.SelectBest([Plan("ONE", 1, 8m), Plan("TWO", 2, 9m), Plan("THREE", 3, 10m)])));
    }

    [Theory]
    [InlineData(8.0, 10.0, true)]
    [InlineData(8.0, 9.99, false)]
    [InlineData(8.0, 9.9, false)]
    public void SelectBest_ThresholdEdge_IsInclusive(double baseline, double extra, bool extraWins)
    {
        var best = Assert.Single(policy.SelectBest([Plan("ONE", 1, (decimal)baseline), Plan("TWO", 2, (decimal)extra)]));

        Assert.Equal(extraWins ? "TWO" : "ONE", best.Assignments.Values.Single());
    }

    [Fact]
    public void SelectBest_CustomThreshold_FlipsOutcomes()
    {
        var strict = new BoundedTradeOffPolicy(0.5m, 4.0m);
        var loose = new BoundedTradeOffPolicy(0.5m, 0.5m);

        Assert.Equal(["ONE"], Names(strict.SelectBest([Plan("ONE", 1, 6m), Plan("TWO", 2, 9m)])));
        Assert.Equal(["TWO"], Names(loose.SelectBest([Plan("ONE", 1, 8m), Plan("TWO", 2, 9m)])));
    }

    [Theory]
    [InlineData(10.8, "EXTRA")]
    [InlineData(10.6, "LOCAL")]
    public void SelectBest_BaselineIsTheStrictPriorityPick_NotTheHighestMinimumQuality(double extraQuality, string expected)
    {
        // Band 0.5: the local plan at 8.7 is the strict pick over the mail plan at 9.0, so the challenger must beat 8.7.
        var best = Assert.Single(policy.SelectBest(
            [Plan("MAIL", 1, 9.0m, 0), Plan("LOCAL", 1, 8.7m, 1), Plan("EXTRA", 2, (decimal)extraQuality)]));

        Assert.Equal(expected, best.Assignments.Values.Single());
    }

    [Fact]
    public void SelectBest_ChallengerGroupUsesTheSameBandRanking()
    {
        var best = Assert.Single(policy.SelectBest(
            [Plan("ONE", 1, 6m), Plan("TWO-MAIL", 2, 9.0m, 0), Plan("TWO-LOCAL", 2, 8.7m, 1)]));

        Assert.Equal("TWO-LOCAL", best.Assignments.Values.Single());
    }

    [Fact]
    public void SelectBest_LowerQualityChallengerIsDroppedBeforeTheUpliftComparison()
    {
        // The 7.0 plan is outside the band of 9.0, so it never competes; 9.0 is the challenger.
        var best = Assert.Single(policy.SelectBest(
            [Plan("ONE", 1, 6m), Plan("TWO-LOW", 2, 7.0m, 2), Plan("TWO-HIGH", 2, 9.0m, 0)]));

        Assert.Equal("TWO-HIGH", best.Assignments.Values.Single());
    }

    [Fact]
    public void SelectBest_TiedBaselines_AreAllReturnedInInputOrder()
        => Assert.Equal(["A", "B"], Names(policy.SelectBest([Plan("A", 1, 8m), Plan("B", 1, 8m), Plan("TWO", 2, 9m)])));

    [Fact]
    public void SelectBest_TiedChallengers_AreAllReturnedInInputOrder()
        => Assert.Equal(["X", "Y"], Names(policy.SelectBest([Plan("ONE", 1, 6m), Plan("X", 2, 9m), Plan("Y", 2, 9m)])));

    [Fact]
    public void SelectBest_OnlyMinimumPlans_GivesTheStrictResult()
        => Assert.Equal(["B"], Names(policy.SelectBest([Plan("A", 1, 7m), Plan("B", 1, 9m)])));

    [Fact]
    public void SelectBest_Empty_ReturnsEmpty()
        => Assert.Empty(policy.SelectBest([]));

    [Fact]
    public void SelectBest_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => policy.SelectBest(null!));

    [Fact]
    public void Constructor_BadBandAndThreshold_ReportsBothErrors()
    {
        var exception = Assert.Throws<DomainValidationException>(() => new BoundedTradeOffPolicy(-0.1m, 0m));

        Assert.Contains("band", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("threshold", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_NegativeThreshold_Throws()
        => Assert.Throws<DomainValidationException>(() => new BoundedTradeOffPolicy(0.5m, -1m));

    [Fact]
    public void Constructor_ZeroBandAndTinyThreshold_AreAccepted()
        => Assert.Equal(1, new BoundedTradeOffPolicy(0m, 0.0001m).MaxExtraShipments);

    [Fact]
    public void SelectBest_RealSearch_ReturnsBothSizesAndPicksPerUplift()
    {
        // ADR-0003: the +1 plan is better by far more than the band, yet the search still returns the minimum plan.
        var plans = new PlanSearcher().Search(new PlanSearchRequest(
            [Demand("wheelchair"), Demand("cane")],
            [
                Local("SUP-T007", 6m, "cane", "wheelchair"),
                Local("SUP-T008", 10m, "wheelchair"),
                Local("SUP-T009", 10m, "cane"),
            ],
            maxExtraShipments: 1)).Plans;

        Assert.Contains(plans, p => p.ShipmentCount == 1);
        Assert.Contains(plans, p => p.ShipmentCount == 2);
        Assert.Equal(2, Assert.Single(policy.SelectBest(plans)).ShipmentCount);
        Assert.Equal(1, Assert.Single(new BoundedTradeOffPolicy(0.5m, 5m).SelectBest(plans)).ShipmentCount);
    }
}
