using OrderRouter.Application.Routing.Contracts;
using OrderRouter.Domain.Eligibility;
using OrderRouter.Domain.Routing;
using OrderRouter.Domain.Routing.Policies;

namespace OrderRouter.Application.Routing.Mappers;

/// <summary>
/// Maps a <see cref="RoutingDecision"/> to the v1 envelope (FR-14, FR-15, FR-16, ADR-0004). It never sets
/// <c>feasible</c>: <see cref="RouteResponse"/> derives it from the status (AC7).
/// </summary>
public static class RouteResponseMapper
{
    /// <summary>Maps the decision.</summary>
    /// <param name="orderId">The caller's order id, echoed back.</param>
    /// <param name="decision">The routing decision.</param>
    /// <exception cref="ArgumentOutOfRangeException">A status or reason code is not one this mapper knows (fail safe: no guessed text).</exception>
    public static RouteResponse Map(string orderId, RoutingDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var unfulfilled = decision.Unfulfilled
            .Select(line => new UnfulfilledItem(line.ProductCode, line.Quantity, line.Category?.Value, line.ReasonCode, MessageFor(line)))
            .ToArray();

        return new RouteResponse
        {
            OrderId = orderId,
            Status = decision.Status switch
            {
                RoutingOutcomeStatus.Routed => RoutingStatus.Routed,
                RoutingOutcomeStatus.PartiallyRouted => RoutingStatus.PartiallyRouted,
                RoutingOutcomeStatus.NotRouted => RoutingStatus.NotRouted,
                _ => throw new ArgumentOutOfRangeException(nameof(decision), decision.Status, "Unknown routing status."),
            },
            Routing = GroupBySupplier(decision.RoutedLines),
            UnfulfilledItems = unfulfilled,
            Errors = [.. unfulfilled.Select(item => item.Message)],
            Warnings = [],
        };
    }

    // FR-15: groups follow the supplier's first line, items keep request order.
    private static SupplierRouting[] GroupBySupplier(IReadOnlyList<RoutedLine> lines)
        => [.. lines
            .GroupBy(line => line.Supplier.Id, StringComparer.Ordinal)
            .Select(group => new SupplierRouting(
                group.Key,
                group.First().Supplier.Name,
                [.. group.Select(line => new RoutedItem(line.ProductCode, line.Quantity, line.Category.Value, ModeText(line.Mode)))]))];

    private static string ModeText(FulfillmentMode mode)
        => mode switch
        {
            FulfillmentMode.Local => "local",
            FulfillmentMode.Mail => "mail",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown fulfillment mode."),
        };

    private static string MessageFor(UnfulfilledLine line)
        => line.ReasonCode switch
        {
            UnfulfilledLine.UnknownProductCode => $"Product code '{line.ProductCode}' was not found in the catalog.",
            "NO_SUPPLIER_FOR_CATEGORY_IN_AREA" => $"No supplier delivers '{line.Category}' to the delivery ZIP.",
            "NO_SUPPLIER_FOR_CATEGORY" => $"No mail-order supplier handles '{line.Category}'.",
            _ => throw new ArgumentOutOfRangeException(nameof(line), line.ReasonCode, "Unknown unfulfilled reason code."),
        };
}
