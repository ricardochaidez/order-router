using Microsoft.Extensions.Options;
using OrderRouter.Application.Interfaces;
using OrderRouter.Application.Routing.Contracts;
using OrderRouter.Application.Routing.Experiments;
using OrderRouter.Application.Routing.Logging;
using OrderRouter.Application.Routing.Storage;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Domain.Routing;

namespace OrderRouter.Application.Routing;

/// <summary>
/// Orchestrates one routing request: resolves products and loads suppliers (the only I/O), then hands the
/// pure rules to <see cref="OrderRoutingService"/> and maps the decision to the v1 envelope. It then stores the
/// decision before answering (FR-20, ADR-0007) and writes the decision log event (AB-04). It holds no business rule (the Domain has all of them, ADR-0001) and
/// only reads reference data (OR-006); the only thing it writes is the new stored order.
/// </summary>
public sealed class RouteOrderUseCase(
    ICapabilityResolver capabilityResolver,
    IReferenceDataReader referenceDataReader,
    IExperimentAssigner experimentAssigner,
    IRoutingPolicyFactory policyFactory,
    OrderRoutingService routingService,
    ICallerHasher callerHasher,
    IRoutingOrderStore orderStore,
    IRoutingDecisionLog decisionLog,
    TimeProvider timeProvider,
    IOptions<RoutingOptions> routingOptions) : IRouteOrderUseCase
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

        // SC-25, SC-26: the arms come from the port (reloadable configuration today), never from routing code.
        var assignment = experimentAssigner.Assign(new AssignmentContext(command.CallerId, command.CustomerZip, command.OrderId));
        var policy = policyFactory.Create(assignment);

        var decision = routingService.Route(new OrderToRoute(command.CustomerZip, command.MailOrder, lines), suppliers, policy);

        var response = RouteResponseMapper.Map(command.OrderId, decision);

        // FR-20, SC-29: every valid POST is stored under a new server-generated UUIDv7, retries included. The save
        // happens before the answer, so an id is never returned for an order that cannot be read back. A failed save
        // throws and the host turns it into a 500 with no id. The raw caller ID is hashed here and never stored
        // (NFR-11); a caller with no ID is stored with a null hash, so nobody can read that order back (fail safe).
        var id = Guid.CreateVersion7();
        var callerHash = command.CallerId is { } callerId ? callerHasher.Hash(callerId) : null;

        var stored = StoredRoutingOrderFactory.Create(id, command, callerHash, timeProvider.GetUtcNow(), decision, response);

        await orderStore.SaveAsync(stored, cancellationToken);

        // AB-04, ADR-0005, ADR-0007: the decision event is written only after the order is saved, with its id, so the
        // log never shows an order that does not exist. The raw caller ID is not an input of the entry (NFR-11).
        decisionLog.Record(RoutingDecisionLogEntryFactory.Create(id, command.OrderId, callerHash, command.CustomerZip.Value, decision));

        // AB-05, OQ-08: diagnostics are built from the record just stored (the same mapper GET uses) and only when the
        // flag is on. Off, the field stays null and is omitted. What is stored and logged does not depend on the flag.
        return response with
        {
            Id = id,
            Diagnostics = routingOptions.Value.ExposeDiagnostics ? DiagnosticsMapper.Map(stored) : null,
        };
    }
}
