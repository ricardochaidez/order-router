using System.Globalization;
using System.Text;

namespace OrderRouter.Domain.Entities.ReferenceData;

/// <summary>
/// The lookup key for a product code (FR-05): all whitespace removed, then lower-cased with the
/// invariant culture. <c>" ox-port-024 "</c>, <c>"OX- PORT-024"</c> and <c>"OX-PORT-024"</c> share one key.
/// </summary>
/// <remarks>
/// The matching rule lives here, in one place, so it can be unit tested without a database.
/// <see cref="Product.Code"/> stays case-preserving (DQ-09); only the lookup is forgiving.
/// Whitespace means <see cref="char.IsWhiteSpace(char)"/>. This is safe against collisions only while no
/// stored product code contains whitespace, which a test checks against the seed file.
/// <para>
/// <c>default(ProductCodeKey)</c> has no value; <see cref="Value"/> then throws instead of returning <c>null</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// ProductCodeKey.TryCreate(" OX- PORT-024 ", out var key); // true, key.Value == "ox-port-024"
/// </code>
/// </example>
public readonly record struct ProductCodeKey
{
    private readonly string? value;

    private ProductCodeKey(string value) => this.value = value;

    /// <summary>The normalized key (no whitespace, lower-case).</summary>
    /// <exception cref="InvalidOperationException">This is <c>default(ProductCodeKey)</c>.</exception>
    public string Value => value
        ?? throw new InvalidOperationException("ProductCodeKey was not initialized; use TryCreate.");

    /// <summary>Tries to build a key from a raw product code.</summary>
    /// <param name="productCode">Raw code as received from a caller.</param>
    /// <param name="key">The key, or <c>default</c> when this returns <c>false</c>.</param>
    /// <returns><c>false</c> for null, empty or whitespace-only input: such a code can never match a product.</returns>
    public static bool TryCreate(string? productCode, out ProductCodeKey key)
    {
        key = default;

        if (string.IsNullOrEmpty(productCode))
        {
            return false;
        }

        var builder = new StringBuilder(productCode.Length);

        foreach (var character in productCode)
        {
            if (!char.IsWhiteSpace(character))
            {
                builder.Append(char.ToLower(character, CultureInfo.InvariantCulture));
            }
        }

        if (builder.Length == 0)
        {
            return false;
        }

        key = new ProductCodeKey(builder.ToString());

        return true;
    }

    /// <summary>Returns the normalized key, or an empty string for <c>default</c>.</summary>
    public override string ToString() => value ?? string.Empty;
}
