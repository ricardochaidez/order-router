using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderRouter.Application.Routing.Logging;
using OrderRouter.Domain.Eligibility;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Domain.Routing;
using OrderRouter.Domain.Routing.Experiments;
using OrderRouter.Domain.Routing.PlanSearch;
using OrderRouter.Domain.Routing.Policies;
using OrderRouter.Domain.Routing.TieBreaking;
using OrderRouter.Infrastructure.CallerHashing;
using OrderRouter.UnitTests.Domain.Routing.TieBreaking;
using static OrderRouter.UnitTests.Domain.Routing.TieBreaking.TieBreakTestData;

namespace OrderRouter.UnitTests.Application.Routing.Logging;

public class RoutingDecisionLogTests
{
    private static readonly Guid id = Guid.CreateVersion7();

    private static Supplier Supplier(string supplierId, decimal? rating = 8m)
        => new(supplierId, $"Name {supplierId}", [new Category("cane"), new Category("walker")], new ServiceArea([ZipRange.Parse("10000-10099")]), rating, true);

    private static RoutedLine Routed(string code, string supplierId, FulfillmentMode mode = FulfillmentMode.Local, decimal? rating = 8m)
        => new(code, 1, new Category("cane"), Supplier(supplierId, rating), mode);

    private static RoutingDecision Decision(
        RoutingOutcomeStatus status,
        RoutedLine[] routed,
        UnfulfilledLine[]? notRouted = null,
        TieBreakResult? tie = null,
        ExperimentAssignment? assignment = null,
        SearchMode? mode = SearchMode.Exact)
        => new(status, routed, notRouted ?? [], assignment ?? ExperimentAssignment.AllControl, tie, tie is null ? null : mode, null);

    private static TieBreakResult Single(string supplierId) => new UniformRandomTieBreaker(new FixedRandomSource(0)).Pick([Plan(supplierId)]);

    private static RoutingDecisionLogEntry Entry(RoutingDecision decision, string? callerHash = "abc123")
        => RoutingDecisionLogEntryFactory.Create(id, "ORD-1", callerHash, "10015", decision);

    // AC1
    [Fact]
    public void Create_RoutedOrder_HasEveryField()
    {
        var assignment = new ExperimentAssignment(
            new ExperimentArms(ExperimentArm.Control, ExperimentArm.Treatment, ExperimentArm.Control),
            RoutingParameters.Default,
            new ExperimentReasons(ExposureReason.Disabled, ExposureReason.Bucketed, ExposureReason.NotTargeted),
            "cfg-7");
        var decision = Decision(
            RoutingOutcomeStatus.Routed,
            [Routed("A", "SUP-1"), Routed("B", "SUP-1"), Routed("C", "SUP-2", FulfillmentMode.Mail, rating: null)],
            tie: Single("SUP-1"),
            assignment: assignment);

        var entry = Entry(decision);

        Assert.Equal(
            new RoutingDecisionLogEntry(
                id, "ORD-1", "abc123", "control", "disabled", "treatment", "bucketed", "control", "not_targeted",
                "SUP-1:local,SUP-2:mail", null, null, 2, 8m, 1, 0, "exact", "cfg-7", "10015"),
            entry);
    }

    // AC2
    [Fact]
    public void Create_TieBreakRan_ListsTheTiedCandidatesAndThePick()
    {
        var tie = new UniformRandomTieBreaker(new FixedRandomSource(1)).Pick([Plan("SUP-T003"), Plan("SUP-T004")]);

        var entry = Entry(Decision(RoutingOutcomeStatus.Routed, [Routed("A", "SUP-T004")], tie: tie));

        Assert.Equal("SUP-T003,SUP-T004", entry.TiedCandidates);
        Assert.Equal("SUP-T004", entry.TiePick);
    }

    [Fact]
    public void Create_NoTie_HasNoTiedCandidatesAndNoPick()
    {
        var entry = Entry(Decision(RoutingOutcomeStatus.Routed, [Routed("A", "SUP-1")], tie: Single("SUP-1")));

        Assert.Null(entry.TiedCandidates);
        Assert.Null(entry.TiePick);
    }

    [Fact]
    public void Create_PartiallyRouted_CountsTheUnfulfilledLines()
    {
        var entry = Entry(Decision(
            RoutingOutcomeStatus.PartiallyRouted,
            [Routed("A", "SUP-1")],
            [new UnfulfilledLine("X", 1, null, UnfulfilledLine.UnknownProductCode), new UnfulfilledLine("Y", 1, null, UnfulfilledLine.UnknownProductCode)],
            Single("SUP-1")));

        Assert.Equal(2, entry.UnfulfilledCount);
        Assert.Equal(1, entry.ShipmentCount);
        Assert.Equal("SUP-1:local", entry.ChosenSuppliers);
    }

    // AB-04: every outcome is logged, including not routed.
    [Fact]
    public void Create_NotRoutedWithNoSearch_HasNoPlanFields()
    {
        var entry = Entry(Decision(RoutingOutcomeStatus.NotRouted, [], [new UnfulfilledLine("X", 1, null, UnfulfilledLine.UnknownProductCode)]));

        Assert.Equal(string.Empty, entry.ChosenSuppliers);
        Assert.Equal(0, entry.ShipmentCount);
        Assert.Null(entry.PlanQuality);
        Assert.Null(entry.SearchMode);
        Assert.Equal(0, entry.UnratedChosen);
        Assert.Equal(1, entry.UnfulfilledCount);
    }

    [Fact]
    public void Create_NotRoutedAfterASearch_HasNoPlanQualityButKeepsTheSearchMode()
    {
        var entry = Entry(Decision(RoutingOutcomeStatus.NotRouted, [], [new UnfulfilledLine("X", 1, null, UnfulfilledLine.UnknownProductCode)], Single("SUP-1")));

        Assert.Null(entry.PlanQuality);
        Assert.Equal(0, entry.ShipmentCount);
        Assert.Equal("exact", entry.SearchMode);
    }

    [Fact]
    public void Create_NoCallerHash_KeepsItNull()
    {
        Assert.Null(Entry(Decision(RoutingOutcomeStatus.Routed, [Routed("A", "SUP-1")], tie: Single("SUP-1")), callerHash: null).CallerHash);
    }

    [Fact]
    public void Create_NullDecision_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RoutingDecisionLogEntryFactory.Create(id, "ORD-1", null, "10015", null!));
    }

    // The emitter: structured properties, one event, the AB-04 field names.
    [Fact]
    public void Record_Entry_WritesOneInformationEventWithTheAb04FieldNames()
    {
        var logger = new CapturingLogger();
        var entry = Entry(Decision(RoutingOutcomeStatus.Routed, [Routed("A", "SUP-1")], tie: Single("SUP-1")));

        new LoggerRoutingDecisionLog(logger).Record(entry);

        var logged = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logged.Level);
        Assert.Equal("RoutingDecisionMade", logged.EventId.Name);
        Assert.Equal(
            [
                "id", "order_id", "caller_hash", "exp1_arm", "exp1_reason", "exp2_arm", "exp2_reason", "exp3_arm", "exp3_reason",
                "chosen_suppliers", "tied_candidates", "tie_pick", "shipment_count", "plan_quality", "unrated_chosen", "unfulfilled_count",
                "search_mode", "config_version", "customer_zip",
            ],
            logged.Properties.Keys.Where(key => key != "{OriginalFormat}"));
        Assert.Equal(id, logged.Properties["id"]);
        Assert.Equal("ORD-1", logged.Properties["order_id"]);
        Assert.Equal("abc123", logged.Properties["caller_hash"]);
        Assert.Equal(1, logged.Properties["shipment_count"]);
        Assert.Equal("10015", logged.Properties["customer_zip"]);
    }

    // AC3: the raw caller ID appears nowhere, only its hash.
    [Fact]
    public void Record_CallerUser123_LogsTheHashAndNeverTheCallerId()
    {
        var logger = new CapturingLogger();
        var hash = new HmacCallerHasher(Options.Create(new CallerHashingOptions { Secret = "unit-test-secret" })).Hash("user-123");
        var entry = Entry(Decision(RoutingOutcomeStatus.Routed, [Routed("A", "SUP-1")], tie: Single("SUP-1")), callerHash: hash);

        new LoggerRoutingDecisionLog(logger).Record(entry);

        var everything = string.Join('\n', logger.Entries.Select(logged => logged.Message + string.Join(',', logged.Properties.Values)));
        Assert.DoesNotContain("user-123", everything, StringComparison.Ordinal);
        Assert.Contains(hash, everything, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_LoggerThrows_DoesNotThrow()
    {
        var entry = Entry(Decision(RoutingOutcomeStatus.Routed, [Routed("A", "SUP-1")], tie: Single("SUP-1")));

        var exception = Record.Exception(() => new LoggerRoutingDecisionLog(new ThrowingLogger()).Record(entry));

        Assert.Null(exception);
    }

    private sealed class CapturingLogger : ILogger<LoggerRoutingDecisionLog>
    {
        public List<(LogLevel Level, EventId EventId, string Message, Dictionary<string, object?> Properties)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value) : [];
            Entries.Add((logLevel, eventId, formatter(state, exception), properties));
        }
    }

    private sealed class ThrowingLogger : ILogger<LoggerRoutingDecisionLog>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => throw new InvalidOperationException("provider down");
    }
}
