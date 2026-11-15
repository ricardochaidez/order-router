using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace OrderRouter.DataNormalizer.Parsing;

/// <summary>
/// Parses the raw rating cell (DQ-06). It does not check the 1-10 range: that invariant belongs to
/// the domain <c>Rating</c> and is applied when the <c>Supplier</c> is built.
/// </summary>
public static class RatingParser
{
    private const string unrated = "no ratings yet";

    /// <summary>Parses <c>10</c>, <c>10.0</c>, <c>6.7</c> or <c>no ratings yet</c>.</summary>
    /// <param name="cell">The raw cell text.</param>
    /// <param name="rating">The rating, or <c>null</c> when the supplier is unrated (never <c>0</c>).</param>
    /// <param name="error">Why the cell was rejected; <c>null</c> on success.</param>
    /// <returns><c>true</c> when the cell is a number or the unrated marker.</returns>
    public static bool TryParse(string? cell, out decimal? rating, [NotNullWhen(false)] out string? error)
    {
        rating = null;
        var trimmed = cell?.Trim();

        if (string.Equals(trimmed, unrated, StringComparison.OrdinalIgnoreCase))
        {
            error = null;
            return true;
        }

        // Invariant culture: the source uses "." and must parse the same on every machine.
        // AllowDecimalPoint only: NumberStyles.Number would accept thousands separators, so "1,0" would
        // silently become 10 (NFR-10: never guess).
        if (decimal.TryParse(trimmed, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            rating = value;
            error = null;
            return true;
        }

        error = $"rating \"{cell}\" is neither a number nor \"no ratings yet\"";
        return false;
    }
}
