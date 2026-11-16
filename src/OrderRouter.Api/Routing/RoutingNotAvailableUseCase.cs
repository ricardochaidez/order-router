using OrderRouter.Application.Routing;
using OrderRouter.Application.Routing.Contracts;

namespace OrderRouter.Api.Routing;

/// <summary>
/// Temporary placeholder until OR-015 implements routing. It throws so a valid request returns 500 instead of a
/// success-looking body: nothing may claim to be routed when it was not (fail safe). OR-015 replaces this.
/// </summary>
internal sealed class RoutingNotAvailableUseCase : IRouteOrderUseCase
{
    /// <inheritdoc />
    public Task<RouteResponse> RouteAsync(RouteOrderCommand command, CancellationToken cancellationToken)
        => throw new NotSupportedException("Routing is not implemented yet (OR-015).");
}
