using OrderRouter.Application.Routing;
using OrderRouter.Application.Routing.Contracts;

namespace OrderRouter.Application.Interfaces;

/// <summary>
/// Routes a validated order and returns the v1 envelope. OR-015 provides the real implementation; the Api only
/// depends on this seam (DIP).
/// </summary>
public interface IRouteOrderUseCase
{
    /// <summary>Routes the order.</summary>
    /// <param name="command">A validated order.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<RouteResponse> RouteAsync(RouteOrderCommand command, CancellationToken cancellationToken);
}
