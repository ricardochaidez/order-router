using OrderRouter.Application.Routing;
using OrderRouter.Application.Routing.Contracts;
using OrderRouter.Application.Routing.Experiments;
using OrderRouter.Domain.Eligibility;
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

    private RouteOrderUseCase UseCase()
        => new(
            resolver,
            reader,
            assigner,
            policyFactory,
            new OrderRoutingService(
                new SupplierEligibility(ZipRange.Parse("00100-99999")),
                new CountingPlanSearch(),
                new UniformRandomTieBreaker(new FixedRandomSource(0))));

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

    [Fact]
    public async Task RouteAsync_NullCommand_Throws()
        => await Assert.ThrowsAsync<ArgumentNullException>(() => UseCase().RouteAsync(null!, token));

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

            return new RoutingPolicyFactory().Create(ExperimentAssignment.AllControl);
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
