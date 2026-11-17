using OrderRouter.Api.Mappers;
using OrderRouter.Api.ViewModels.Response;
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

namespace OrderRouter.Tests.Api.Mappers;

public class RoutingOrderMappingExtensionsStoredTests
{
    private static readonly Guid id = Guid.CreateVersion7();
    private static readonly DateTimeOffset now = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly TieBreakResult tie = new UniformRandomTieBreaker(new FixedRandomSource(0)).Pick([Plan("S1")]);

    private static Supplier Supplier(string id) => new(id, $"Name {id}", [new Category("cane")], new ServiceArea([ZipRange.Parse("10000-10099")]), 8m, true);

    private static RoutedLine Routed(string code, string supplier, FulfillmentMode mode = FulfillmentMode.Local, int quantity = 1)
        => new(code, quantity, new Category("cane"), Supplier(supplier), mode);

    private static UnfulfilledLine Unknown(string code, int quantity = 1) => new(code, quantity, null, UnfulfilledLine.UnknownProductCode);

    private static RouteOrderCommand Command(params (string Code, int Quantity)[] lines)
        => RouteOrderCommand.Create("ORD-1", "10015", false, [.. lines.Select(line => new RouteOrderLine(line.Code, line.Quantity))]);

    private static RoutingDecision Decision(RoutingOutcomeStatus status, RoutedLine[] routed, UnfulfilledLine[] notRouted)
        => new(status, routed, notRouted, ExperimentAssignment.AllControl, tie, SearchMode.Exact, null);

    // Builds the POST response and the stored order from the same decision, then maps the stored order back.
    private static (RouteResponseViewModel Post, StoredOrderResponseViewModel Get) RoundTrip(RouteOrderCommand command, RoutingDecision decision)
    {
        var stored = StoredRoutingOrderFactory.Create(id, command, "hash", now, decision);
        var post = RoutingOrderMappingExtensions.ToRouteViewModel(stored, false);

        return (post, RoutingOrderMappingExtensions.ToViewModel(stored, false));
    }

    [Fact]
    public void Map_RoutedOrder_RebuildsTheSameRoutingAsPost()
    {
        var command = Command(("A", 1), ("B", 1), ("C", 3));

        var (post, get) = RoundTrip(command, Decision(RoutingOutcomeStatus.Routed, [Routed("A", "S2"), Routed("B", "S1"), Routed("C", "S2", FulfillmentMode.Mail, 3)], []));

        Assert.Equal(post.Routing, get.Routing, SupplierRoutingComparer.Instance);
        Assert.Equal(post.Status, get.Status);
        Assert.True(get.Feasible);
        Assert.Equal(id, get.Id);
        Assert.Equal("ORD-1", get.OrderId);
        Assert.Equal("10015", get.CustomerZip);
        Assert.False(get.MailOrder);
        Assert.Equal(now, get.CreatedAt);
    }

    [Fact]
    public void Map_PartiallyRouted_MatchesPostRoutingUnfulfilledItemsAndErrors()
    {
        var command = Command(("A", 1), ("NOPE", 2));

        var (post, get) = RoundTrip(command, Decision(RoutingOutcomeStatus.PartiallyRouted, [Routed("A", "S1")], [Unknown("NOPE", 2)]));

        Assert.Equal(post.Routing, get.Routing, SupplierRoutingComparer.Instance);
        Assert.Equal(post.UnfulfilledItems, get.UnfulfilledItems);
        Assert.Equal(post.Errors, get.Errors);
        Assert.False(get.Feasible);
        Assert.Equal(RoutingStatus.PartiallyRouted, get.Status);
    }

    // AC3: the order is held back by all-or-nothing, so no line is routed, but every requested line is listed.
    [Fact]
    public void Map_HeldBackByAllOrNothing_ListsEveryLineAndRoutesNone()
    {
        var command = Command(("A", 1), ("NOPE", 1), ("B", 4));

        var (_, get) = RoundTrip(command, Decision(RoutingOutcomeStatus.NotRouted, [], [Unknown("NOPE")]));

        Assert.Equal([new RequestedItemResponseViewModel("A", 1), new RequestedItemResponseViewModel("NOPE", 1), new RequestedItemResponseViewModel("B", 4)], get.Items);
        Assert.Empty(get.Routing);
        Assert.Equal(["NOPE"], get.UnfulfilledItems.Select(item => item.ProductCode));
        Assert.Equal(RoutingStatus.NotRouted, get.Status);
    }

    [Fact]
    public void Map_DuplicateProductCodes_KeepRequestOrderAndSupplierGroupsFollowTheFirstLine()
    {
        var command = Command(("A", 1), ("B", 1), ("A", 2));

        var (post, get) = RoundTrip(command, Decision(RoutingOutcomeStatus.Routed, [Routed("A", "S2"), Routed("B", "S1"), Routed("A", "S2", quantity: 2)], []));

        Assert.Equal([new RequestedItemResponseViewModel("A", 1), new RequestedItemResponseViewModel("B", 1), new RequestedItemResponseViewModel("A", 2)], get.Items);
        Assert.Equal(["S2", "S1"], get.Routing.Select(group => group.SupplierId));
        Assert.Equal([1, 2], get.Routing[0].Items.Select(item => item.Quantity));
        Assert.Equal(post.Routing, get.Routing, SupplierRoutingComparer.Instance);
    }

    [Fact]
    public void Map_Warnings_AreCarriedOver()
    {
        var warning = new RoutingWarning("DEPENDENCY", "Needs B.", ["B"], ["A"]);
        var order = Stored("routed", warnings: [warning]);

        var get = RoutingOrderMappingExtensions.ToViewModel(order, false);

        Assert.Equal(new RoutingWarningResponseViewModel(warning.Code, warning.Message, warning.UnfulfilledProductCodes, warning.AffectedProductCodes), Assert.Single(get.Warnings));
    }

    [Fact]
    public void Map_NoRowsOfAKind_CollectionsAreEmptyNeverNull()
    {
        var get = RoutingOrderMappingExtensions.ToViewModel(Stored("not_routed", items: []), false);

        Assert.NotNull(get.Items);
        Assert.Empty(get.Items);
        Assert.Empty(get.Routing);
        Assert.Empty(get.UnfulfilledItems);
        Assert.Empty(get.Warnings);
        Assert.Empty(get.Errors);
    }

    [Fact]
    public void Map_UnknownStatus_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => RoutingOrderMappingExtensions.ToViewModel(Stored("sideways"), false));

    [Fact]
    public void Map_RoutedLineWithoutCategory_Throws()
    {
        var order = Stored("routed", items: [new StoredOrderItem(1, "A", 1, null, "S1", "Name S1", "local", null, null)]);

        Assert.Throws<InvalidOperationException>(() => RoutingOrderMappingExtensions.ToViewModel(order, false));
    }

    [Fact]
    public void Map_NullOrder_Throws()
        => Assert.Throws<ArgumentNullException>(() => RoutingOrderMappingExtensions.ToViewModel(null!, false));

    private static StoredRoutingOrder Stored(string status, IReadOnlyList<StoredOrderItem>? items = null, IReadOnlyList<RoutingWarning>? warnings = null)
        => new(id, "ORD-1", "hash", "10015", false, status, status == "routed", "{}", "cfg", "exact", now, items ?? [], warnings ?? []);

    // SupplierRoutingResponseViewModel holds a list, so record equality alone would compare list references.
    private sealed class SupplierRoutingComparer : IEqualityComparer<SupplierRoutingResponseViewModel>
    {
        public static SupplierRoutingComparer Instance { get; } = new();

        public bool Equals(SupplierRoutingResponseViewModel? x, SupplierRoutingResponseViewModel? y)
            => x is not null && y is not null
                && x.SupplierId == y.SupplierId && x.SupplierName == y.SupplierName && x.Items.SequenceEqual(y.Items);

        public int GetHashCode(SupplierRoutingResponseViewModel obj) => obj.SupplierId.GetHashCode(StringComparison.Ordinal);
    }
}
