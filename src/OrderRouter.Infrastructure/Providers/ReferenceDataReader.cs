using Microsoft.EntityFrameworkCore;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Infrastructure.Interfaces;
using OrderRouter.Infrastructure.Mappers;

namespace OrderRouter.Infrastructure.Providers;

/// <summary>
/// Read-only EF Core implementation of <see cref="IReferenceDataReader"/>. No cache (ADR-0006 option A, NFR-03).
/// </summary>
/// <param name="context">The context; used with <c>AsNoTracking</c> only (AC4).</param>
public sealed class ReferenceDataReader(IOrderRouterDataContext context) : IReferenceDataReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Supplier>> GetSuppliersHandlingAsync(
        IEnumerable<Category> categories,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(categories);

        // default(Category) has no value; skip it rather than throw: it cannot match any supplier.
        var keys = categories
            .Where(category => category != default)
            .Select(category => category.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (keys.Count == 0)
        {
            return [];
        }

        // The filter only picks suppliers; Include loads their FULL category and service-area sets (AC3),
        // so OR-010 can apply ZIP and mail rules in the domain. AsSplitQuery sends one query per collection
        // instead of one joined query, which avoids a categories x service-areas cartesian product.
        // Category.Value is already normalized, so this is plain equality on the indexed category column.
        var rows = await context.Suppliers
            .AsNoTracking()
            .Where(supplier => supplier.Categories.Any(child => keys.Contains(child.Category)))
            .Include(supplier => supplier.Categories)
            .Include(supplier => supplier.ServiceAreas)
            .AsSplitQuery()
            .ToListAsync(ct);

        // Sorted here, not in SQL: PostgreSQL orders by the database collation, but the contract is ordinal,
        // so the result is deterministic whatever collation the server uses.
        return [.. rows.Select(SupplierMappingExtensions.ToDomainModel).OrderBy(supplier => supplier.Id, StringComparer.Ordinal)];
    }
}
