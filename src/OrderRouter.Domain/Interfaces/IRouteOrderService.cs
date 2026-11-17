using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Models;

namespace OrderRouter.Domain.Interfaces;

/// <summary>
/// Routes a validated order and stores the decision and returns it as stored. OR-015 provides the real implementation; the Api only
/// depends on this seam (DIP).
/// </summary>
public interface IRouteOrderService
{
    /// <summary>Routes the order.</summary>
    /// <param name="command">A validated order.</param>
    /// <param name="ct">Cancels the work.</param>
    Task<StoredRoutingOrder> RouteAsync(RouteOrderCommand command, CancellationToken ct = default);
}
