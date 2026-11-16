using System.Collections.ObjectModel;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Domain.Routing.PlanSearch;
using OrderRouter.Domain.Routing.Policies;

namespace OrderRouter.UnitTests.Domain.Routing.Policies;

public class PartialRoutingPolicyTests
{
    private static readonly RoutingPlan plan = new(
        new ReadOnlyDictionary<Category, string>(new Dictionary<Category, string> { [new Category("cane")] = "SUP-1" }), 1, 8m, 1);

    private static readonly UnfulfilledLine[] oneLine = [new("X-1", 2, new Category("bed"), "NO_SUPPLIER_FOR_CATEGORY")];

    [Fact]
    public void Apply_PlanAndUnfulfilledLine_IsPartiallyRouted()
    {
        // AC1
        var outcome = PartialRoutingPolicy.Instance.Apply(plan, oneLine);

        Assert.Equal(RoutingOutcomeStatus.PartiallyRouted, outcome.Status);
        Assert.Same(plan, outcome.Plan);
        Assert.Equal(oneLine, outcome.Unfulfilled);
    }

    [Fact]
    public void Apply_NoPlanAndUnfulfilledLines_IsNotRouted()
    {
        // AC2
        var outcome = PartialRoutingPolicy.Instance.Apply(null, oneLine);

        Assert.Equal(RoutingOutcomeStatus.NotRouted, outcome.Status);
        Assert.Null(outcome.Plan);
        Assert.Equal(oneLine, outcome.Unfulfilled);
    }

    [Fact]
    public void Apply_PlanAndNoUnfulfilled_IsRouted()
    {
        // AC3
        var outcome = PartialRoutingPolicy.Instance.Apply(plan, []);

        Assert.Equal(RoutingOutcomeStatus.Routed, outcome.Status);
        Assert.Same(plan, outcome.Plan);
        Assert.Empty(outcome.Unfulfilled);
    }

    [Fact]
    public void Apply_NoPlanAndNoLines_IsNotRouted()
        => Assert.Equal(RoutingOutcomeStatus.NotRouted, PartialRoutingPolicy.Instance.Apply(null, []).Status);

    [Fact]
    public void Apply_NullLines_Throws()
        => Assert.Throws<ArgumentNullException>(() => PartialRoutingPolicy.Instance.Apply(plan, null!));

    [Fact]
    public void Instance_IsShared()
        => Assert.Same(PartialRoutingPolicy.Instance, PartialRoutingPolicy.Instance);
}
