using Microsoft.EntityFrameworkCore;
using OrderRouter.Application.Interfaces;
using OrderRouter.Application.Routing.Contracts;
using OrderRouter.Application.Routing.Storage;

namespace OrderRouter.Infrastructure.Persistence.RoutingOrders;

/// <summary>
/// Reads a stored routing order with EF Core (ADR-0007, FR-21). The id and the caller hash are filtered in the same
/// <c>WHERE</c>, so another caller's order and a missing one both come back as <see langword="null"/> (SC-31).
/// It only reads the order tables and never joins reference data, so a re-seed cannot change what is returned.
/// </summary>
/// <param name="context">The request-scoped database context.</param>
public sealed class EfRoutingOrderReader(OrderRouterDbContext context) : IRoutingOrderReader
{
    /// <inheritdoc />
    public async Task<StoredRoutingOrder?> FindAsync(Guid id, string callerHash, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerHash);

        // A stored NULL hash never equals a value in SQL, so an order stored without a caller is never found.
        var row = await context.RoutingOrders
            .AsNoTracking()
            .AsSplitQuery()
            .Include(order => order.Items.OrderBy(item => item.LineNumber))
            .Include(order => order.Warnings.OrderBy(warning => warning.Ordinal))
            .SingleOrDefaultAsync(order => order.Id == id && order.CallerHash == callerHash, cancellationToken);

        return row is null ? null : ToStoredOrder(row);
    }

    private static StoredRoutingOrder ToStoredOrder(RoutingOrderRow row)
        => new(
            row.Id,
            row.OrderId,
            row.CallerHash,
            row.CustomerZip,
            row.MailOrder,
            row.Status,
            row.Feasible,
            row.ExperimentArms,
            row.ConfigVersion,
            row.SearchMode,
            row.CreatedAt,
            [
                .. row.Items.Select(item => new StoredOrderItem(
                    item.LineNumber,
                    item.ProductCode,
                    item.Quantity,
                    item.Category,
                    item.SupplierId,
                    item.SupplierName,
                    item.FulfillmentMode,
                    item.ReasonCode,
                    item.Message)),
            ],
            [
                .. row.Warnings.Select(warning => new RoutingWarning(
                    warning.Code,
                    warning.Message,
                    [.. warning.UnfulfilledProductCodes],
                    [.. warning.AffectedProductCodes])),
            ]);
}
