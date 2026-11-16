using OrderRouter.Domain.Eligibility;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Domain.Routing;
using OrderRouter.Domain.Routing.Experiments;
using OrderRouter.Domain.Routing.PlanSearch;
using OrderRouter.Domain.Routing.Policies;
using OrderRouter.Domain.Routing.TieBreaking;
using OrderRouter.UnitTests.Domain.Routing.TieBreaking;

namespace OrderRouter.UnitTests.Domain.Routing;

public class OrderRoutingServiceTests
{
    private static readonly ZipRange nationwide = ZipRange.Parse("00100-99999");
    private static readonly ZipCode zip = ZipCode.Parse("10015");
    private static readonly RoutingPolicy control = new RoutingPolicyFactory().Create(ExperimentAssignment.AllControl);

    private static Supplier Supplier(string id, string[] categories, string range = "10000-10099", bool canMail = false, decimal? rating = 8m)
        => new(id, id, categories.Select(c => new Category(c)), new ServiceArea([ZipRange.Parse(range)]), rating, canMail);

    private static OrderLine Known(string code, string category, int quantity = 1) => new(code, quantity, new Category(category));

    private static OrderLine Unknown(string code) => new(code, 1, null);

    private static OrderToRoute Order(bool mail, params OrderLine[] lines) => new(zip, mail, lines);

    private static OrderRoutingService Service(CountingPlanSearch? search = null, FixedRandomSource? random = null)
        => new(new SupplierEligibility(nationwide), search ?? new CountingPlanSearch(), new UniformRandomTieBreaker(random ?? new FixedRandomSource(0)));

    [Fact]
    public void Route_AllLinesFromOneSupplier_IsRoutedWithOneShipment()
    {
        var decision = Service().Route(
            Order(false, Known("A", "wheelchair"), Known("B", "cane")),
            [Supplier("S1", ["wheelchair", "cane"])],
            control);

        Assert.Equal(RoutingOutcomeStatus.Routed, decision.Status);
        Assert.All(decision.RoutedLines, line => Assert.Equal("S1", line.Supplier.Id));
        Assert.All(decision.RoutedLines, line => Assert.Equal(FulfillmentMode.Local, line.Mode));
        Assert.Empty(decision.Unfulfilled);
        Assert.Equal(SearchMode.Exact, decision.SearchMode);
    }

    [Fact]
    public void Route_TwoSupplierPlan_KeepsRequestOrderForRoutedLines()
    {
        var decision = Service().Route(
            Order(false, Known("BED", "hospital_bed"), Known("BP", "bp_monitor"), Known("LIFT", "patient_lift")),
            [Supplier("S1", ["hospital_bed", "patient_lift"]), Supplier("S2", ["bp_monitor"])],
            control);

        Assert.Equal(["BED", "BP", "LIFT"], decision.RoutedLines.Select(l => l.ProductCode));
        Assert.Equal(["S1", "S2", "S1"], decision.RoutedLines.Select(l => l.Supplier.Id));
    }

    [Fact]
    public void Route_DuplicateAndSameCategoryLines_AreOneDemandAndOneSupplier()
    {
        // DEV-10, FR-10
        var search = new CountingPlanSearch();

        var decision = Service(search).Route(
            Order(false, Known("A", "cane"), Known("A", "cane", 2), Known("B", "cane")),
            [Supplier("S1", ["cane"]), Supplier("S2", ["cane"])],
            control);

        var demand = Assert.Single(search.LastRequest!.Demand);
        Assert.Equal(3, demand.LineCount);
        Assert.Equal(3, decision.RoutedLines.Count);
        Assert.Single(decision.RoutedLines.Select(l => l.Supplier.Id).Distinct());
        Assert.Equal([1, 2, 1], decision.RoutedLines.Select(l => l.Quantity));
    }

    [Fact]
    public void Route_OneUnknownLineUnderControl_IsNotRoutedWithUnknownProduct()
    {
        var decision = Service().Route(
            Order(false, Known("A", "cane"), Unknown("NOPE")),
            [Supplier("S1", ["cane"])],
            control);

        Assert.Equal(RoutingOutcomeStatus.NotRouted, decision.Status);
        Assert.Empty(decision.RoutedLines);
        var line = Assert.Single(decision.Unfulfilled);
        Assert.Equal(("NOPE", null, "UNKNOWN_PRODUCT"), (line.ProductCode, line.Category, line.ReasonCode));
    }

    [Fact]
    public void Route_AllLinesUnknown_NeverSearchesOrTieBreaks()
    {
        var search = new CountingPlanSearch();
        var random = new FixedRandomSource(0);

        var decision = Service(search, random).Route(Order(false, Unknown("X"), Unknown("Y")), [], control);

        Assert.Equal(0, search.CallCount);
        Assert.Equal(0, random.CallCount);
        Assert.Null(decision.TieBreak);
        Assert.Null(decision.SearchMode);
        Assert.Equal(["X", "Y"], decision.Unfulfilled.Select(l => l.ProductCode));
        Assert.Equal(RoutingOutcomeStatus.NotRouted, decision.Status);
    }

    [Theory]
    [InlineData(false, "NO_SUPPLIER_FOR_CATEGORY_IN_AREA")]
    [InlineData(true, "NO_SUPPLIER_FOR_CATEGORY")]
    public void Route_IneligibleCategory_EveryLineOfItIsUnfulfilledWithTheReason(bool mail, string expected)
    {
        var decision = Service().Route(
            Order(mail, Known("A", "cpap"), Known("B", "cpap"), Known("C", "cane")),
            [Supplier("S1", ["cane"], canMail: mail)],
            control);

        Assert.Equal(["A", "B"], decision.Unfulfilled.Select(l => l.ProductCode));
        Assert.All(decision.Unfulfilled, line => Assert.Equal(expected, line.ReasonCode));
        Assert.Equal(RoutingOutcomeStatus.NotRouted, decision.Status);
    }

    [Fact]
    public void Route_UnratedSupplier_GetsEffectiveRatingZeroFromThePolicy()
    {
        var search = new CountingPlanSearch();

        Service(search).Route(Order(false, Known("A", "cane")), [Supplier("S1", ["cane"], rating: null)], control);

        Assert.Equal(0m, Assert.Single(search.LastRequest!.Candidates).EffectiveRating);
    }

    [Fact]
    public void Route_PassesBandAndMaxExtraShipmentsIntoTheRequest()
    {
        var search = new CountingPlanSearch();
        var assignment = new ExperimentAssignment(ExperimentArms.AllControl, new RoutingParameters(similarityBand: 0.25m));
        var policy = new RoutingPolicyFactory().Create(assignment);

        Service(search).Route(Order(false, Known("A", "cane")), [Supplier("S1", ["cane"])], policy);

        Assert.Equal(0.25m, search.LastRequest!.SimilarityBand);
        Assert.Equal(policy.Selection.MaxExtraShipments, search.LastRequest.MaxExtraShipments);
    }

    [Fact]
    public void Route_FullTie_UsesTheRandomSource()
    {
        var random = new FixedRandomSource(1);

        var decision = Service(random: random).Route(
            Order(false, Known("A", "cane")),
            [Supplier("S1", ["cane"]), Supplier("S2", ["cane"])],
            control);

        Assert.Equal(1, random.CallCount);
        Assert.True(decision.TieBreak!.UsedRandom);
        Assert.Equal("S2", Assert.Single(decision.RoutedLines).Supplier.Id);
    }

    [Fact]
    public void Route_NoTie_DoesNotUseTheRandomSource()
    {
        var random = new FixedRandomSource(0);

        var decision = Service(random: random).Route(
            Order(false, Known("A", "cane")),
            [Supplier("S1", ["cane"], rating: 9m), Supplier("S2", ["cane"], rating: 5m)],
            control);

        Assert.Equal(0, random.CallCount);
        Assert.False(decision.TieBreak!.UsedRandom);
        Assert.Equal("S1", Assert.Single(decision.RoutedLines).Supplier.Id);
    }

    [Fact]
    public void Route_MailOrderSupplierOutsideTheZip_ShipsByMail()
    {
        var decision = Service().Route(
            Order(true, Known("A", "cane")),
            [Supplier("S1", ["cane"], range: "20000-20010", canMail: true)],
            control);

        Assert.Equal(FulfillmentMode.Mail, Assert.Single(decision.RoutedLines).Mode);
    }

    [Fact]
    public void Route_NoPlanDespiteDemand_Throws()
    {
        var empty = new CountingPlanSearch(new PlanSearchResult([], 0));

        Assert.Throws<InvalidOperationException>(() =>
            Service(empty).Route(Order(false, Known("A", "cane")), [Supplier("S1", ["cane"])], control));
    }

    [Fact]
    public void Route_NullArguments_Throw()
    {
        var service = Service();
        var order = Order(false, Known("A", "cane"));

        Assert.Throws<ArgumentNullException>(() => service.Route(null!, [], control));
        Assert.Throws<ArgumentNullException>(() => service.Route(order, null!, control));
        Assert.Throws<ArgumentNullException>(() => service.Route(order, [], null!));
    }
}
