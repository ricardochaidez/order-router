using System.Diagnostics.CodeAnalysis;
using OrderRouter.Domain.Entities.ReferenceData;

namespace OrderRouter.DataNormalizer.Parsing;

/// <summary>
/// Turns the raw <c>service_zips</c> cell into ZIP ranges (DQ-01, DQ-02, DQ-03). It only cleans syntax
/// (splitting, trimming, left-padding); the domain types still decide what a valid ZIP or range is.
/// </summary>
/// <example>
/// <code>
/// ServiceZipsParser.TryParse("2130, 2114-2212", out var ranges, out _);
/// // ranges: 02130-02130 and 02114-02212
/// </code>
/// </example>
public static class ServiceZipsParser
{
    private const int zipLength = 5;

    /// <summary>Parses a comma-separated list of ZIPs and ZIP ranges.</summary>
    /// <param name="cell">The raw cell text.</param>
    /// <param name="ranges">The parsed ranges in source order; empty on failure.</param>
    /// <param name="error">Why the cell was rejected; <c>null</c> on success.</param>
    /// <returns><c>true</c> when every token is valid; one bad token rejects the whole cell (NFR-10).</returns>
    public static bool TryParse(
        string? cell,
        out IReadOnlyList<ZipRange> ranges,
        [NotNullWhen(false)] out string? error)
    {
        ranges = [];

        if (string.IsNullOrWhiteSpace(cell))
        {
            error = "service_zips is empty";
            return false;
        }

        var parsed = new List<ZipRange>();

        foreach (var rawToken in cell.Split(','))
        {
            var token = rawToken.Trim();

            if (!TryParseToken(token, out var range, out error))
            {
                return false;
            }

            parsed.Add(range);
        }

        ranges = parsed;
        error = null;

        return true;
    }

    private static bool TryParseToken(string token, out ZipRange range, [NotNullWhen(false)] out string? error)
    {
        range = default;

        if (token.Length == 0)
        {
            error = "service_zips contains an empty token";
            return false;
        }

        var parts = token.Split('-');

        if (parts.Length > 2)
        {
            error = $"service_zips token \"{token}\" is not a ZIP or a ZIP range";
            return false;
        }

        if (!TryPad(parts[0], out var start) || (parts.Length == 2 && !TryPad(parts[1], out _)))
        {
            error = $"service_zips token \"{token}\" is not a valid ZIP or ZIP range";
            return false;
        }

        var endText = parts.Length == 2 ? parts[1].Trim() : start;
        _ = TryPad(endText, out var end);

        // ZipCode and ZipRange stay the authority on validity; start > end is rejected here without exceptions.
        if (!ZipCode.TryParse(start, out var startZip) || !ZipCode.TryParse(end, out var endZip) || startZip.CompareTo(endZip) > 0)
        {
            error = $"service_zips token \"{token}\" is not a valid ZIP or ZIP range (start must not exceed end)";
            return false;
        }

        range = new ZipRange(startZip, endZip);
        error = null;

        return true;
    }

    // DQ-01: the source lost leading zeros (2130 for 02130). Only 1-5 ASCII digits are padded.
    private static bool TryPad(string text, out string padded)
    {
        var trimmed = text.Trim();
        padded = string.Empty;

        if (trimmed.Length is 0 or > zipLength || !trimmed.All(char.IsAsciiDigit))
        {
            return false;
        }

        padded = trimmed.PadLeft(zipLength, '0');

        return true;
    }
}
