using OrderRouter.Domain.Entities.Routing;
namespace OrderRouter.Domain.Interfaces;

/// <summary>
/// Reads a stored routing order back (ADR-0007, FR-21). It is separate from <see cref="IRoutingOrderStore"/> so the
/// write path does not depend on a read method (ISP). Ownership is part of the lookup, not a later check: the order
/// is found by its id <b>and</b> the caller's hash in one query (SC-31).
/// </summary>
public interface IRoutingOrderReader
{
    /// <summary>Finds the order of this caller.</summary>
    /// <param name="id">The stored order id.</param>
    /// <param name="callerHash">The hash of the authenticated caller, as stored on POST.</param>
    /// <param name="ct">Cancels the work.</param>
    /// <returns>
    /// The stored order, or <see langword="null"/> when there is no such order <b>or</b> it belongs to another caller.
    /// The two cases are deliberately indistinguishable (SC-31), so existence is never revealed.
    /// </returns>
    Task<StoredRoutingOrder?> FindAsync(Guid id, string callerHash, CancellationToken ct = default);
}
