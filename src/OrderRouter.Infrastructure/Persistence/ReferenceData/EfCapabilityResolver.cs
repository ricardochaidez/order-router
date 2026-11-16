using Microsoft.EntityFrameworkCore;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.Infrastructure.Persistence.ReferenceData;

/// <summary>
/// Read-only EF Core implementation of <see cref="ICapabilityResolver"/>. It reads straight from PostgreSQL
/// with no cache (ADR-0006 option A, NFR-03) and sends one query per batch.
/// </summary>
/// <param name="context">The context; used with <c>AsNoTracking</c> only (AC4).</param>
public sealed class EfCapabilityResolver(OrderRouterDbContext context) : ICapabilityResolver
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, CapabilityResolution>> ResolveAsync(
        IEnumerable<string?> productCodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productCodes);

        // A null input cannot be a dictionary key; callers treat a missing code as unknown.
        var distinctInputs = productCodes.OfType<string>().Distinct(StringComparer.Ordinal).ToList();

        // Every input starts as unknown and only the database can change that (fail safe, FR-05).
        var results = distinctInputs.ToDictionary(input => input, _ => CapabilityResolution.Unknown, StringComparer.Ordinal);

        // Several spellings (" ox-port-024 ", "OX-PORT-024") can share one key; the lookup keeps all of them.
        var inputsByKey = distinctInputs
            .Select(input => ProductCodeKey.TryCreate(input, out var key) ? (Key: key.Value, Input: input) : default)
            .Where(pair => pair.Key is not null)
            .ToLookup(pair => pair.Key, pair => pair.Input, StringComparer.Ordinal);

        // Nothing valid to look up: skip the round trip.
        if (inputsByKey.Count == 0)
        {
            return results;
        }

        var keys = inputsByKey.Select(group => group.Key).ToList();

        // Npgsql translates Contains + ToLower to `lower(product_code) = ANY(@keys)`: a parameterized
        // equality, so '%' and '_' in a code are never treated as wildcards (unlike ILIKE).
        // Stored codes contain no whitespace (guarded by a test), so lower() alone matches ProductCodeKey.
        var rows = await context.Products
            .AsNoTracking()
            .Where(product => keys.Contains(product.ProductCode.ToLower()))
            .Select(product => new { product.ProductCode, product.Category })
            .ToListAsync(cancellationToken);

        // Group with the domain rule, not a second spelling of it, so the keys always line up with the inputs.
        foreach (var group in rows
            .Where(row => ProductCodeKey.TryCreate(row.ProductCode, out _))
            .GroupBy(row => ProductCodeKey.TryCreate(row.ProductCode, out var key) ? key.Value : string.Empty, StringComparer.Ordinal))
        {
            var matchedInputs = inputsByKey[group.Key];

            var matches = group.ToList();

            // Two rows for one key is corrupt data (OR-005 rejects it). Ambiguity must not route (CLAUDE.md fail safe).
            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Product code lookup is ambiguous: stored codes {string.Join(", ", matches.Select(match => $"\"{match.ProductCode}\""))} " +
                    "all match the same key.");
            }

            CapabilityResolution resolved;

            try
            {
                resolved = CapabilityResolution.Resolved(new Category(matches[0].Category));
            }
            catch (Domain.Common.DomainValidationException exception)
            {
                throw new InvalidOperationException(
                    $"Stored product \"{matches[0].ProductCode}\" has an invalid category.", exception);
            }

            foreach (var input in matchedInputs)
            {
                results[input] = resolved;
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<CapabilityResolution> ResolveAsync(string? productCode, CancellationToken cancellationToken)
    {
        if (productCode is null)
        {
            return CapabilityResolution.Unknown;
        }

        var results = await ResolveAsync([productCode], cancellationToken);

        return results[productCode];
    }
}
