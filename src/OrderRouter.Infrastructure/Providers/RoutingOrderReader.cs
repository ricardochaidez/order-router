using Microsoft.EntityFrameworkCore;
using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Infrastructure.Interfaces;
using OrderRouter.Infrastructure.Mappers;

namespace OrderRouter.Infrastructure.Providers;

/// <summary>
/// Reads a stored routing order with EF Core (ADR-0007, FR-21). The id and the caller hash are filtered in the same
/// <c>WHERE</c>, so another caller's order and a missing one both come back as <see langword="null"/> (SC-31).
/// It only reads the order tables and never joins reference data, so a re-seed cannot change what is returned.
/// </summary>
/// <param name="context">The request-scoped database context.</param>
public sealed class RoutingOrderReader(IOrderRouterDataContext context) : IRoutingOrderReader
{
    /// <inheritdoc />
    public async Task<StoredRoutingOrder?> FindAsync(Guid id, string callerHash, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerHash);

        // A stored NULL hash never equals a value in SQL, so an order stored without a caller is never found.
        var row = await context.RoutingOrders
            .AsNoTracking()
            .AsSplitQuery()
            .Include(order => order.Items.OrderBy(item => item.LineNumber))
            .Include(order => order.Warnings.OrderBy(warning => warning.Ordinal))
            .SingleOrDefaultAsync(order => order.Id == id && order.CallerHash == callerHash, ct);

        return row is null ? null : row.ToDomainModel();
    }
}
