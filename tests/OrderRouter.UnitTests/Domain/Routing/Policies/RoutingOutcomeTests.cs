using System.Collections.ObjectModel;
using OrderRouter.Domain.Common;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Domain.Routing.PlanSearch;
using OrderRouter.Domain.Routing.Policies;

namespace OrderRouter.UnitTests.Domain.Routing.Policies;

public class RoutingOutcomeTests
{
    private static readonly RoutingPlan plan = new(new ReadOnlyDictionary<Category, string>(new Dictionary<Category, string>()), 1, 8m, 0);
    private static readonly UnfulfilledLine[] line = [new("X", 1, null, "NO_SUPPLIER_FOR_CATEGORY")];

    [Fact]
    public void Constructor_NullLines_BecomeEmpty()
        => Assert.Empty(new RoutingOutcome(RoutingOutcomeStatus.Routed, plan, null).Unfulfilled);

    [Fact]
    public void Constructor_ValidCombinations_AreAccepted()
    {
        _ = new RoutingOutcome(RoutingOutcomeStatus.PartiallyRouted, plan, line);
        _ = new RoutingOutcome(RoutingOutcomeStatus.NotRouted, null, line);
    }

    [Fact]
    public void Constructor_RoutedWithoutPlan_Throws()
        => Assert.Throws<DomainValidationException>(() => new RoutingOutcome(RoutingOutcomeStatus.Routed, null, []));

    [Fact]
    public void Constructor_RoutedWithUnfulfilledLines_Throws()
        => Assert.Throws<DomainValidationException>(() => new RoutingOutcome(RoutingOutcomeStatus.Routed, plan, line));

    [Fact]
    public void Constructor_PartialWithoutLines_Throws()
        => Assert.Throws<DomainValidationException>(() => new RoutingOutcome(RoutingOutcomeStatus.PartiallyRouted, plan, []));

    [Fact]
    public void Constructor_NotRoutedWithPlan_Throws()
        => Assert.Throws<DomainValidationException>(() => new RoutingOutcome(RoutingOutcomeStatus.NotRouted, plan, line));

    [Fact]
    public void Constructor_UndefinedStatus_Throws()
        => Assert.Throws<DomainValidationException>(() => new RoutingOutcome((RoutingOutcomeStatus)99, plan, []));
}
