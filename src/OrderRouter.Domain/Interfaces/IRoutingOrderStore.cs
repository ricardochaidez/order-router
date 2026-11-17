using OrderRouter.Domain.Entities.Routing;
namespace OrderRouter.Domain.Interfaces;

/// <summary>
/// Saves a routing decision (ADR-0007, FR-20). An implementation must save the order and all its rows in one
/// transaction: it either stores everything or nothing, so no <c>id</c> is ever returned that cannot be read back.
/// </summary>
public interface IRoutingOrderStore
{
    /// <summary>Saves the order.</summary>
    /// <param name="order">The order to store.</param>
    /// <param name="ct">Cancels the work.</param>
    /// <exception cref="Exception">The save failed; nothing was stored. The caller must not return an <c>id</c>.</exception>
    Task SaveAsync(StoredRoutingOrder order, CancellationToken ct = default);
}
