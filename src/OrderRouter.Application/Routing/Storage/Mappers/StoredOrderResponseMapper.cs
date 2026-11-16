using OrderRouter.Application.Routing.Contracts;
using OrderRouter.Application.Routing.Mappers;

namespace OrderRouter.Application.Routing.Storage.Mappers;

/// <summary>
/// Rebuilds the v1 decision from a stored order (FR-21, ADR-0007). It must produce the same <c>routing</c>,
/// <c>unfulfilled_items</c> and <c>errors</c> as <see cref="RouteResponseMapper"/> did on POST. It holds no business
/// rule and never routes: it only reshapes what was stored.
/// </summary>
public static class StoredOrderResponseMapper
{
    /// <summary>Maps the stored order.</summary>
    /// <param name="order">The stored order.</param>
    /// <exception cref="ArgumentOutOfRangeException">The stored status is not one this mapper knows (fail safe: no guessed value).</exception>
    /// <exception cref="InvalidOperationException">A stored row is inconsistent (for example a routed line with no category).</exception>
    public static StoredOrderResponse Map(StoredRoutingOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        var status = order.Status switch
        {
            "routed" => RoutingStatus.Routed,
            "partially_routed" => RoutingStatus.PartiallyRouted,
            "not_routed" => RoutingStatus.NotRouted,
            _ => throw new ArgumentOutOfRangeException(nameof(order), order.Status, "Unknown stored routing status."),
        };

        var unfulfilled = order.Items
            .Where(item => item.ReasonCode is not null)
            .Select(item => new UnfulfilledItem(
                item.ProductCode,
                item.Quantity,
                item.Category,
                item.ReasonCode!,
                item.Message ?? throw new InvalidOperationException("A stored unfulfilled line has no message.")))
            .ToArray();

        return new StoredOrderResponse
        {
            Id = order.Id,
            OrderId = order.OrderId,
            CustomerZip = order.CustomerZip,
            MailOrder = order.MailOrder,
            CreatedAt = order.CreatedAt,
            Items = [.. order.Items.Select(item => new RequestedItem(item.ProductCode, item.Quantity))],
            Status = status,
            Routing = GroupBySupplier(order.Items),
            UnfulfilledItems = unfulfilled,
            Warnings = order.Warnings,
            Errors = [.. unfulfilled.Select(item => item.Message)],
        };
    }

    // FR-15: groups follow the supplier's first line, items keep request order (the stored items are in line order).
    private static SupplierRouting[] GroupBySupplier(IReadOnlyList<StoredOrderItem> items)
        => [.. items
            .Where(item => item.SupplierId is not null)
            .GroupBy(item => item.SupplierId!, StringComparer.Ordinal)
            .Select(group => new SupplierRouting(
                group.Key,
                group.First().SupplierName ?? throw new InvalidOperationException("A stored routed line has no supplier name."),
                [.. group.Select(item => new RoutedItem(
                    item.ProductCode,
                    item.Quantity,
                    item.Category ?? throw new InvalidOperationException("A stored routed line has no category."),
                    item.FulfillmentMode ?? throw new InvalidOperationException("A stored routed line has no fulfillment mode.")))]))];
}
