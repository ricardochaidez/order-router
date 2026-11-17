using System.Collections.ObjectModel;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Services.Policies;

namespace OrderRouter.Tests.Domain.Services.Policies;

public class AllOrNothingPolicyTests
{
    private static readonly RoutingPlan plan = new(
        new ReadOnlyDictionary<Category, string>(new Dictionary<Category, string> { [new Category("cane")] = "SUP-1" }), 1, 8m, 1);

    private static readonly UnfulfilledLine[] oneLine = [new("X-1", 2, new Category("bed"), "NO_SUPPLIER_FOR_CATEGORY")];

    [Fact]
    public void Apply_PlanAndUnfulfilledLine_IsNotRoutedWithLineListed()
    {
        // AC7
        var outcome = AllOrNothingPolicy.Instance.Apply(plan, oneLine);

        Assert.Equal(RoutingOutcomeStatus.NotRouted, outcome.Status);
        Assert.Null(outcome.Plan);
        Assert.Equal(oneLine, outcome.Unfulfilled);
    }

    [Fact]
    public void Apply_PlanAndNoUnfulfilled_IsRouted()
    {
        var outcome = AllOrNothingPolicy.Instance.Apply(plan, []);

        Assert.Equal(RoutingOutcomeStatus.Routed, outcome.Status);
        Assert.Same(plan, outcome.Plan);
        Assert.Empty(outcome.Unfulfilled);
    }

    [Fact]
    public void Apply_NoPlanAndNoLines_IsNotRouted()
    {
        var outcome = AllOrNothingPolicy.Instance.Apply(null, []);

        Assert.Equal(RoutingOutcomeStatus.NotRouted, outcome.Status);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    public void Apply_AnyInput_NeverPartiallyRouted(bool withPlan, int lines)
    {
        var outcome = AllOrNothingPolicy.Instance.Apply(withPlan ? plan : null, [.. oneLine.Take(lines)]);

        Assert.NotEqual(RoutingOutcomeStatus.PartiallyRouted, outcome.Status);
    }

    [Fact]
    public void Apply_NullLines_Throws()
        => Assert.Throws<ArgumentNullException>(() => AllOrNothingPolicy.Instance.Apply(plan, null!));
}
