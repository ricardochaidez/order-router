using OrderRouter.Domain.Routing.PlanSearch;
using static OrderRouter.UnitTests.Domain.Routing.PlanSearch.PlanSearchTestData;

namespace OrderRouter.UnitTests.Domain.Routing.PlanSearch;

public class PlanSearcherGuardRailTests
{
    private readonly PlanSearcher searcher = new();

    private static PlanSearchRequest Categories(int count)
    {
        var names = Enumerable.Range(0, count).Select(i => $"c{i}").ToArray();

        return new PlanSearchRequest(
            names.Select(n => Demand(n)),
            [Local("S1", 8m, names[..(count / 2)]), Local("S2", 8m, names[(count / 2)..])]);
    }

    private static PlanSearchRequest ThreeSupplierOrder() => new(
        [Demand("a"), Demand("b"), Demand("c")],
        [Local("S1", 8m, "a", "b"), Local("S2", 8m, "b", "c"), Local("S3", 9m, "a"), Local("S4", 9m, "c")]);

    [Fact]
    public void Search_NineCategories_FallsBackToGreedy()
    {
        var result = searcher.Search(Categories(9));

        Assert.Equal(SearchMode.Greedy, result.Mode);
        Assert.Equal(SearchFallbackReason.TooManyCategories, result.FallbackReason);
        Assert.Single(result.Plans);
    }

    [Fact]
    public void Search_EightCategories_StaysExact()
    {
        var result = searcher.Search(Categories(8));

        Assert.Equal(SearchMode.Exact, result.Mode);
        Assert.Null(result.FallbackReason);
        Assert.NotEmpty(result.Plans);
    }

    [Fact]
    public void Search_CustomCategoryLimit_IsHonored()
    {
        var result = searcher.Search(Categories(3), new PlanSearchLimits(2, 200_000));

        Assert.Equal(SearchFallbackReason.TooManyCategories, result.FallbackReason);
    }

    [Fact]
    public void Search_BudgetOfOne_FallsBackToGreedy()
    {
        var result = searcher.Search(ThreeSupplierOrder(), new PlanSearchLimits(8, 1));

        Assert.Equal(SearchMode.Greedy, result.Mode);
        Assert.Equal(SearchFallbackReason.NodeBudgetExceeded, result.FallbackReason);
        Assert.Single(result.Plans);
    }

    [Fact]
    public void Search_BudgetExactlyEqualToNodesNeeded_StaysExact()
    {
        var request = ThreeSupplierOrder();
        var needed = Enumerable.Range(1, 10_000)
            .First(budget => searcher.Search(request, new PlanSearchLimits(8, budget)).Mode == SearchMode.Exact);

        Assert.True(needed > 1);
        Assert.Equal(SearchMode.Exact, searcher.Search(request, new PlanSearchLimits(8, needed)).Mode);
        Assert.Equal(SearchMode.Greedy, searcher.Search(request, new PlanSearchLimits(8, needed - 1)).Mode);
    }

    [Fact]
    public void Search_BudgetExceeded_NeverMixesPartialExactPlans()
    {
        var request = ThreeSupplierOrder();
        var needed = Enumerable.Range(1, 10_000)
            .First(budget => searcher.Search(request, new PlanSearchLimits(8, budget)).Mode == SearchMode.Exact);

        var result = searcher.Search(request, new PlanSearchLimits(8, needed - 1));

        Assert.Equal(new GreedyPlanSearcher().Search(request).Plans.Single().Assignments, result.Plans.Single().Assignments);
    }

    [Fact]
    public void Search_NormalOrdersWithDefaultLimits_StayExactAndMatchTheOverload()
    {
        var requests = new[]
        {
            ThreeSupplierOrder(),
            new PlanSearchRequest([Demand("a")], [Local("S1", 8m, "a")]),
            new PlanSearchRequest(
                [Demand("bed"), Demand("lift"), Demand("commode"), Demand("bp-monitor")],
                [Local("SUP-0375", 9.2m, "bed", "lift", "commode"), Local("SUP-042", 9.0m, "bp-monitor")]),
        };

        foreach (var request in requests)
        {
            var result = searcher.Search(request, PlanSearchLimits.Default);

            Assert.Equal(SearchMode.Exact, result.Mode);
            Assert.Null(result.FallbackReason);
            Assert.Equal(searcher.Search(request).Plans.Select(Ids), result.Plans.Select(Ids));
        }
    }

    [Fact]
    public void Search_TooManyCategoriesAndNoCover_ReturnsNoPlans()
    {
        var names = Enumerable.Range(0, 9).Select(i => $"c{i}").ToArray();
        var request = new PlanSearchRequest(names.Select(n => Demand(n)), [Local("S1", 8m, names[..4])]);

        var result = searcher.Search(request);

        Assert.Empty(result.Plans);
        Assert.Equal(SearchMode.Greedy, result.Mode);
    }
}
