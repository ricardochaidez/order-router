using OrderRouter.Domain.Common;
using OrderRouter.Domain.Eligibility;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Domain.Routing;
using OrderRouter.Domain.Routing.Experiments;
using OrderRouter.Domain.Routing.PlanSearch;
using OrderRouter.Domain.Routing.Policies;
using OrderRouter.Domain.Routing.TieBreaking;
using OrderRouter.UnitTests.Domain.Routing.TieBreaking;
using static OrderRouter.UnitTests.Domain.Routing.TieBreaking.TieBreakTestData;

namespace OrderRouter.UnitTests.Domain.Routing;

public class RoutingDecisionTests
{
    private static readonly Supplier supplier = new("S1", "S1", [new Category("cane")], new ServiceArea([ZipRange.Parse("10000-10099")]), 8m, false);
    private static readonly RoutedLine routed = new("A", 1, new Category("cane"), supplier, FulfillmentMode.Local);
    private static readonly UnfulfilledLine unfulfilled = new("B", 1, null, UnfulfilledLine.UnknownProductCode);
    private static readonly TieBreakResult tie = new UniformRandomTieBreaker(new FixedRandomSource(0)).Pick([Plan("S1")]);

    private static RoutingDecision Build(
        RoutingOutcomeStatus status,
        RoutedLine[]? lines = null,
        UnfulfilledLine[]? notRouted = null,
        TieBreakResult? tieBreak = null,
        SearchMode? mode = null,
        SearchFallbackReason? reason = null)
        => new(status, lines, notRouted, ExperimentAssignment.AllControl, tieBreak, mode, reason);

    [Fact]
    public void Constructor_Routed_Succeeds()
    {
        var decision = Build(RoutingOutcomeStatus.Routed, [routed], [], tie, SearchMode.Exact);

        Assert.Single(decision.RoutedLines);
        Assert.Same(ExperimentAssignment.AllControl, decision.Assignment);
    }

    [Fact]
    public void Constructor_NotRoutedWithoutSearch_AllowsNullTieBreakAndEmptyCollections()
    {
        var decision = Build(RoutingOutcomeStatus.NotRouted, null, null);

        Assert.Empty(decision.RoutedLines);
        Assert.Empty(decision.Unfulfilled);
    }

    [Fact]
    public void Constructor_NotRoutedAfterSearch_Succeeds()
    {
        var decision = Build(RoutingOutcomeStatus.NotRouted, [], [unfulfilled], tie, SearchMode.Exact);

        Assert.NotNull(decision.TieBreak);
    }

    [Fact]
    public void Constructor_PartiallyRouted_Succeeds()
    {
        Assert.Equal(
            RoutingOutcomeStatus.PartiallyRouted,
            Build(RoutingOutcomeStatus.PartiallyRouted, [routed], [unfulfilled], tie, SearchMode.Exact).Status);
    }

    [Fact]
    public void Constructor_RoutedWithoutLines_Throws()
        => Assert.Throws<DomainValidationException>(() => Build(RoutingOutcomeStatus.Routed, [], [], tie, SearchMode.Exact));

    [Fact]
    public void Constructor_NotRoutedWithLines_Throws()
        => Assert.Throws<DomainValidationException>(() => Build(RoutingOutcomeStatus.NotRouted, [routed], [], tie, SearchMode.Exact));

    [Fact]
    public void Constructor_RoutedWithUnfulfilled_Throws()
        => Assert.Throws<DomainValidationException>(() => Build(RoutingOutcomeStatus.Routed, [routed], [unfulfilled], tie, SearchMode.Exact));

    [Fact]
    public void Constructor_PartiallyRoutedWithoutUnfulfilled_Throws()
        => Assert.Throws<DomainValidationException>(() => Build(RoutingOutcomeStatus.PartiallyRouted, [routed], [], tie, SearchMode.Exact));

    [Fact]
    public void Constructor_RoutedWithoutTieBreak_Throws()
        => Assert.Throws<DomainValidationException>(() => Build(RoutingOutcomeStatus.Routed, [routed]));

    [Fact]
    public void Constructor_TieBreakWithoutSearchMode_Throws()
        => Assert.Throws<DomainValidationException>(() => Build(RoutingOutcomeStatus.NotRouted, [], [unfulfilled], tie));

    [Fact]
    public void Constructor_FallbackReasonWithExactMode_Throws()
        => Assert.Throws<DomainValidationException>(() =>
            Build(RoutingOutcomeStatus.Routed, [routed], [], tie, SearchMode.Exact, SearchFallbackReason.NodeBudgetExceeded));

    [Fact]
    public void Constructor_UndefinedStatus_Throws()
        => Assert.Throws<DomainValidationException>(() => Build((RoutingOutcomeStatus)99, [routed], [], tie, SearchMode.Exact));

    [Fact]
    public void Constructor_NullAssignment_Throws()
        => Assert.Throws<ArgumentNullException>(() => new RoutingDecision(RoutingOutcomeStatus.NotRouted, null, null, null!, null, null, null));
}
