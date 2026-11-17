using OrderRouter.Domain.Entities.Experiments;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Helpers;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.Models;

namespace OrderRouter.Domain.Services;

/// <summary>
/// Orchestrates one routing request: resolves products and loads suppliers (the only I/O), then hands the
/// pure rules to <see cref="OrderRoutingService"/> and returns the stored decision. It stores the
/// decision before answering (FR-20, ADR-0007) and writes the decision log event (AB-04). It holds no business rule (the Domain has all of them, ADR-0001) and
/// only reads reference data (OR-006); the only thing it writes is the new stored order.
/// </summary>
public sealed class RouteOrderService(
    IProductCatalog productCatalog,
    IReferenceDataReader referenceDataReader,
    IExperimentAssigner experimentAssigner,
    IRoutingPolicyFactory policyFactory,
    OrderRoutingService routingService,
    DependencyWarningService dependencyWarnings,
    ICallerHasher callerHasher,
    IRoutingOrderStore orderStore,
    IRoutingDecisionLog decisionLog,
    TimeProvider timeProvider) : IRouteOrderService
{
    /// <inheritdoc />
    public async Task<StoredRoutingOrder> RouteAsync(RouteOrderCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // One batch lookup per request (OR-006). The matching rules are the Domain's; the catalog only fetches.
        // A code missing from the result is unknown, never an error.
        var lookup = ProductCodeLookup.For(command.Lines.Select(line => line.ProductCode));
        var resolutions = lookup.Resolve(await productCatalog.FindByKeysAsync(lookup.Keys, ct));

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
            : await referenceDataReader.GetSuppliersHandlingAsync(categories, ct);

        // SC-25, SC-26: the arms come from the port (reloadable configuration today), never from routing code.
        var assignment = experimentAssigner.Assign(new AssignmentContext(command.CallerId, command.CustomerZip, command.OrderId));
        var policy = policyFactory.Create(assignment);

        var decision = routingService.Route(new OrderToRoute(command.CustomerZip, command.MailOrder, lines), suppliers, policy);

        // FR-18, OR-020: dependency warnings are read off the finished decision, so they can never change it (DR-04).
        // Only a partial route with rules configured needs the category lookup, so every other request skips the I/O.
        IReadOnlyList<RoutingWarning> warnings = dependencyWarnings.AppliesTo(decision)
            ? dependencyWarnings.WarningsFor(
                decision,
                await productCatalog.FindExistingCategoriesAsync(dependencyWarnings.RuleCategories, ct))
            : [];

        // FR-20, SC-29: every valid POST is stored under a new server-generated UUIDv7, retries included. The save
        // happens before the answer, so an id is never returned for an order that cannot be read back. A failed save
        // throws and the host turns it into a 500 with no id. The raw caller ID is hashed here and never stored
        // (NFR-11); a caller with no ID is stored with a null hash, so nobody can read that order back (fail safe).
        var id = Guid.CreateVersion7();
        var callerHash = command.CallerId is { } callerId ? callerHasher.Hash(callerId) : null;

        var stored = StoredRoutingOrderFactory.Create(id, command, callerHash, timeProvider.GetUtcNow(), decision, warnings);

        await orderStore.SaveAsync(stored, ct);

        // AB-04, ADR-0005, ADR-0007: the decision event is written only after the order is saved, with its id, so the
        // log never shows an order that does not exist. The raw caller ID is not an input of the entry (NFR-11).
        decisionLog.Record(RoutingDecisionLogEntryFactory.Create(id, command.OrderId, callerHash, command.CustomerZip.Value, decision));

        // The stored order is the answer: the Api maps it to the response, and GET maps the same record (AB-05, ADR-0004).
        return stored;
    }
}
