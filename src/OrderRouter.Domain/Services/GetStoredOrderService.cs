using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Interfaces;

namespace OrderRouter.Domain.Services;

/// <summary>
/// Reads a stored order back for its owner (FR-21, SC-31, ADR-0007). It never routes and never writes. It holds no
/// business rule: it hashes the caller and asks the reader.
/// </summary>
public sealed class GetStoredOrderService(ICallerHasher callerHasher, IRoutingOrderReader reader) : IGetStoredOrderService
{
    /// <inheritdoc />
    public async Task<StoredRoutingOrder?> GetAsync(Guid id, string? callerId, CancellationToken ct = default)
    {
        // Fail safe: a caller with no ID has no hash, and an order stored without a caller has a null hash that
        // matches nothing. So there is nothing to look up and the database is not touched.
        if (string.IsNullOrWhiteSpace(callerId))
        {
            return null;
        }

        // The raw caller ID never leaves this method; only the hash is compared (NFR-11).
        return await reader.FindAsync(id, callerHasher.Hash(callerId), ct);
    }
}
