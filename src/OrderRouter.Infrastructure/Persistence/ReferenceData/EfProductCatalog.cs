using Microsoft.EntityFrameworkCore;
using OrderRouter.Domain.Common;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.Infrastructure.Persistence.ReferenceData;

/// <summary>
/// Read-only EF Core implementation of <see cref="IProductCatalog"/>. It reads straight from PostgreSQL with no cache
/// (ADR-0006 option A, NFR-03) and sends one query per batch. It only fetches and maps: what a key means and what to do
/// with no match or two matches is <see cref="ProductCodeLookup"/>'s job, so those rules are unit tested without a database.
/// </summary>
/// <param name="context">The context; used with <c>AsNoTracking</c> only (AC4).</param>
public sealed class EfProductCatalog(OrderRouterDbContext context) : IProductCatalog
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Product>> FindByKeysAsync(
        IReadOnlyCollection<ProductCodeKey> keys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);

        // Nothing to look up: skip the round trip.
        if (keys.Count == 0)
        {
            return [];
        }

        var values = keys.Select(key => key.Value).Distinct(StringComparer.Ordinal).ToList();

        // Npgsql translates Contains + ToLower to `lower(product_code) = ANY(@values)`: a parameterized
        // equality, so '%' and '_' in a code are never treated as wildcards (unlike ILIKE).
        // Stored codes contain no whitespace (guarded by a test), so lower() alone matches ProductCodeKey.
        var rows = await context.Products
            .AsNoTracking()
            .Where(product => values.Contains(product.ProductCode.ToLower()))
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToDomain)];
    }

    // The Product constructor stays the only place invariants are checked. A row it rejects is never skipped or
    // repaired (NFR-10): it throws with the product code.
    private static Product ToDomain(ProductRow row)
    {
        try
        {
            return new Product(row.ProductCode, row.Name, new Category(row.Category));
        }
        catch (DomainValidationException exception)
        {
            throw new InvalidOperationException(
                $"Stored product \"{row.ProductCode}\" is invalid and cannot be used for routing: {exception.Message}",
                exception);
        }
    }
}
