using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Helpers;

namespace OrderRouter.Domain.Interfaces;

/// <summary>
/// Port: the stored products whose code matches a lookup key (FR-05, FR-16). Implemented in Infrastructure; the
/// domain never knows where the catalog lives (ADR-0002). It only fetches. Turning raw request codes into keys,
/// treating a missing code as unknown and rejecting ambiguous matches belong to <see cref="ProductCodeLookup"/>.
/// </summary>
/// <example>
/// <code>
/// var lookup = ProductCodeLookup.For(["OX-PORT-024", " ox-port-024 ", "nope"]);
/// var resolutions = lookup.Resolve(await catalog.FindByKeysAsync(lookup.Keys, ct));
/// </code>
/// </example>
public interface IProductCatalog
{
    /// <summary>Finds every stored product whose code has one of the keys, in one round trip (ADR-0006 option A).</summary>
    /// <param name="keys">The lookup keys; empty returns an empty list without any I/O.</param>
    /// <param name="ct">Cancels the lookup.</param>
    /// <returns>
    /// The matching products, never <c>null</c>. Stored data is returned as it is: two products sharing one key
    /// (corrupt data) both come back, so the caller can refuse to guess.
    /// </returns>
    /// <exception cref="InvalidOperationException">A stored product violates a domain invariant (never skipped or repaired, NFR-10).</exception>
    Task<IReadOnlyList<Product>> FindByKeysAsync(IReadOnlyCollection<ProductCodeKey> keys, CancellationToken ct = default);

    /// <summary>
    /// Returns which of the given categories have at least one stored product, in one round trip. Used to skip
    /// item-dependency rules that name a category not in the reference data (DR-02).
    /// </summary>
    /// <param name="categories">The categories to check; empty returns an empty set without any I/O.</param>
    /// <param name="ct">Cancels the lookup.</param>
    /// <returns>The subset of <paramref name="categories"/> that exists. Never <c>null</c>.</returns>
    Task<IReadOnlySet<Category>> FindExistingCategoriesAsync(IReadOnlyCollection<Category> categories, CancellationToken ct = default);
}
