namespace OrderRouter.Domain.ReferenceData;

/// <summary>
/// The answer to "what category is this product code?": a <see cref="Category"/> or <i>unknown</i>
/// (FR-05, FR-16). It is never <c>null</c> and lookups never throw for an unknown code (§5.1);
/// OR-015 later turns <i>unknown</i> into <c>UNKNOWN_PRODUCT</c>.
/// </summary>
/// <remarks>
/// <c>default(CapabilityResolution)</c> is <i>unknown</i>, so a missing value fails safe (CLAUDE.md).
/// </remarks>
/// <example>
/// <code>
/// var result = await resolver.ResolveAsync("ox-port-024", cancellationToken);
/// if (result.TryGetCategory(out var category)) { /* route by category */ }
/// </code>
/// </example>
public readonly record struct CapabilityResolution
{
    private readonly Category? category;

    private CapabilityResolution(Category category) => this.category = category;

    /// <summary>The unknown-product result.</summary>
    public static CapabilityResolution Unknown => default;

    /// <summary>True when the product code matched a product.</summary>
    public bool IsKnown => category is not null;

    /// <summary>The resolved category.</summary>
    /// <exception cref="InvalidOperationException">The result is unknown; check <see cref="IsKnown"/> first.</exception>
    public Category Category => category
        ?? throw new InvalidOperationException("The product code is unknown, so there is no category.");

    /// <summary>Creates a resolved result.</summary>
    /// <param name="category">The product's category.</param>
    public static CapabilityResolution Resolved(Category category) => new(category);

    /// <summary>Gets the category when the result is known.</summary>
    /// <param name="category">The category, or <c>default</c> when unknown.</param>
    /// <returns><c>true</c> when known.</returns>
    public bool TryGetCategory(out Category category)
    {
        category = this.category ?? default;

        return IsKnown;
    }
}
