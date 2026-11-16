using Microsoft.Extensions.Options;
using OrderRouter.Application.Interfaces;
using OrderRouter.Application.Routing.Contracts;
using OrderRouter.Application.Routing.Storage;

namespace OrderRouter.Application.Routing;

/// <summary>
/// Reads a stored order back for its owner (FR-21, SC-31, ADR-0007). It never routes and never writes. It holds no
/// business rule: it hashes the caller, asks the reader and maps the stored rows.
/// </summary>
public sealed class GetStoredOrderUseCase(ICallerHasher callerHasher, IRoutingOrderReader reader, IOptions<RoutingOptions> routingOptions) : IGetStoredOrderUseCase
{
    /// <inheritdoc />
    public async Task<StoredOrderResponse?> GetAsync(Guid id, string? callerId, CancellationToken cancellationToken)
    {
        // Fail safe: a caller with no ID has no hash, and an order stored without a caller has a null hash that
        // matches nothing. So there is nothing to look up and the database is not touched.
        if (string.IsNullOrWhiteSpace(callerId))
        {
            return null;
        }

        // The raw caller ID never leaves this method; only the hash is compared (NFR-11).
        var order = await reader.FindAsync(id, callerHasher.Hash(callerId), cancellationToken);

        if (order is null)
        {
            return null;
        }

        // AB-05, OQ-08: the same mapper as POST, only when the flag is on. A miss (404) never builds diagnostics.
        var response = StoredOrderResponseMapper.Map(order);

        return routingOptions.Value.ExposeDiagnostics ? response with { Diagnostics = DiagnosticsMapper.Map(order) } : response;
    }
}
