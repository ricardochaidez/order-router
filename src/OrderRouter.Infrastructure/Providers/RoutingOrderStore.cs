using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Infrastructure.Interfaces;
using OrderRouter.Infrastructure.Mappers;

namespace OrderRouter.Infrastructure.Providers;

/// <summary>
/// Saves a stored routing order with EF Core (ADR-0007). One <c>SaveChangesAsync</c> call writes the order, its
/// items and its warnings in a single database transaction, so a failure leaves no partial rows (FR-20, OR-027 AC7).
/// It only inserts into the order tables; reference data stays read-only for the API (ADR-0002).
/// </summary>
/// <param name="context">The request-scoped database context.</param>
public sealed class RoutingOrderStore(IOrderRouterDataContext context) : IRoutingOrderStore
{
    /// <inheritdoc />
    public async Task SaveAsync(StoredRoutingOrder order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        context.RoutingOrders.Add(order.ToEntity());

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch
        {
            // The failed graph stays tracked otherwise; detach so the scoped context is clean if it is ever reused.
            context.ChangeTracker.Clear();

            throw;
        }
    }
}
