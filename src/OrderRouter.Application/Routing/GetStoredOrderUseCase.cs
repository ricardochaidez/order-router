using OrderRouter.Application.Routing.Contracts;
using OrderRouter.Application.Routing.Storage;

namespace OrderRouter.Application.Routing;

/// <summary>
/// Reads a stored order back for its owner (FR-21, SC-31, ADR-0007). It never routes and never writes. It holds no
/// business rule: it hashes the caller, asks the reader and maps the stored rows.
/// </summary>
public sealed class GetStoredOrderUseCase(ICallerHasher callerHasher, IRoutingOrderReader reader) : IGetStoredOrderUseCase
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

        return order is null ? null : StoredOrderResponseMapper.Map(order);
    }
}
