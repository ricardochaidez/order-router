using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderRouter.Api.Mappers;
using OrderRouter.Api.ViewModels.Response;
using OrderRouter.Domain.Entities.Experiments;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Helpers;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.Models;
using OrderRouter.Domain.Sections;
using OrderRouter.Domain.Services.Policies;
using OrderRouter.Domain.Services;
using OrderRouter.Tests.TestData;

namespace OrderRouter.Tests.Domain.Services;

public class RouteOrderServiceTests
{
    private static readonly CancellationToken token = CancellationToken.None;

    private readonly FakeCatalog catalog = new();
    private readonly FakeReader reader = new();
    private readonly FakeAssigner assigner = new();
    private readonly CapturingPolicyFactory policyFactory = new();
    private readonly FakeHasher hasher = new();
    private readonly List<string> calls = [];
    private readonly FakeStore store;
    private readonly FakeLog log;
    private static readonly DateTimeOffset now = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
    private List<ItemDependencyRuleOptions> dependencyRules = [];

    public RouteOrderServiceTests()
    {
        store = new FakeStore(calls);
        log = new FakeLog(calls);
    }

    // The service returns the stored order; these tests assert on the v1 response, so the adapter applies the Api mapper
    // exactly as the controller does.
    private ResponseAdapter UseCase()
        => new(new RouteOrderService(
            catalog,
            reader,
            assigner,
            policyFactory,
            new OrderRoutingService(
                new SupplierEligibility(ZipRange.Parse("00100-99999")),
                new CountingPlanSearch()),
            new DependencyWarningService(
                Options.Create(new ItemDependencyOptions { Rules = dependencyRules }),
                NullLogger<DependencyWarningService>.Instance),
            hasher,
            store,
            log,
            new FixedClock(now)));

    private sealed class ResponseAdapter(RouteOrderService service)
    {
        public async Task<RouteResponseViewModel> RouteAsync(RouteOrderCommand command, CancellationToken ct)
            => (await service.RouteAsync(command, ct)).ToRouteViewModel();
    }

    private static RouteOrderCommand Command(params string[] codes)
        => RouteOrderCommand.Create("ORD-1", "10015", false, [.. codes.Select(code => new RouteOrderLine(code, 1))]);

    [Fact]
    public async Task RouteAsync_ResolvesAllCodesInOneCall()
    {
        catalog.Known["A"] = new Category("cane");
        catalog.Known["B"] = new Category("cane");
        reader.Suppliers.Add(new Supplier("S1", "S1", [new Category("cane")], new ServiceArea([ZipRange.Parse("10000-10099")]), 8m, false));

        var response = await UseCase().RouteAsync(Command("A", "B", "A"), token);

        Assert.Equal(1, catalog.BatchCalls);
        Assert.Equal(["a", "b"], catalog.LastKeys);
        Assert.Equal(RoutingStatus.Routed, response.Status);
        Assert.Equal(3, Assert.Single(response.Routing).Items.Count);
    }

    [Fact]
    public async Task RouteAsync_CodeMissingFromCatalog_IsUnknown()
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
        catalog.Known["A"] = new Category("cane");
        catalog.Known["B"] = new Category("cane");
        catalog.Known["C"] = new Category("walker");

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
        catalog.Known["A"] = new Category("cane");
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

    // The arms, reasons and search mode are stored for analysis; the response never carries them.
    [Fact]
    public async Task RouteAsync_StoresTheArmsReasonsAndSearchMode()
    {
        catalog.Known["A"] = new Category("cane");
        reader.Suppliers.Add(new Supplier("S1", "S1", [new Category("cane")], new ServiceArea([ZipRange.Parse("10000-10099")]), 8m, false));
        assigner.Result = new ExperimentAssignment(
            new ExperimentArms(ExperimentArm.Control, ExperimentArm.Treatment, ExperimentArm.Control, ExperimentArm.Control),
            RoutingParameters.Default,
            new ExperimentReasons(ExposureReason.Disabled, ExposureReason.Bucketed, ExposureReason.NotTargeted, ExposureReason.Disabled),
            "v-test");

        await UseCase().RouteAsync(Command("A"), token);

        var saved = Assert.Single(store.Saved);
        var arms = JsonNode.Parse(saved.ExperimentArmsJson)!;
        Assert.Equal("control", (string?)arms["exp1_consolidation_vs_quality"]!["arm"]);
        Assert.Equal("disabled", (string?)arms["exp1_consolidation_vs_quality"]!["reason"]);
        Assert.Equal("treatment", (string?)arms["exp2_unrated_suppliers"]!["arm"]);
        Assert.Equal("bucketed", (string?)arms["exp2_unrated_suppliers"]!["reason"]);
        Assert.Equal("not_targeted", (string?)arms["exp3_partial_routing"]!["reason"]);
        Assert.Equal("exact", saved.SearchMode);
    }

    // No search ran, so there is no search mode, but the arms are still stored.
    [Fact]
    public async Task RouteAsync_NoKnownProduct_StoresArmsWithoutSearchMode()
    {
        await UseCase().RouteAsync(Command("UNKNOWN"), token);

        var saved = Assert.Single(store.Saved);
        Assert.Equal(4, JsonNode.Parse(saved.ExperimentArmsJson)!.AsObject().Count);
        Assert.Null(saved.SearchMode);
    }

    // A hospital bed with a local supplier, and a patient lift nobody delivers to 10015.
    private void BedRoutableLiftNot()
    {
        catalog.Known["BED"] = new Category("hospital bed");
        catalog.Known["LIFT"] = new Category("patient lift");
        reader.Suppliers.Add(new Supplier("S1", "S1", [new Category("hospital bed")], new ServiceArea([ZipRange.Parse("10000-10099")]), 8m, false));
    }

    private static ItemDependencyRuleOptions BedNeedsLift() => new() { Category = "hospital bed", Requires = ["patient lift"] };

    // OR-020 AC1: the warning is stored with the order and returned in the response.
    [Fact]
    public async Task RouteAsync_PartialRouteMatchingARule_StoresAndReturnsTheWarning()
    {
        BedRoutableLiftNot();
        policyFactory.PartialRouting = true;
        dependencyRules = [BedNeedsLift()];

        var response = await UseCase().RouteAsync(Command("BED", "LIFT"), token);

        Assert.Equal(RoutingStatus.PartiallyRouted, response.Status);
        var warning = Assert.Single(response.Warnings);
        Assert.Equal("DEPENDENT_ITEM_UNFULFILLED", warning.Code);
        Assert.Equal(["LIFT"], warning.UnfulfilledProductCodes);
        Assert.Equal(["BED"], warning.AffectedProductCodes);
        Assert.Equal(warning.Message, Assert.Single(Assert.Single(store.Saved).Warnings).Message);
        Assert.Equal(1, catalog.CategoryCalls);
    }

    // OR-020 AC2.
    [Fact]
    public async Task RouteAsync_PartialRouteWithNoRules_HasNoWarningsAndSkipsTheCategoryLookup()
    {
        BedRoutableLiftNot();
        policyFactory.PartialRouting = true;

        var response = await UseCase().RouteAsync(Command("BED", "LIFT"), token);

        Assert.Equal(RoutingStatus.PartiallyRouted, response.Status);
        Assert.Empty(response.Warnings);
        Assert.Equal(0, catalog.CategoryCalls);
    }

    // FR-18: under all-or-nothing nothing is routed, so nothing can be affected.
    [Fact]
    public async Task RouteAsync_NotRoutedWithRules_HasNoWarningsAndSkipsTheCategoryLookup()
    {
        BedRoutableLiftNot();
        dependencyRules = [BedNeedsLift()];

        var response = await UseCase().RouteAsync(Command("BED", "LIFT"), token);

        Assert.Equal(RoutingStatus.NotRouted, response.Status);
        Assert.Empty(response.Warnings);
        Assert.Equal(0, catalog.CategoryCalls);
    }

    // OR-020 AC4: the rule names a category the catalog does not have, so it is skipped and the request succeeds.
    [Fact]
    public async Task RouteAsync_RuleNamingAnUnknownCategory_IsSkipped()
    {
        BedRoutableLiftNot();
        policyFactory.PartialRouting = true;
        dependencyRules = [new() { Category = "hospital bed", Requires = ["patient lift", "bed rail"] }];

        var response = await UseCase().RouteAsync(Command("BED", "LIFT"), token);

        Assert.Equal(RoutingStatus.PartiallyRouted, response.Status);
        Assert.Empty(response.Warnings);
    }

    // OR-020 AC6: rules only add warnings; the routing is the same.
    [Fact]
    public async Task RouteAsync_SameOrderWithAndWithoutRules_RoutesTheSame()
    {
        BedRoutableLiftNot();
        policyFactory.PartialRouting = true;

        await UseCase().RouteAsync(Command("BED", "LIFT"), token);
        dependencyRules = [BedNeedsLift()];
        await UseCase().RouteAsync(Command("BED", "LIFT"), token);

        var (without, with) = (store.Saved[0], store.Saved[1]);
        Assert.Equal(without.Items, with.Items);
        Assert.Equal(without.Status, with.Status);
        Assert.Empty(without.Warnings);
        Assert.Single(with.Warnings);
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

        public Task SaveAsync(StoredRoutingOrder order, CancellationToken ct)
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

        // EXP-3 treatment rules, for the dependency-warning tests that need a partial route.
        public bool PartialRouting { get; set; }

        public RoutingPolicy Create(ExperimentAssignment assignment)
        {
            Assignments.Add(assignment);

            // Control rules, but carrying the assignment it was given, so the decision (and the stored order) shows it.
            var control = new RoutingPolicyFactory(TestTieBreakers.Treatment()).Create(ExperimentAssignment.AllControl);

            return new RoutingPolicy(control.Rating, control.Selection, PartialRouting ? PartialRoutingPolicy.Instance : control.Unfulfillable, control.TieBreaker, assignment);
        }
    }

    private sealed class FakeCatalog : IProductCatalog
    {
        public Dictionary<string, Category> Known { get; } = [];

        public int BatchCalls { get; private set; }

        public List<string> LastKeys { get; private set; } = [];

        public Task<IReadOnlyList<Product>> FindByKeysAsync(IReadOnlyCollection<ProductCodeKey> keys, CancellationToken ct)
        {
            BatchCalls++;
            LastKeys = [.. keys.Select(key => key.Value)];

            // Matches by key like the real catalog, so the use case goes through ProductCodeLookup end to end.
            IReadOnlyList<Product> result =
            [
                .. Known
                    .Where(entry => ProductCodeKey.TryCreate(entry.Key, out var key) && keys.Contains(key))
                    .Select(entry => new Product(entry.Key, entry.Key, entry.Value)),
            ];

            return Task.FromResult(result);
        }

        public int CategoryCalls { get; private set; }

        public Task<IReadOnlySet<Category>> FindExistingCategoriesAsync(IReadOnlyCollection<Category> categories, CancellationToken ct)
        {
            CategoryCalls++;
            IReadOnlySet<Category> result = categories.Where(Known.ContainsValue).ToHashSet();

            return Task.FromResult(result);
        }
    }

    private sealed class FakeReader : IReferenceDataReader
    {
        public List<Supplier> Suppliers { get; } = [];

        public int Calls { get; private set; }

        public List<Category> LastCategories { get; private set; } = [];

        public Task<IReadOnlyList<Supplier>> GetSuppliersHandlingAsync(IEnumerable<Category> categories, CancellationToken ct)
        {
            Calls++;
            LastCategories = [.. categories];

            return Task.FromResult<IReadOnlyList<Supplier>>(Suppliers);
        }
    }
}
