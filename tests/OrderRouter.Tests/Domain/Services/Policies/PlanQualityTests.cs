using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Services.PlanSearch;
using OrderRouter.Domain.Services.Policies;
using static OrderRouter.Tests.TestData.PlanSearchTestData;

namespace OrderRouter.Tests.Domain.Services.Policies;

public class PlanQualityTests
{
    private readonly PlanSearcher searcher = new();

    [Fact]
    public void Quality_TwoLinesInOneCategory_IsLineWeightedMean()
    {
        // AC6: (2 x 9.0 + 1 x 6.0) / 3 = 8.0
        var request = new PlanSearchRequest(
            [Demand("walker", 2), Demand("cane")],
            [Local("SUP-A", 9m, "walker"), Local("SUP-B", 6m, "cane")]);

        var plan = Assert.Single(searcher.Search(request).Plans);

        Assert.Equal(8.0m, plan.Quality);
    }

    [Fact]
    public void Quality_UnratedSupplierAtZero_IsRoutableAndPullsMeanDown()
    {
        var request = new PlanSearchRequest(
            [Demand("walker"), Demand("cane")],
            [Local("SUP-A", 8m, "walker"), Local("SUP-B", 0m, "cane")]);

        var plan = Assert.Single(searcher.Search(request).Plans);

        Assert.Equal(4.0m, plan.Quality);
    }

    [Fact]
    public void Search_RatedAndUnratedForSameCategory_RatedWins()
    {
        var request = new PlanSearchRequest(
            [Demand("cane")],
            [Local("SUP-RATED", 5m, "cane"), Local("SUP-UNRATED", 0m, "cane")]);

        var plans = new StrictPriorityPolicyProbe().Select(searcher.Search(request).Plans);

        Assert.Equal(["SUP-RATED"], Ids(Assert.Single(plans)));
    }

    private sealed class StrictPriorityPolicyProbe
    {
        public IReadOnlyList<RoutingPlan> Select(IReadOnlyList<RoutingPlan> plans)
            => new StrictPriorityPolicy(0.5m).SelectBest(plans);
    }
}
