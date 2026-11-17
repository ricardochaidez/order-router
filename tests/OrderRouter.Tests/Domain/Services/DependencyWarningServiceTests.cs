using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderRouter.Domain.Entities.Eligibility;
using OrderRouter.Domain.Entities.Experiments;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Models;
using OrderRouter.Domain.Sections;
using OrderRouter.Domain.Services;
using OrderRouter.Tests.TestData;
using static OrderRouter.Tests.TestData.TieBreakTestData;

namespace OrderRouter.Tests.Domain.Services;

public class DependencyWarningServiceTests
{
    private static readonly Category bed = new("hospital bed");
    private static readonly Category lift = new("patient lift");
    private static readonly Category cpap = new("cpap");
    private static readonly Category nebulizer = new("nebulizer");
    private static readonly TieBreakResult tie = new UniformRandomTieBreaker(new FixedRandomSource(0)).Pick([Plan("S1")]);
    private static readonly HashSet<Category> allKnown = [bed, lift, cpap, nebulizer];

    private readonly ListLogger logger = new();

    private DependencyWarningService Create(params ItemDependencyRuleOptions[] rules)
        => new(Options.Create(new ItemDependencyOptions { Rules = [.. rules] }), logger);

    private static ItemDependencyRuleOptions Rule(string category, params string[] requires) => new() { Category = category, Requires = [.. requires] };

    private static RoutedLine Routed(string code, Category category)
        => new(code, 1, category, new Supplier("S1", "S1", [category], new ServiceArea([ZipRange.Parse("10000-10099")]), 8m, false), FulfillmentMode.Local);

    private static UnfulfilledLine Missing(string code, Category category) => new(code, 1, category, "NO_SUPPLIER_FOR_CATEGORY_IN_AREA");

    private static RoutingDecision Partial(RoutedLine[] routed, UnfulfilledLine[] unfulfilled)
        => new(RoutingOutcomeStatus.PartiallyRouted, routed, unfulfilled, ExperimentAssignment.AllControl, tie, SearchMode.Exact, null);

    private static readonly RoutingDecision bedRoutedLiftMissing = Partial([Routed("HB-FUL-018", bed)], [Missing("PL-ELEC-043", lift)]);

    // OR-020 AC1.
    [Fact]
    public void WarningsFor_PartialRouteMatchingARule_ReturnsOneWarning()
    {
        var warnings = Create(Rule("hospital bed", "patient lift")).WarningsFor(bedRoutedLiftMissing, allKnown);

        var warning = Assert.Single(warnings);
        Assert.Equal(RoutingWarning.DependentItemUnfulfilledCode, warning.Code);
        Assert.Equal(["PL-ELEC-043"], warning.UnfulfilledProductCodes);
        Assert.Equal(["HB-FUL-018"], warning.AffectedProductCodes);
    }

    [Fact]
    public void WarningsFor_SeveralMatchingRules_ReturnsOnePerRuleInConfiguredOrder()
    {
        var decision = Partial(
            [Routed("HB-FUL-018", bed), Routed("CP-STD-031", cpap)],
            [Missing("PL-ELEC-043", lift), Missing("NB-COMP-039", nebulizer)]);

        var warnings = Create(Rule("cpap", "nebulizer"), Rule("hospital bed", "patient lift")).WarningsFor(decision, allKnown);

        Assert.Equal([["CP-STD-031"], ["HB-FUL-018"]], warnings.Select(warning => warning.AffectedProductCodes));
    }

    // OR-020 AC2.
    [Fact]
    public void WarningsFor_NoRules_ReturnsNoWarnings()
    {
        var service = Create();

        Assert.False(service.AppliesTo(bedRoutedLiftMissing));
        Assert.Empty(service.WarningsFor(bedRoutedLiftMissing, allKnown));
        Assert.Empty(service.RuleCategories);
    }

    // FR-18: only a partial route gets dependency warnings.
    [Theory]
    [InlineData(RoutingOutcomeStatus.Routed)]
    [InlineData(RoutingOutcomeStatus.NotRouted)]
    public void WarningsFor_NotAPartialRoute_ReturnsNoWarnings(RoutingOutcomeStatus status)
    {
        var decision = status is RoutingOutcomeStatus.Routed
            ? new RoutingDecision(status, [Routed("HB-FUL-018", bed)], [], ExperimentAssignment.AllControl, tie, SearchMode.Exact, null)
            : new RoutingDecision(status, [], [Missing("PL-ELEC-043", lift)], ExperimentAssignment.AllControl, null, null, null);
        var service = Create(Rule("hospital bed", "patient lift"));

        Assert.False(service.AppliesTo(decision));
        Assert.Empty(service.WarningsFor(decision, allKnown));
    }

    // OR-020 AC4: the rule is skipped, a warning is logged, and other rules still apply.
    [Fact]
    public void WarningsFor_RuleNamingAnUnknownCategory_IsSkippedAndLogged()
    {
        var service = Create(Rule("hospital bed", "patient lift", "bed rail"), Rule("cpap", "nebulizer"));
        var decision = Partial([Routed("HB-FUL-018", bed), Routed("CP-STD-031", cpap)], [Missing("PL-ELEC-043", lift), Missing("NB-COMP-039", nebulizer)]);

        var warnings = service.WarningsFor(decision, allKnown);

        Assert.Equal(["CP-STD-031"], Assert.Single(warnings).AffectedProductCodes);
        var (level, message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Equal("Item-dependency rule for 'hospital bed' was skipped: bed rail not found in the reference data.", message);
    }

    [Fact]
    public void RuleCategories_ListsEveryNamedCategoryOnce()
        => Assert.Equal([bed, lift, cpap], Create(Rule("hospital bed", "patient lift"), Rule("cpap", "patient lift")).RuleCategories);

    [Fact]
    public void Constructor_MalformedRule_Throws()
        => Assert.Throws<DomainValidationException>(() => Create(Rule("cpap", "cpap")));

    [Fact]
    public void WarningsFor_NullArguments_Throw()
    {
        var service = Create();

        Assert.Throws<ArgumentNullException>(() => service.WarningsFor(null!, allKnown));
        Assert.Throws<ArgumentNullException>(() => service.WarningsFor(bedRoutedLiftMissing, null!));
    }

    private sealed class ListLogger : ILogger<DependencyWarningService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
