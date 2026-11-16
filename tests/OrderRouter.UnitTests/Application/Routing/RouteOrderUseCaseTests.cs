using Microsoft.Extensions.Options;
using OrderRouter.Application.Interfaces;
using OrderRouter.Application.Routing;
using OrderRouter.Application.Routing.Contracts;
using OrderRouter.Application.Routing.Experiments;
using OrderRouter.Application.Routing.Logging;
using OrderRouter.Application.Routing.Storage;
using OrderRouter.Domain.Eligibility;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Domain.Routing;
using OrderRouter.Domain.Routing.Experiments;
using OrderRouter.Domain.Routing.Policies;
using OrderRouter.Domain.Routing.TieBreaking;
using OrderRouter.UnitTests.Domain.Routing;
using OrderRouter.UnitTests.Domain.Routing.TieBreaking;

namespace OrderRouter.UnitTests.Application.Routing;

public class RouteOrderUseCaseTests
{
    private static readonly CancellationToken token = CancellationToken.None;

    private readonly FakeResolver resolver = new();
    private readonly FakeReader reader = new();
    private readonly FakeAssigner assigner = new();
    private readonly CapturingPolicyFactory policyFactory = new();
    private readonly FakeHasher hasher = new();
    private readonly List<string> calls = [];
    private readonly FakeStore store;
    private readonly FakeLog log;
    private static readonly DateTimeOffset now = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
    private bool exposeDiagnostics;

    public RouteOrderUseCaseTests()
    {
        store = new FakeStore(calls);
        log = new FakeLog(calls);
    }

    private RouteOrderUseCase UseCase()
        => new(
            resolver,
            reader,
            assigner,
            policyFactory,
            new OrderRoutingService(
                new SupplierEligibility(ZipRange.Parse("00100-99999")),
                new CountingPlanSearch(),
                new UniformRandomTieBreaker(new FixedRandomSource(0))),
            hasher,
            store,
            log,
            new FixedClock(now),
            Options.Create(new RoutingOptions { ExposeDiagnostics = exposeDiagnostics }));

    private static RouteOrderCommand Command(params string[] codes)
        => RouteOrderCommand.Create("ORD-1", "10015", false, [.. codes.Select(code => new RouteOrderLine(code, 1))]);

    [Fact]
    public async Task RouteAsync_ResolvesAllCodesInOneCall()
    {
        resolver.Known["A"] = new Category("cane");
        resolver.Known["B"] = new Category("cane");
        reader.Suppliers.Add(new Supplier("S1", "S1", [new Category("cane")], new ServiceArea([ZipRange.Parse("10000-10099")]), 8m, false));

        var response = await UseCase().RouteAsync(Command("A", "B", "A"), token);

        Assert.Equal(1, resolver.BatchCalls);
        Assert.Equal(["A", "B", "A"], resolver.LastCodes);
        Assert.Equal(RoutingStatus.Routed, response.Status);
        Assert.Equal(3, Assert.Single(response.Routing).Items.Count);
    }

    [Fact]
    public async Task RouteAsync_CodeMissingFromResolverResult_IsUnknown()
    {
        var response = await UseCase().RouteAsync(Command("MISSING"), token);

        var item = Assert.Single(response.UnfulfilledItems);
        Assert.Equal("UNKNOWN_PRODUCT", item.ReasonCode);
        Assert.Equal(RoutingStatus.NotRouted, response.Status);
    }

    [Fact]
    public async Task RouteAsync_EverythingUnknown_DoesNotReadSuppliers()
    {
        // AC6
        var response = await UseCase().RouteAsync(Command("X", "Y"), token);

        Assert.Equal(0, reader.Calls);
        Assert.False(response.Feasible);
        Assert.Equal(2, response.UnfulfilledItems.Count);
    }

    [Fact]
    public async Task RouteAsync_KnownProducts_ReadsSuppliersOnceForTheDistinctCategories()
    {
        resolver.Known["A"] = new Category("cane");
        resolver.Known["B"] = new Category("cane");
        resolver.Known["C"] = new Category("walker");

        await UseCase().RouteAsync(Command("A", "B", "C", "UNKNOWN"), token);

        Assert.Equal(1, reader.Calls);
        Assert.Equal([new Category("cane"), new Category("walker")], reader.LastCategories.Order(Comparer<Category>.Create((a, b) => string.CompareOrdinal(a.Value, b.Value))));
    }

    [Fact]
    public async Task RouteAsync_PassesCallerZipAndOrderIdToTheAssigner()
    {
        var command = RouteOrderCommand.Create("ORD-9", "02114", false, [new RouteOrderLine("A", 1)], "portal-client");

        await UseCase().RouteAsync(command, token);

        var context = Assert.Single(assigner.Contexts);
        Assert.Equal(new AssignmentContext("portal-client", ZipCode.Parse("02114"), "ORD-9"), context);
    }

    [Fact]
    public async Task RouteAsync_NoCaller_PassesNullCallerToTheAssigner()
    {
        await UseCase().RouteAsync(Command("A"), token);

        Assert.Null(Assert.Single(assigner.Contexts).CallerId);
    }

    [Fact]
    public async Task RouteAsync_GivesThePolicyFactoryExactlyTheAssignmentTheAssignerReturned()
    {
        var assignment = new ExperimentAssignment(ExperimentArms.AllControl, new RoutingParameters(similarityBand: 0.25m), ExperimentReasons.AllDisabled, "v-test");
        assigner.Result = assignment;

        await UseCase().RouteAsync(Command("A"), token);

        Assert.Same(assignment, Assert.Single(policyFactory.Assignments));
    }

    // FR-20, SC-28: the response carries the id of the stored order.
    [Fact]
    public async Task RouteAsync_ValidOrder_StoresOnceAndReturnsTheStoredId()
    {
        resolver.Known["A"] = new Category("cane");
        reader.Suppliers.Add(new Supplier("S1", "S1", [new Category("cane")], new ServiceArea([ZipRange.Parse("10000-10099")]), 8m, false));

        var response = await UseCase().RouteAsync(Command("A"), token);

        var stored = Assert.Single(store.Saved);
        Assert.NotNull(response.Id);
        Assert.Equal(response.Id, stored.Id);
        Assert.Equal(7, stored.Id.Version);
        Assert.Equal("ORD-1", stored.OrderId);
        Assert.Equal(now, stored.CreatedAt);
        Assert.Equal("routed", stored.Status);
    }

    // AB-04: one event per stored order, written after the save, with the stored id.
    [Fact]
    public async Task RouteAsync_ValidOrder_LogsOneEventAfterTheSaveWithTheStoredId()
    {
        var response = await UseCase().RouteAsync(Command("A"), token);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(response.Id, entry.Id);
        Assert.Equal("ORD-1", entry.OrderId);
        Assert.Equal("10015", entry.CustomerZip);
        Assert.Equal(["save", "log"], calls);
    }

    // AC5: no stored order, no event.
    [Fact]
    public async Task RouteAsync_SaveFails_WritesNoEvent()
    {
        store.Failure = new InvalidOperationException("db down");

        await Assert.ThrowsAsync<InvalidOperationException>(() => UseCase().RouteAsync(Command("A"), token));

        Assert.Empty(log.Entries);
    }

    // AC3: the entry carries the hash, never the caller ID.
    [Fact]
    public async Task RouteAsync_WithCaller_LogsTheHashAndNotTheCallerId()
    {
        var command = RouteOrderCommand.Create("ORD-1", "10015", false, [new RouteOrderLine("A", 1)], "user-123");

        await UseCase().RouteAsync(command, token);

        var entry = Assert.Single(log.Entries);
        Assert.Equal("hash-of-user-123", entry.CallerHash);
        Assert.DoesNotContain("user-123", entry.ToString().Replace("hash-of-user-123", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    // The answer must not exist before the save: nothing may come back for an order that was not stored.
    [Fact]
    public async Task RouteAsync_SaveFails_ThrowsAndReturnsNoResponse()
    {
        store.Failure = new InvalidOperationException("db down");

        await Assert.ThrowsAsync<InvalidOperationException>(() => UseCase().RouteAsync(Command("A"), token));
    }

    // SC-29: every POST creates a new record.
    [Fact]
    public async Task RouteAsync_SameRequestTwice_StoresTwoOrdersWithDifferentIds()
    {
        var first = await UseCase().RouteAsync(Command("A"), token);
        var second = await UseCase().RouteAsync(Command("A"), token);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, store.Saved.Count);
    }

    // NFR-11: only the hash is stored.
    [Fact]
    public async Task RouteAsync_WithCaller_StoresTheHashAndNotTheCallerId()
    {
        var command = RouteOrderCommand.Create("ORD-1", "10015", false, [new RouteOrderLine("A", 1)], "user-123");

        await UseCase().RouteAsync(command, token);

        var stored = Assert.Single(store.Saved);
        Assert.Equal("hash-of-user-123", stored.CallerHash);
        Assert.DoesNotContain("user-123", stored.ExperimentArmsJson + stored.OrderId + stored.ConfigVersion, StringComparison.Ordinal);
    }

    // Decided with the engineer: no caller ID means a null hash, so the order can never be read back.
    [Fact]
    public async Task RouteAsync_NoCaller_StoresNullHashWithoutCallingTheHasher()
    {
        await UseCase().RouteAsync(Command("A"), token);

        Assert.Null(Assert.Single(store.Saved).CallerHash);
        Assert.Empty(hasher.Hashed);
    }

    // AB-05, AC1: the default is off, so the field is absent.
    [Fact]
    public async Task RouteAsync_FlagOffByDefault_HasNoDiagnostics()
    {
        resolver.Known["A"] = new Category("cane");

        var response = await UseCase().RouteAsync(Command("A"), token);

        Assert.False(new RoutingOptions().ExposeDiagnostics);
        Assert.Null(response.Diagnostics);
    }

    // AC2: arms and reasons of the assignment, and the search mode of the decision.
    [Fact]
    public async Task RouteAsync_FlagOn_ShowsTheArmsReasonsAndSearchMode()
    {
        exposeDiagnostics = true;
        resolver.Known["A"] = new Category("cane");
        reader.Suppliers.Add(new Supplier("S1", "S1", [new Category("cane")], new ServiceArea([ZipRange.Parse("10000-10099")]), 8m, false));
        assigner.Result = new ExperimentAssignment(
            new ExperimentArms(ExperimentArm.Control, ExperimentArm.Treatment, ExperimentArm.Control),
            RoutingParameters.Default,
            new ExperimentReasons(ExposureReason.Disabled, ExposureReason.Bucketed, ExposureReason.NotTargeted),
            "v-test");

        var response = await UseCase().RouteAsync(Command("A"), token);

        var diagnostics = Assert.IsType<Diagnostics>(response.Diagnostics);
        Assert.Equal(new ExperimentDiagnostic("control", "disabled"), diagnostics.ExperimentArms["exp1_consolidation_vs_quality"]);
        Assert.Equal(new ExperimentDiagnostic("treatment", "bucketed"), diagnostics.ExperimentArms["exp2_unrated_suppliers"]);
        Assert.Equal(new ExperimentDiagnostic("control", "not_targeted"), diagnostics.ExperimentArms["exp3_partial_routing"]);
        Assert.Equal(Assert.Single(store.Saved).SearchMode, diagnostics.SearchMode);
        Assert.Equal("exact", diagnostics.SearchMode);
    }

    // The flag only changes the body: what is stored is the same either way.
    [Fact]
    public async Task RouteAsync_FlagOn_StoresTheSameRecordAsFlagOff()
    {
        resolver.Known["A"] = new Category("cane");

        await UseCase().RouteAsync(Command("A"), token);
        exposeDiagnostics = true;
        await UseCase().RouteAsync(Command("A"), token);

        var (off, on) = (store.Saved[0], store.Saved[1]);
        Assert.Equal(off.ExperimentArmsJson, on.ExperimentArmsJson);
        Assert.Equal(off.SearchMode, on.SearchMode);
        Assert.Equal(off.ConfigVersion, on.ConfigVersion);
        Assert.Equal(off.Status, on.Status);
    }

    // Decided with the engineer: no search ran, so search_mode is omitted but the arms are still shown.
    [Fact]
    public async Task RouteAsync_FlagOnAndNoKnownProduct_ShowsArmsWithoutSearchMode()
    {
        exposeDiagnostics = true;

        var response = await UseCase().RouteAsync(Command("UNKNOWN"), token);

        var diagnostics = Assert.IsType<Diagnostics>(response.Diagnostics);
        Assert.Equal(3, diagnostics.ExperimentArms.Count);
        Assert.Null(diagnostics.SearchMode);
    }

    [Fact]
    public async Task RouteAsync_NullCommand_Throws()
        => await Assert.ThrowsAsync<ArgumentNullException>(() => UseCase().RouteAsync(null!, token));

    private sealed class FakeHasher : ICallerHasher
    {
        public List<string> Hashed { get; } = [];

        public string Hash(string callerId)
        {
            Hashed.Add(callerId);

            return $"hash-of-{callerId}";
        }
    }

    private sealed class FakeLog(List<string> calls) : IRoutingDecisionLog
    {
        public List<RoutingDecisionLogEntry> Entries { get; } = [];

        public void Record(RoutingDecisionLogEntry entry)
        {
            calls.Add("log");
            Entries.Add(entry);
        }
    }

    private sealed class FakeStore(List<string> calls) : IRoutingOrderStore
    {
        public List<StoredRoutingOrder> Saved { get; } = [];

        public Exception? Failure { get; set; }

        public Task SaveAsync(StoredRoutingOrder order, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            calls.Add("save");
            Saved.Add(order);

            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeAssigner : IExperimentAssigner
    {
        public List<AssignmentContext> Contexts { get; } = [];

        public ExperimentAssignment Result { get; set; } = ExperimentAssignment.AllControl;

        public ExperimentAssignment Assign(AssignmentContext context)
        {
            Contexts.Add(context);

            return Result;
        }
    }

    private sealed class CapturingPolicyFactory : IRoutingPolicyFactory
    {
        public List<ExperimentAssignment> Assignments { get; } = [];

        public RoutingPolicy Create(ExperimentAssignment assignment)
        {
            Assignments.Add(assignment);

            // Control rules, but carrying the assignment it was given, so the decision (and the stored order) shows it.
            var control = new RoutingPolicyFactory().Create(ExperimentAssignment.AllControl);

            return new RoutingPolicy(control.Rating, control.Selection, control.Unfulfillable, assignment);
        }
    }

    private sealed class FakeResolver : ICapabilityResolver
    {
        public Dictionary<string, Category> Known { get; } = [];

        public int BatchCalls { get; private set; }

        public List<string?> LastCodes { get; private set; } = [];

        public Task<IReadOnlyDictionary<string, CapabilityResolution>> ResolveAsync(IEnumerable<string?> productCodes, CancellationToken cancellationToken)
        {
            BatchCalls++;
            LastCodes = [.. productCodes];

            // Only known codes are returned, like a real resolver that omits nothing but tests the "missing means unknown" rule.
            IReadOnlyDictionary<string, CapabilityResolution> result = Known.ToDictionary(entry => entry.Key, entry => CapabilityResolution.Resolved(entry.Value));

            return Task.FromResult(result);
        }

        public Task<CapabilityResolution> ResolveAsync(string? productCode, CancellationToken cancellationToken)
            => throw new NotSupportedException("The use case must use the batch overload.");
    }

    private sealed class FakeReader : IReferenceDataReader
    {
        public List<Supplier> Suppliers { get; } = [];

        public int Calls { get; private set; }

        public List<Category> LastCategories { get; private set; } = [];

        public Task<IReadOnlyList<Supplier>> GetSuppliersHandlingAsync(IEnumerable<Category> categories, CancellationToken cancellationToken)
        {
            Calls++;
            LastCategories = [.. categories];

            return Task.FromResult<IReadOnlyList<Supplier>>(Suppliers);
        }
    }
}
