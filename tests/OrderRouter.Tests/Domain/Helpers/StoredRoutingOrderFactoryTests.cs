using OrderRouter.Domain.Entities.Eligibility;
using OrderRouter.Domain.Entities.Experiments;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Helpers;
using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Models;
using OrderRouter.Domain.Services;
using OrderRouter.Tests.TestData;
using static OrderRouter.Tests.TestData.TieBreakTestData;

namespace OrderRouter.Tests.Domain.Helpers;

public class StoredRoutingOrderFactoryTests
{
    private static readonly Guid id = Guid.CreateVersion7();
    private static readonly DateTimeOffset now = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly TieBreakResult tie = new UniformRandomTieBreaker(new FixedRandomSource(0)).Pick([Plan("S1")]);

    private static Supplier Supplier(string id) => new(id, $"Name {id}", [new Category("cane")], new ServiceArea([ZipRange.Parse("10000-10099")]), 8m, true);

    private static RoutedLine Routed(string code, string supplier, FulfillmentMode mode = FulfillmentMode.Local, int quantity = 1)
        => new(code, quantity, new Category("cane"), Supplier(supplier), mode);

    private static UnfulfilledLine Unknown(string code, int quantity = 1) => new(code, quantity, null, UnfulfilledLine.UnknownProductCode);

    private static RouteOrderCommand Command(params (string Code, int Quantity)[] lines)
        => RouteOrderCommand.Create("ORD-1", "10015", true, [.. lines.Select(line => new RouteOrderLine(line.Code, line.Quantity))]);

    private static RoutingDecision Decision(RoutingOutcomeStatus status, RoutedLine[] routed, UnfulfilledLine[] notRouted, ExperimentAssignment? assignment = null, bool searched = true)
        => new(status, routed, notRouted, assignment ?? ExperimentAssignment.AllControl, searched ? tie : null, searched ? SearchMode.Exact : null, null);

    private static StoredRoutingOrder Create(RouteOrderCommand command, RoutingDecision decision, string? callerHash = "hash")
        => StoredRoutingOrderFactory.Create(id, command, callerHash, now, decision);

    [Fact]
    public void Create_RoutedOrder_CopiesTheRequestAndTheDecision()
    {
        var command = Command(("A", 2), ("B", 1));

        var order = Create(command, Decision(RoutingOutcomeStatus.Routed, [Routed("A", "S1", quantity: 2), Routed("B", "S1", FulfillmentMode.Mail)], []));

        Assert.Equal(id, order.Id);
        Assert.Equal("ORD-1", order.OrderId);
        Assert.Equal("hash", order.CallerHash);
        Assert.Equal("10015", order.CustomerZip);
        Assert.True(order.MailOrder);
        Assert.Equal("routed", order.Status);
        Assert.True(order.Feasible);
        Assert.Equal("exact", order.SearchMode);
        Assert.Equal("defaults", order.ConfigVersion);
        Assert.Equal(now, order.CreatedAt);
        Assert.Equal(
            [
                new StoredOrderItem(1, "A", 2, "cane", "S1", "Name S1", "local", null, null),
                new StoredOrderItem(2, "B", 1, "cane", "S1", "Name S1", "mail", null, null),
            ],
            order.Items);
    }

    [Fact]
    public void Create_UnknownProduct_StoresTheReasonAndTheSameMessageAsTheResponse()
    {
        var command = Command(("NOPE", 3));
        var decision = Decision(RoutingOutcomeStatus.NotRouted, [], [Unknown("NOPE", 3)], searched: false);

        var order = Create(command, decision);

        var item = Assert.Single(order.Items);
        Assert.Equal("UNKNOWN_PRODUCT", item.ReasonCode);
        Assert.Equal(RouteResponseTestMapper.Map("ORD-1", decision).UnfulfilledItems[0].Message, item.Message);
        Assert.Null(item.Category);
        Assert.Null(item.SupplierId);
        Assert.Equal("not_routed", order.Status);
        Assert.False(order.Feasible);
        Assert.Null(order.SearchMode);
    }

    [Fact]
    public void Create_PartiallyRouted_KeepsRequestOrderAcrossRoutedAndUnfulfilledLines()
    {
        var command = Command(("A", 1), ("NOPE", 1), ("B", 1));

        var order = Create(command, Decision(RoutingOutcomeStatus.PartiallyRouted, [Routed("A", "S1"), Routed("B", "S2")], [Unknown("NOPE")]));

        Assert.Equal(["A", "NOPE", "B"], order.Items.Select(item => item.ProductCode));
        Assert.Equal([1, 2, 3], order.Items.Select(item => item.LineNumber));
        Assert.Equal(["S1", null, "S2"], order.Items.Select(item => item.SupplierId));
        Assert.Equal("partially_routed", order.Status);
    }

    [Fact]
    public void Create_DuplicateProductCodes_MapEachLineToItsOwnRow()
    {
        var command = Command(("A", 1), ("A", 1), ("NOPE", 1), ("NOPE", 1));

        var order = Create(command, Decision(RoutingOutcomeStatus.PartiallyRouted, [Routed("A", "S1"), Routed("A", "S1")], [Unknown("NOPE"), Unknown("NOPE")]));

        Assert.Equal(["S1", "S1", null, null], order.Items.Select(item => item.SupplierId));
        Assert.Equal([null, null, "UNKNOWN_PRODUCT", "UNKNOWN_PRODUCT"], order.Items.Select(item => item.ReasonCode));
    }

    [Fact]
    public void Create_LineHeldBackByAllOrNothing_HasNeitherSupplierNorReason()
    {
        var command = Command(("A", 1), ("NOPE", 1));

        var order = Create(command, Decision(RoutingOutcomeStatus.NotRouted, [], [Unknown("NOPE")]));

        Assert.Equal(new StoredOrderItem(1, "A", 1, null, null, null, null, null, null), order.Items[0]);
        Assert.Equal("UNKNOWN_PRODUCT", order.Items[1].ReasonCode);
    }

    [Fact]
    public void Create_NoCallerHash_StoresNull()
    {
        var order = Create(Command(("A", 1)), Decision(RoutingOutcomeStatus.Routed, [Routed("A", "S1")], []), callerHash: null);

        Assert.Null(order.CallerHash);
    }

    [Fact]
    public void Create_ExperimentAssignment_StoresArmAndReasonPerExperiment()
    {
        var assignment = new ExperimentAssignment(
            new ExperimentArms(ExperimentArm.Control, ExperimentArm.Treatment, ExperimentArm.Control, ExperimentArm.Control),
            RoutingParameters.Default,
            new ExperimentReasons(ExposureReason.Disabled, ExposureReason.Bucketed, ExposureReason.NotTargeted, ExposureReason.Disabled),
            "v-123");

        var order = Create(Command(("A", 1)), Decision(RoutingOutcomeStatus.Routed, [Routed("A", "S1")], [], assignment));

        Assert.Equal("v-123", order.ConfigVersion);
        Assert.Equal(
            """{"exp1_consolidation_vs_quality":{"arm":"control","reason":"disabled"},"exp2_unrated_suppliers":{"arm":"treatment","reason":"bucketed"},"exp3_partial_routing":{"arm":"control","reason":"not_targeted"},"exp4_tie_breaking":{"arm":"control","reason":"disabled"}}""",
            order.ExperimentArmsJson);
    }

    private static readonly RoutingWarning warning = new(RoutingWarning.DependentItemUnfulfilledCode, "B needs X.", ["X"], ["A"]);

    [Fact]
    public void Create_NoWarningsGiven_StoresNone()
        => Assert.Empty(Create(Command(("A", 1)), Decision(RoutingOutcomeStatus.Routed, [Routed("A", "S1")], [])).Warnings);

    // FR-18: the warnings are stored with the order, so GET reads them back (ADR-0007).
    [Fact]
    public void Create_PartialRouteWithWarnings_StoresThem()
    {
        var decision = Decision(RoutingOutcomeStatus.PartiallyRouted, [Routed("A", "S1")], [Unknown("X")]);

        var order = StoredRoutingOrderFactory.Create(id, Command(("A", 1), ("X", 1)), "hash", now, decision, [warning]);

        Assert.Equal([warning], order.Warnings);
    }

    // Fail safe: the contract has warnings on partial routes only.
    [Fact]
    public void Create_WarningsOnARoutedDecision_Throws()
    {
        var decision = Decision(RoutingOutcomeStatus.Routed, [Routed("A", "S1")], []);

        Assert.Throws<InvalidOperationException>(() => StoredRoutingOrderFactory.Create(id, Command(("A", 1)), "hash", now, decision, [warning]));
    }

    [Fact]
    public void Create_DecisionDoesNotMatchTheRequest_Throws()
    {
        var command = Command(("A", 1));

        Assert.Throws<InvalidOperationException>(() => Create(command, Decision(RoutingOutcomeStatus.Routed, [Routed("A", "S1"), Routed("B", "S1")], [])));
    }
}
