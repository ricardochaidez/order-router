using OrderRouter.Application.Routing.Contracts;

namespace OrderRouter.Application.Interfaces;

/// <summary>
/// Reads a stored order for the caller who created it (FR-21). The Api only depends on this seam (DIP).
/// </summary>
public interface IGetStoredOrderUseCase
{
    /// <summary>Gets the order.</summary>
    /// <param name="id">The stored order id.</param>
    /// <param name="callerId">The authenticated caller ID, or <see langword="null"/> when the token has none.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// The order, or <see langword="null"/> when it does not exist, belongs to another caller, or the caller has no ID.
    /// All three are the same outcome (SC-31).
    /// </returns>
    Task<StoredOrderResponse?> GetAsync(Guid id, string? callerId, CancellationToken cancellationToken);
}
