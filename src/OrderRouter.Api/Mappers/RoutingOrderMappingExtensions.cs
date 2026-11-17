using OrderRouter.Api.ViewModels.Response;
using OrderRouter.Domain.Entities.Routing;

namespace OrderRouter.Api.Mappers;

/// <summary>
/// Maps a <see cref="StoredRoutingOrder"/> to the v1 response view models (FR-14, FR-15, FR-16, FR-21, ADR-0004).
/// POST and GET both start from the stored record, so the two can never disagree about the same order (ADR-0007).
/// It holds no business rule and never routes: it only reshapes what was stored.
/// </summary>
public static class RoutingOrderMappingExtensions
{
    /// <summary>Maps the stored order to the body of <c>POST /api/v1/route</c>.</summary>
    /// <param name="order">The stored order.</param>
    /// <exception cref="ArgumentOutOfRangeException">The stored status is not one this mapper knows (fail safe: no guessed value).</exception>
    /// <exception cref="InvalidOperationException">A stored row is inconsistent (for example a routed line with no category).</exception>
    public static RouteResponseViewModel ToRouteViewModel(this StoredRoutingOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        var unfulfilled = UnfulfilledItems(order);

        return new RouteResponseViewModel
        {
            Id = order.Id,
            OrderId = order.OrderId,
            Status = StatusOf(order),
            Routing = GroupBySupplier(order.Items),
            UnfulfilledItems = unfulfilled,
            Errors = [.. unfulfilled.Select(item => item.Message)],
            Warnings = Warnings(order),
        };
    }

    /// <summary>Maps the stored order to the body of <c>GET /api/v1/route/{id}</c>.</summary>
    /// <param name="order">The stored order.</param>
    /// <exception cref="ArgumentOutOfRangeException">The stored status is not one this mapper knows (fail safe: no guessed value).</exception>
    /// <exception cref="InvalidOperationException">A stored row is inconsistent (for example a routed line with no category).</exception>
    public static StoredOrderResponseViewModel ToViewModel(this StoredRoutingOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        var unfulfilled = UnfulfilledItems(order);

        return new StoredOrderResponseViewModel
        {
            Id = order.Id,
            OrderId = order.OrderId,
            CustomerZip = order.CustomerZip,
            MailOrder = order.MailOrder,
            CreatedAt = order.CreatedAt,
            Items = [.. order.Items.Select(item => new RequestedItemResponseViewModel(item.ProductCode, item.Quantity))],
            Status = StatusOf(order),
            Routing = GroupBySupplier(order.Items),
            UnfulfilledItems = unfulfilled,
            Warnings = Warnings(order),
            Errors = [.. unfulfilled.Select(item => item.Message)],
        };
    }

    private static RoutingStatus StatusOf(StoredRoutingOrder order)
        => order.Status switch
        {
            "routed" => RoutingStatus.Routed,
            "partially_routed" => RoutingStatus.PartiallyRouted,
            "not_routed" => RoutingStatus.NotRouted,
            _ => throw new ArgumentOutOfRangeException(nameof(order), order.Status, "Unknown stored routing status."),
        };

    private static UnfulfilledItemResponseViewModel[] UnfulfilledItems(StoredRoutingOrder order)
        => [.. order.Items
            .Where(item => item.ReasonCode is not null)
            .Select(item => new UnfulfilledItemResponseViewModel(
                item.ProductCode,
                item.Quantity,
                item.Category,
                item.ReasonCode!,
                item.Message ?? throw new InvalidOperationException("A stored unfulfilled line has no message.")))];

    private static RoutingWarningResponseViewModel[] Warnings(StoredRoutingOrder order)
        => [.. order.Warnings.Select(warning => new RoutingWarningResponseViewModel(
            warning.Code,
            warning.Message,
            warning.UnfulfilledProductCodes,
            warning.AffectedProductCodes))];

    // FR-15: groups follow the supplier's first line, items keep request order (the stored items are in line order).
    private static SupplierRoutingResponseViewModel[] GroupBySupplier(IReadOnlyList<StoredOrderItem> items)
        => [.. items
            .Where(item => item.SupplierId is not null)
            .GroupBy(item => item.SupplierId!, StringComparer.Ordinal)
            .Select(group => new SupplierRoutingResponseViewModel(
                group.Key,
                group.First().SupplierName ?? throw new InvalidOperationException("A stored routed line has no supplier name."),
                [.. group.Select(item => new RoutedItemResponseViewModel(
                    item.ProductCode,
                    item.Quantity,
                    item.Category ?? throw new InvalidOperationException("A stored routed line has no category."),
                    item.FulfillmentMode ?? throw new InvalidOperationException("A stored routed line has no fulfillment mode.")))]))];
}
