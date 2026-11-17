using OrderRouter.Domain.Entities.Eligibility;
using OrderRouter.Domain.Entities.Experiments;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Services.Policies;
using OrderRouter.Domain.Services;
using OrderRouter.Tests.TestData;

namespace OrderRouter.Tests.Domain.Services;

public class OrderRoutingServiceTests
{
    private static readonly ZipRange nationwide = ZipRange.Parse("00100-99999");
    private static readonly ZipCode zip = ZipCode.Parse("10015");
    private static readonly RoutingPolicy control = new RoutingPolicyFactory(TestTieBreakers.Treatment()).Create(ExperimentAssignment.AllControl);

    private static readonly RoutingPolicy partial = new RoutingPolicyFactory(TestTieBreakers.Treatment()).Create(
        new ExperimentAssignment(
            new ExperimentArms(ExperimentArm.Control, ExperimentArm.Control, ExperimentArm.Treatment, ExperimentArm.Control), RoutingParameters.Default));

    private static Supplier Supplier(string id, string[] categories, string range = "10000-10099", bool canMail = false, decimal? rating = 8m)
        => new(id, id, categories.Select(c => new Category(c)), new ServiceArea([ZipRange.Parse(range)]), rating, canMail);

    private static OrderLine Known(string code, string category, int quantity = 1) => new(code, quantity, new Category(category));

    private static OrderLine Unknown(string code) => new(code, 1, null);

    private static OrderToRoute Order(bool mail, params OrderLine[] lines) => new(zip, mail, lines);

    private static OrderRoutingService Service(CountingPlanSearch? search = null)
        => new(new SupplierEligibility(nationwide), search ?? new CountingPlanSearch());

    // The random tie-breaker exists only as the EXP-4 treatment, so every random-source test uses that arm.
    private static RoutingPolicy RandomTieWith(FixedRandomSource random)
        => new RoutingPolicyFactory(new UniformRandomTieBreaker(random)).Create(new ExperimentAssignment(
            new ExperimentArms(ExperimentArm.Control, ExperimentArm.Control, ExperimentArm.Control, ExperimentArm.Treatment), RoutingParameters.Default));

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

    // Every bit of the 64-bit category mask is in use, including the top one, and the greedy path covers them all.
    [Fact]
    public void Route_ExactlyMaxCategories_IsRoutedThroughGreedy()
    {
        var categories = Enumerable.Range(0, PlanSearchRequest.MaxCategories).Select(index => $"c{index:D2}").ToArray();

        var decision = Service().Route(
            Order(false, [.. categories.Select(category => Known($"P-{category}", category))]),
            [.. categories.Select(category => Supplier($"S-{category}", [category]))],
            control);

        Assert.Equal(RoutingOutcomeStatus.Routed, decision.Status);
        Assert.Equal(PlanSearchRequest.MaxCategories, decision.RoutedLines.Select(line => line.Supplier.Id).Distinct().Count());
        Assert.All(decision.RoutedLines, line => Assert.Equal($"S-{line.Category}", line.Supplier.Id));
        Assert.Equal(SearchMode.Greedy, decision.SearchMode);
    }

    // Fail safe: even partial routing holds the whole order back, without a search, and every line keeps its own reason.
    [Fact]
    public void Route_MoreThanMaxCategories_HoldsTheWholeOrderBackWithoutSearching()
    {
        var categories = Enumerable.Range(0, PlanSearchRequest.MaxCategories + 1).Select(index => $"c{index:D2}").ToArray();
        var search = new CountingPlanSearch();

        var decision = Service(search).Route(
            Order(false, [Unknown("NOPE"), Known("GAP", "unserved"), .. categories.Select(category => Known($"P-{category}", category))]),
            [.. categories.Select(category => Supplier($"S-{category}", [category]))],
            partial);

        Assert.Equal(RoutingOutcomeStatus.NotRouted, decision.Status);
        Assert.Empty(decision.RoutedLines);
        Assert.Null(decision.TieBreak);
        Assert.Null(decision.SearchMode);
        Assert.Equal(0, search.CallCount);
        Assert.Equal(["NOPE", "GAP", .. categories.Select(category => $"P-{category}")], decision.Unfulfilled.Select(line => line.ProductCode));
        Assert.Equal(UnfulfilledLine.UnknownProductCode, decision.Unfulfilled[0].ReasonCode);
        Assert.Equal("NO_SUPPLIER_FOR_CATEGORY_IN_AREA", decision.Unfulfilled[1].ReasonCode);
        Assert.All(decision.Unfulfilled.Skip(2), line => Assert.Equal(UnfulfilledLine.TooManyCategories, line.ReasonCode));
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
    public void Route_OneUnknownLineUnderPartialRouting_RoutesTheRestAndListsTheUnknownLine()
    {
        // EXP-3 treatment, SC-05
        var decision = Service().Route(
            Order(false, Known("A", "cane"), Unknown("NOPE")),
            [Supplier("S1", ["cane"])],
            partial);

        Assert.Equal(RoutingOutcomeStatus.PartiallyRouted, decision.Status);
        Assert.Equal("A", Assert.Single(decision.RoutedLines).ProductCode);
        var line = Assert.Single(decision.Unfulfilled);
        Assert.Equal(("NOPE", "UNKNOWN_PRODUCT"), (line.ProductCode, line.ReasonCode));
    }

    [Fact]
    public void Route_NoSupplierInAreaUnderPartialRouting_RoutesTheRestAndListsTheLine()
    {
        var decision = Service().Route(
            Order(false, Known("A", "cpap"), Known("C", "cane")),
            [Supplier("S1", ["cane"])],
            partial);

        Assert.Equal(RoutingOutcomeStatus.PartiallyRouted, decision.Status);
        Assert.Equal(["C"], decision.RoutedLines.Select(l => l.ProductCode));
        var line = Assert.Single(decision.Unfulfilled);
        Assert.Equal(("A", "NO_SUPPLIER_FOR_CATEGORY_IN_AREA"), (line.ProductCode, line.ReasonCode));
    }

    [Fact]
    public void Route_NothingRoutableUnderPartialRouting_IsNotRouted()
    {
        var decision = Service().Route(Order(false, Unknown("X"), Known("A", "cpap")), [Supplier("S1", ["cane"])], partial);

        Assert.Equal(RoutingOutcomeStatus.NotRouted, decision.Status);
        Assert.Empty(decision.RoutedLines);
        Assert.Equal(2, decision.Unfulfilled.Count);
    }

    [Fact]
    public void Route_FullyRoutableUnderPartialRouting_IsRouted()
    {
        var decision = Service().Route(Order(false, Known("A", "cane")), [Supplier("S1", ["cane"])], partial);

        Assert.Equal(RoutingOutcomeStatus.Routed, decision.Status);
        Assert.Empty(decision.Unfulfilled);
    }

    [Fact]
    public void Route_PartialOrderUnderControl_IsStillNotRouted()
    {
        // SC-10: the control arm is unchanged.
        var decision = Service().Route(
            Order(false, Known("A", "cane"), Unknown("NOPE")),
            [Supplier("S1", ["cane"])],
            control);

        Assert.Equal(RoutingOutcomeStatus.NotRouted, decision.Status);
        Assert.Empty(decision.RoutedLines);
    }

    [Fact]
    public void Route_AllLinesUnknown_NeverSearchesOrTieBreaks()
    {
        var search = new CountingPlanSearch();
        var random = new FixedRandomSource(0);

        var decision = Service(search).Route(Order(false, Unknown("X"), Unknown("Y")), [], RandomTieWith(random));

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
    public void Route_UnratedSupplierUnderColdStart_GetsThePriorAsEffectiveRating()
    {
        var search = new CountingPlanSearch();
        var assignment = new ExperimentAssignment(
            new ExperimentArms(ExperimentArm.Control, ExperimentArm.Treatment, ExperimentArm.Control, ExperimentArm.Control), RoutingParameters.Default);

        Service(search).Route(
            Order(false, Known("A", "cane")),
            [Supplier("S1", ["cane"], rating: null)],
            new RoutingPolicyFactory(TestTieBreakers.Treatment()).Create(assignment));

        Assert.Equal(6.5m, Assert.Single(search.LastRequest!.Candidates).EffectiveRating);
    }

    [Fact]
    public void Route_PassesBandAndMaxExtraShipmentsIntoTheRequest()
    {
        var search = new CountingPlanSearch();
        var assignment = new ExperimentAssignment(ExperimentArms.AllControl, new RoutingParameters(similarityBand: 0.25m));
        var policy = new RoutingPolicyFactory(TestTieBreakers.Treatment()).Create(assignment);

        Service(search).Route(Order(false, Known("A", "cane")), [Supplier("S1", ["cane"])], policy);

        Assert.Equal(0.25m, search.LastRequest!.SimilarityBand);
        Assert.Equal(policy.Selection.MaxExtraShipments, search.LastRequest.MaxExtraShipments);
    }

    [Fact]
    public void Route_FullTieUnderExp4Treatment_UsesTheRandomSource()
    {
        var random = new FixedRandomSource(1);

        var decision = Service().Route(
            Order(false, Known("A", "cane")),
            [Supplier("S1", ["cane"]), Supplier("S2", ["cane"])],
            RandomTieWith(random));

        Assert.Equal(1, random.CallCount);
        Assert.True(decision.TieBreak!.HasTie);
        Assert.Equal("S2", Assert.Single(decision.RoutedLines).Supplier.Id);
    }

    [Fact]
    public void Route_FullTieUnderCore_ChoosesTheLowestSupplierIdWithoutRandomness()
    {
        var random = new FixedRandomSource(1);
        var core = new RoutingPolicyFactory(new UniformRandomTieBreaker(random)).Create(ExperimentAssignment.AllControl);

        var decision = Service().Route(
            Order(false, Known("A", "cane")),
            [Supplier("S2", ["cane"]), Supplier("S1", ["cane"])],
            core);

        Assert.Equal(0, random.CallCount);
        Assert.Equal(2, decision.TieBreak!.TiedCandidates.Count);
        Assert.Equal("S1", Assert.Single(decision.RoutedLines).Supplier.Id);
    }

    [Fact]
    public void Route_NoTie_DoesNotUseTheRandomSource()
    {
        var random = new FixedRandomSource(0);

        var decision = Service().Route(
            Order(false, Known("A", "cane")),
            [Supplier("S1", ["cane"], rating: 9m), Supplier("S2", ["cane"], rating: 5m)],
            RandomTieWith(random));

        Assert.Equal(0, random.CallCount);
        Assert.False(decision.TieBreak!.HasTie);
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
