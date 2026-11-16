using OrderRouter.Application.Routing;
using OrderRouter.Application.Routing.Contracts;
using OrderRouter.Application.Routing.Mappers;
using OrderRouter.Domain.Eligibility;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Domain.Routing;
using OrderRouter.Domain.Routing.Experiments;
using OrderRouter.Domain.Routing.PlanSearch;
using OrderRouter.Domain.Routing.Policies;
using OrderRouter.Domain.Routing.TieBreaking;
using OrderRouter.UnitTests.Domain.Routing.TieBreaking;
using static OrderRouter.UnitTests.Domain.Routing.TieBreaking.TieBreakTestData;

namespace OrderRouter.UnitTests.Application.Routing.Mappers;

public class RouteResponseMapperTests
{
    private static readonly TieBreakResult tie = new UniformRandomTieBreaker(new FixedRandomSource(0)).Pick([Plan("S1")]);

    private static Supplier Supplier(string id) => new(id, $"Name {id}", [new Category("cane")], new ServiceArea([ZipRange.Parse("10000-10099")]), 8m, true);

    private static RoutedLine Line(string code, string supplier, FulfillmentMode mode = FulfillmentMode.Local, int quantity = 1)
        => new(code, quantity, new Category("cane"), Supplier(supplier), mode);

    private static RoutingDecision Decision(RoutingOutcomeStatus status, RoutedLine[] routed, UnfulfilledLine[] notRouted, bool searched = true)
        => new(status, routed, notRouted, ExperimentAssignment.AllControl, searched ? tie : null, searched ? SearchMode.Exact : null, null);

    [Fact]
    public void Map_Routed_GroupsBySupplierInFirstLineOrderAndKeepsRequestOrderInside()
    {
        // AC4, FR-15
        var response = RouteResponseMapper.Map(
            "ORD-1",
            Decision(RoutingOutcomeStatus.Routed, [Line("A", "S2"), Line("B", "S1"), Line("C", "S2", FulfillmentMode.Mail, 3)], []));

        Assert.Equal("ORD-1", response.OrderId);
        Assert.Equal(["S2", "S1"], response.Routing.Select(g => g.SupplierId));
        Assert.Equal("Name S2", response.Routing[0].SupplierName);
        Assert.Equal(["A", "C"], response.Routing[0].Items.Select(i => i.ProductCode));
        Assert.Equal(new RoutedItem("C", 3, "cane", "mail"), response.Routing[0].Items[1]);
        Assert.Equal("local", response.Routing[0].Items[0].FulfillmentMode);
        Assert.Null(response.Id);
    }

    [Theory]
    [InlineData(RoutingOutcomeStatus.Routed, RoutingStatus.Routed, true)]
    [InlineData(RoutingOutcomeStatus.PartiallyRouted, RoutingStatus.PartiallyRouted, false)]
    [InlineData(RoutingOutcomeStatus.NotRouted, RoutingStatus.NotRouted, false)]
    public void Map_Status_FeasibleIsTrueOnlyForRouted(RoutingOutcomeStatus status, RoutingStatus expected, bool feasible)
    {
        // AC7
        var unfulfilled = new UnfulfilledLine("U", 1, null, UnfulfilledLine.UnknownProductCode);
        var decision = status switch
        {
            RoutingOutcomeStatus.Routed => Decision(status, [Line("A", "S1")], []),
            RoutingOutcomeStatus.PartiallyRouted => Decision(status, [Line("A", "S1")], [unfulfilled]),
            _ => Decision(status, [], [unfulfilled], false),
        };

        var response = RouteResponseMapper.Map("ORD", decision);

        Assert.Equal(expected, response.Status);
        Assert.Equal(feasible, response.Feasible);
    }

    [Fact]
    public void Map_PartiallyRouted_ShowsRoutedAndUnfulfilledItemsAndErrors()
    {
        // EXP-3 treatment, SC-05
        var response = RouteResponseMapper.Map(
            "ORD",
            Decision(
                RoutingOutcomeStatus.PartiallyRouted,
                [Line("A", "S1")],
                [new UnfulfilledLine("NOPE", 2, null, UnfulfilledLine.UnknownProductCode)]));

        Assert.Equal(RoutingStatus.PartiallyRouted, response.Status);
        Assert.False(response.Feasible);
        Assert.Equal("A", Assert.Single(Assert.Single(response.Routing).Items).ProductCode);
        var item = Assert.Single(response.UnfulfilledItems);
        Assert.Equal("NOPE", item.ProductCode);
        Assert.Equal([item.Message], response.Errors);
        Assert.Empty(response.Warnings);
    }

    [Fact]
    public void Map_UnknownProduct_HasNullCategoryAndMatchingError()
    {
        var response = RouteResponseMapper.Map(
            "ORD",
            Decision(RoutingOutcomeStatus.NotRouted, [], [new UnfulfilledLine("NOPE", 2, null, UnfulfilledLine.UnknownProductCode)], false));

        var item = Assert.Single(response.UnfulfilledItems);
        Assert.Equal(new UnfulfilledItem("NOPE", 2, null, "UNKNOWN_PRODUCT", "Product code 'NOPE' was not found in the catalog."), item);
        Assert.Equal([item.Message], response.Errors);
        Assert.Empty(response.Routing);
    }

    [Fact]
    public void Map_NoSupplierReasons_UseTheDocumentedMessages()
    {
        var cpap = new Category("cpap");

        var response = RouteResponseMapper.Map(
            "ORD",
            Decision(
                RoutingOutcomeStatus.NotRouted,
                [],
                [new("A", 1, cpap, "NO_SUPPLIER_FOR_CATEGORY_IN_AREA"), new("B", 1, cpap, "NO_SUPPLIER_FOR_CATEGORY")]));

        Assert.Equal("cpap", response.UnfulfilledItems[0].Category);
        Assert.Equal(
            ["No supplier delivers 'cpap' to the delivery ZIP.", "No mail-order supplier handles 'cpap'."],
            response.Errors);
        Assert.Equal(response.UnfulfilledItems.Select(i => i.Message), response.Errors);
    }

    [Fact]
    public void Map_Routed_WarningsAndErrorsAreEmptyAndNeverNull()
    {
        var response = RouteResponseMapper.Map("ORD", Decision(RoutingOutcomeStatus.Routed, [Line("A", "S1")], []));

        Assert.NotNull(response.Warnings);
        Assert.Empty(response.Warnings);
        Assert.Empty(response.Errors);
        Assert.Empty(response.UnfulfilledItems);
    }

    [Fact]
    public void Map_UnknownReasonCode_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            RouteResponseMapper.Map("ORD", Decision(RoutingOutcomeStatus.NotRouted, [], [new("A", 1, null, "SOMETHING_NEW")], false)));

    [Fact]
    public void Map_NullDecision_Throws()
        => Assert.Throws<ArgumentNullException>(() => RouteResponseMapper.Map("ORD", null!));
}
