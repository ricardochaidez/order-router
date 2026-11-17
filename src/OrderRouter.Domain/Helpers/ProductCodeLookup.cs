using OrderRouter.Domain.Entities.ReferenceData;

namespace OrderRouter.Domain.Helpers;

/// <summary>
/// Resolves raw product codes to categories (FR-05, FR-16) in two pure steps around one catalog query:
/// <see cref="For"/> turns the request codes into <see cref="Keys"/>, and <see cref="Resolve"/> maps the products
/// the catalog found back to every input spelling. A code with no match is <see cref="CapabilityResolution.Unknown"/>,
/// never an error; two products for one key is corrupt data and throws (fail safe, CLAUDE.md).
/// </summary>
/// <example>
/// <code>
/// var lookup = ProductCodeLookup.For(command.Lines.Select(line => line.ProductCode));
/// var resolutions = lookup.Resolve(await catalog.FindByKeysAsync(lookup.Keys, ct));
/// </code>
/// </example>
public sealed class ProductCodeLookup
{
    private readonly IReadOnlyList<string> inputs;
    private readonly ILookup<ProductCodeKey, string> inputsByKey;

    private ProductCodeLookup(IReadOnlyList<string> inputs, ILookup<ProductCodeKey, string> inputsByKey)
    {
        this.inputs = inputs;
        this.inputsByKey = inputsByKey;
        Keys = [.. inputsByKey.Select(group => group.Key)];
    }

    /// <summary>The distinct keys to look up. Empty when no input can ever match a product.</summary>
    public IReadOnlyCollection<ProductCodeKey> Keys { get; }

    /// <summary>Prepares a lookup for raw request codes.</summary>
    /// <param name="productCodes">Raw codes; null, empty and whitespace-only entries are allowed and resolve to unknown.</param>
    /// <exception cref="ArgumentNullException"><paramref name="productCodes"/> is null.</exception>
    public static ProductCodeLookup For(IEnumerable<string?> productCodes)
    {
        ArgumentNullException.ThrowIfNull(productCodes);

        // A null input cannot be a dictionary key; callers treat a missing code as unknown.
        var inputs = productCodes.OfType<string>().Distinct(StringComparer.Ordinal).ToList();

        // Several spellings (" ox-port-024 ", "OX-PORT-024") can share one key; the lookup keeps all of them.
        var inputsByKey = inputs
            .Select(input => ProductCodeKey.TryCreate(input, out var key) ? (Key: key, Input: input) : default)
            .Where(pair => pair.Input is not null)
            .ToLookup(pair => pair.Key, pair => pair.Input);

        return new ProductCodeLookup(inputs, inputsByKey);
    }

    /// <summary>Maps the products the catalog found to the inputs.</summary>
    /// <param name="products">Products returned for <see cref="Keys"/>; products for other keys are ignored.</param>
    /// <returns>
    /// One entry per distinct non-null input, keyed by the input exactly as given (ordinal). Null inputs are not
    /// present: treat a missing code as unknown.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="products"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Two products match one key (corrupt data; never guess).</exception>
    public IReadOnlyDictionary<string, CapabilityResolution> Resolve(IEnumerable<Product> products)
    {
        ArgumentNullException.ThrowIfNull(products);

        // Every input starts as unknown and only a matching product can change that (fail safe, FR-05).
        var results = inputs.ToDictionary(input => input, _ => CapabilityResolution.Unknown, StringComparer.Ordinal);

        var requested = products
            .Select(product => ProductCodeKey.TryCreate(product.Code, out var key) ? (Key: key, Product: product) : default)
            .Where(pair => pair.Product is not null && inputsByKey.Contains(pair.Key))
            .GroupBy(pair => pair.Key, pair => pair.Product);

        foreach (var group in requested)
        {
            var matches = group.ToList();

            // Two products for one key is corrupt data (OR-005 rejects it). Ambiguity must not route.
            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Product code lookup is ambiguous: stored codes {string.Join(", ", matches.Select(match => $"\"{match.Code}\""))} " +
                    "all match the same key.");
            }

            var resolved = CapabilityResolution.Resolved(matches[0].Category);

            foreach (var input in inputsByKey[group.Key])
            {
                results[input] = resolved;
            }
        }

        return results;
    }
}
