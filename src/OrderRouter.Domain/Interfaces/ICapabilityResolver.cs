using OrderRouter.Domain.ReferenceData;
namespace OrderRouter.Domain.Interfaces;

/// <summary>
/// Port: product code to category (FR-05, FR-16). Implemented in Infrastructure; the domain never
/// knows where the catalog lives (ADR-0002). Lookup ignores case and all whitespace (<see cref="ProductCodeKey"/>).
/// </summary>
/// <example>
/// <code>
/// var one = await resolver.ResolveAsync(" ox-port-024 ", cancellationToken);
/// var many = await resolver.ResolveAsync(["OX-PORT-024", "nope"], cancellationToken);
/// </code>
/// </example>
public interface ICapabilityResolver
{
    /// <summary>Resolves many product codes with one round trip (ADR-0006 option A).</summary>
    /// <param name="productCodes">Raw codes; null, empty and whitespace-only entries are allowed and resolve to unknown.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>
    /// One entry per distinct input string, keyed by the input exactly as given (ordinal). Null inputs
    /// cannot be dictionary keys, so they are not present in the result: treat a missing code as unknown.
    /// </returns>
    /// <exception cref="InvalidOperationException">Two stored products match one code (corrupt data; never guess).</exception>
    Task<IReadOnlyDictionary<string, CapabilityResolution>> ResolveAsync(
        IEnumerable<string?> productCodes,
        CancellationToken cancellationToken);

    /// <summary>Resolves a single product code.</summary>
    /// <param name="productCode">Raw code; null, empty or whitespace-only resolves to unknown.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The category, or <see cref="CapabilityResolution.Unknown"/>. Never throws for an unknown code.</returns>
    Task<CapabilityResolution> ResolveAsync(string? productCode, CancellationToken cancellationToken);
}
