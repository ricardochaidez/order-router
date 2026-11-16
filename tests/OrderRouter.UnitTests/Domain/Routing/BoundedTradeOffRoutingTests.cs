using OrderRouter.Domain.Eligibility;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Domain.Routing;
using OrderRouter.Domain.Routing.Experiments;
using OrderRouter.Domain.Routing.Policies;
using OrderRouter.Domain.Routing.TieBreaking;
using OrderRouter.UnitTests.Domain.Routing.TieBreaking;

namespace OrderRouter.UnitTests.Domain.Routing;

/// <summary>EXP-1 acceptance tests through the real search, selection and factory (OR-017).</summary>
public class BoundedTradeOffRoutingTests
{
    private static readonly ZipCode zip = ZipCode.Parse("10015");
    private static readonly RoutingPolicy control = new RoutingPolicyFactory().Create(ExperimentAssignment.AllControl);

    private static Supplier Supplier(string id, decimal rating, params string[] categories)
        => new(id, id, categories.Select(c => new Category(c)), new ServiceArea([ZipRange.Parse("10000-10099")]), rating, false);

    private static OrderToRoute Order()
        => new(zip, false, [new OrderLine("A", 1, new Category("wheelchair")), new OrderLine("B", 1, new Category("cane"))]);

    private static (OrderRoutingService Service, CountingPlanSearch Search) Create()
    {
        var search = new CountingPlanSearch();

        return (new OrderRoutingService(new SupplierEligibility(ZipRange.Parse("00100-99999")), search, new UniformRandomTieBreaker(new FixedRandomSource(0))), search);
    }

    private static RoutingPolicy Treatment(decimal threshold)
        => new RoutingPolicyFactory().Create(new ExperimentAssignment(
            new ExperimentArms(ExperimentArm.Treatment, ExperimentArm.Control, ExperimentArm.Control),
            new RoutingParameters(qualityUpliftThreshold: threshold)));

    private static int Shipments(RoutingDecision decision) => decision.RoutedLines.Select(l => l.Supplier.Id).Distinct().Count();

    [Fact]
    public void Route_TreatmentBigUplift_UsesTwoShipments()
    {
        // AC1
        var (service, search) = Create();

        var decision = service.Route(Order(), [Supplier("ONE", 6m, "wheelchair", "cane"), Supplier("W", 9m, "wheelchair"), Supplier("C", 9m, "cane")], Treatment(2m));

        Assert.Equal(2, Shipments(decision));
        Assert.Equal(1, search.LastRequest!.MaxExtraShipments);
    }

    [Fact]
    public void Route_TreatmentSmallUplift_KeepsOneShipment()
    {
        // AC2
        var (service, _) = Create();

        var decision = service.Route(Order(), [Supplier("ONE", 8m, "wheelchair", "cane"), Supplier("W", 9m, "wheelchair"), Supplier("C", 9m, "cane")], Treatment(2m));

        Assert.Equal(["ONE"], decision.RoutedLines.Select(l => l.Supplier.Id).Distinct());
    }

    [Fact]
    public void Route_Control_UsesFewestShipmentsAndAsksForNoExtra()
    {
        // AC5
        var (service, search) = Create();

        var decision = service.Route(Order(), [Supplier("ONE", 6m, "wheelchair", "cane"), Supplier("W", 9m, "wheelchair"), Supplier("C", 9m, "cane")], control);

        Assert.Equal(1, Shipments(decision));
        Assert.Equal(0, search.LastRequest!.MaxExtraShipments);
    }
}
