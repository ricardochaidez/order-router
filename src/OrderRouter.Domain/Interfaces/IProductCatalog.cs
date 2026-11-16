using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.Domain.Interfaces;

/// <summary>
/// Port: the stored products whose code matches a lookup key (FR-05, FR-16). Implemented in Infrastructure; the
/// domain never knows where the catalog lives (ADR-0002). It only fetches. Turning raw request codes into keys,
/// treating a missing code as unknown and rejecting ambiguous matches belong to <see cref="ProductCodeLookup"/>.
/// </summary>
/// <example>
/// <code>
/// var lookup = ProductCodeLookup.For(["OX-PORT-024", " ox-port-024 ", "nope"]);
/// var resolutions = lookup.Resolve(await catalog.FindByKeysAsync(lookup.Keys, cancellationToken));
/// </code>
/// </example>
public interface IProductCatalog
{
    /// <summary>Finds every stored product whose code has one of the keys, in one round trip (ADR-0006 option A).</summary>
    /// <param name="keys">The lookup keys; empty returns an empty list without any I/O.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>
    /// The matching products, never <c>null</c>. Stored data is returned as it is: two products sharing one key
    /// (corrupt data) both come back, so the caller can refuse to guess.
    /// </returns>
    /// <exception cref="InvalidOperationException">A stored product violates a domain invariant (never skipped or repaired, NFR-10).</exception>
    Task<IReadOnlyList<Product>> FindByKeysAsync(IReadOnlyCollection<ProductCodeKey> keys, CancellationToken cancellationToken);
}
