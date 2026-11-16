using OrderRouter.Application.Routing.Storage;

namespace OrderRouter.Infrastructure.Persistence.RoutingOrders;

/// <summary>
/// Saves a stored routing order with EF Core (ADR-0007). One <c>SaveChangesAsync</c> call writes the order, its
/// items and its warnings in a single database transaction, so a failure leaves no partial rows (FR-20, OR-027 AC7).
/// It only inserts into the order tables; reference data stays read-only for the API (ADR-0002).
/// </summary>
/// <param name="context">The request-scoped database context.</param>
public sealed class EfRoutingOrderStore(OrderRouterDbContext context) : IRoutingOrderStore
{
    /// <inheritdoc />
    public async Task SaveAsync(StoredRoutingOrder order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        context.RoutingOrders.Add(new RoutingOrderRow
        {
            Id = order.Id,
            OrderId = order.OrderId,
            CallerHash = order.CallerHash,
            CustomerZip = order.CustomerZip,
            MailOrder = order.MailOrder,
            Status = order.Status,
            Feasible = order.Feasible,
            ExperimentArms = order.ExperimentArmsJson,
            ConfigVersion = order.ConfigVersion,
            SearchMode = order.SearchMode,
            CreatedAt = order.CreatedAt,
            Items =
            [
                .. order.Items.Select(item => new RoutingOrderItemRow
                {
                    LineNumber = item.LineNumber,
                    ProductCode = item.ProductCode,
                    Quantity = item.Quantity,
                    Category = item.Category,
                    SupplierId = item.SupplierId,
                    SupplierName = item.SupplierName,
                    FulfillmentMode = item.FulfillmentMode,
                    ReasonCode = item.ReasonCode,
                    Message = item.Message,
                }),
            ],
            Warnings =
            [
                .. order.Warnings.Select((warning, index) => new RoutingOrderWarningRow
                {
                    Ordinal = index + 1,
                    Code = warning.Code,
                    Message = warning.Message,
                    UnfulfilledProductCodes = [.. warning.UnfulfilledProductCodes],
                    AffectedProductCodes = [.. warning.AffectedProductCodes],
                }),
            ],
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The failed graph stays tracked otherwise; detach so the scoped context is clean if it is ever reused.
            context.ChangeTracker.Clear();

            throw;
        }
    }
}
