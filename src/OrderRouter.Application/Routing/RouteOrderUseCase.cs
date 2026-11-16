using OrderRouter.Application.Routing.Contracts;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Domain.Routing;
using OrderRouter.Domain.Routing.Experiments;
using OrderRouter.Domain.Routing.Policies;

namespace OrderRouter.Application.Routing;

/// <summary>
/// Orchestrates one routing request: resolves products and loads suppliers (the only I/O), then hands the
/// pure rules to <see cref="OrderRoutingService"/> and maps the decision to the v1 envelope. It holds no
/// business rule (the Domain has all of them, ADR-0001) and only reads reference data (OR-006).
/// </summary>
public sealed class RouteOrderUseCase(
    ICapabilityResolver capabilityResolver,
    IReferenceDataReader referenceDataReader,
    IRoutingPolicyFactory policyFactory,
    OrderRoutingService routingService) : IRouteOrderUseCase
{
    /// <inheritdoc />
    public async Task<RouteResponse> RouteAsync(RouteOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // One batch lookup per request (OR-006). A code missing from the result is unknown, never an error.
        var resolutions = await capabilityResolver.ResolveAsync(command.Lines.Select(line => line.ProductCode), cancellationToken);

        var lines = command.Lines
            .Select(line => new OrderLine(
                line.ProductCode,
                line.Quantity,
                resolutions.TryGetValue(line.ProductCode, out var resolution) && resolution.TryGetCategory(out var category) ? category : null))
            .ToArray();

        var categories = lines.Where(line => line.Category is not null).Select(line => line.Category!.Value).Distinct().ToArray();

        // AC6: with no known product there is nothing to look up, so the supplier read is skipped.
        IReadOnlyList<Supplier> suppliers = categories.Length == 0
            ? []
            : await referenceDataReader.GetSuppliersHandlingAsync(categories, cancellationToken);

        // TEMPORARY until OR-016: every request runs on the control arms.
        var policy = policyFactory.Create(ExperimentAssignment.AllControl);

        var decision = routingService.Route(new OrderToRoute(command.CustomerZip, command.MailOrder, lines), suppliers, policy);

        return RouteResponseMapper.Map(command.OrderId, decision);
    }
}
