using OrderRouter.Api.Mappers;
using OrderRouter.Api.ViewModels.Response;
using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Helpers;
using OrderRouter.Domain.Models;

namespace OrderRouter.Tests.TestData;

/// <summary>
/// Maps a routing decision to the POST response the way production does: the decision is stored (here, built in memory
/// by <see cref="StoredRoutingOrderFactory"/>) and the stored order is mapped by the Api. Tests that only care about
/// the response shape use it instead of repeating the command and the store.
/// </summary>
internal static class RouteResponseTestMapper
{
    private static readonly DateTimeOffset createdAt = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Stores the decision in memory and maps it.</summary>
    /// <param name="orderId">The caller's order id.</param>
    /// <param name="decision">The routing decision; its routed lines come first, then its unfulfilled lines.</param>
    public static RouteResponseViewModel Map(string orderId, RoutingDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var lines = decision.RoutedLines.Select(line => new RouteOrderLine(line.ProductCode, line.Quantity))
            .Concat(decision.Unfulfilled.Select(line => new RouteOrderLine(line.ProductCode, line.Quantity)))
            .ToArray();
        var command = RouteOrderCommand.Create(orderId, "10015", false, lines);

        return StoredRoutingOrderFactory.Create(Guid.CreateVersion7(), command, null, createdAt, decision).ToRouteViewModel();
    }
}
