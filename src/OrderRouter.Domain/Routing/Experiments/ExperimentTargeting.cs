using OrderRouter.Domain.Common;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.Domain.Routing.Experiments;

/// <summary>
/// Who and where an experiment applies to (SC-26): an allow-list of callers plus customer ZIP prefixes and ranges.
/// An empty list means "everyone" (callers) or "everywhere" (ZIPs), so the empty targeting matches every order.
/// </summary>
public sealed record ExperimentTargeting
{
    /// <summary>The longest ZIP prefix: a whole ZIP.</summary>
    private const int maxPrefixLength = 5;

    /// <summary>Targeting that matches every caller and every ZIP.</summary>
    public static ExperimentTargeting Everyone { get; } = new([], [], []);

    /// <summary>Creates targeting, reporting every invalid prefix in one exception.</summary>
    /// <param name="callers">Allowed caller IDs, compared ordinally; empty means everyone.</param>
    /// <param name="zipPrefixes">Prefixes of 1 to 5 ASCII digits.</param>
    /// <param name="zipRanges">Inclusive 5-digit ranges.</param>
    /// <exception cref="DomainValidationException">A caller is blank or a prefix is not 1 to 5 digits.</exception>
    public ExperimentTargeting(IReadOnlyList<string> callers, IReadOnlyList<string> zipPrefixes, IReadOnlyList<ZipRange> zipRanges)
    {
        ArgumentNullException.ThrowIfNull(callers);
        ArgumentNullException.ThrowIfNull(zipPrefixes);
        ArgumentNullException.ThrowIfNull(zipRanges);

        var errors = new List<string>();

        // The message never echoes a caller value (NFR-11); it only counts them.
        if (callers.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("caller IDs must not be blank");
        }

        errors.AddRange(zipPrefixes
            .Where(prefix => prefix is not { Length: >= 1 and <= maxPrefixLength } || !prefix.All(char.IsAsciiDigit))
            .Select(prefix => $"ZIP prefix must be 1 to {maxPrefixLength} digits (received \"{prefix}\")"));

        if (errors.Count > 0)
        {
            throw new DomainValidationException($"Invalid experiment targeting: {string.Join("; ", errors)}.");
        }

        Callers = [.. callers];
        ZipPrefixes = [.. zipPrefixes];
        ZipRanges = [.. zipRanges];
    }

    /// <summary>Allowed caller IDs; empty means everyone.</summary>
    public IReadOnlyList<string> Callers { get; }

    /// <summary>ZIP prefixes of 1 to 5 digits.</summary>
    public IReadOnlyList<string> ZipPrefixes { get; }

    /// <summary>Inclusive ZIP ranges.</summary>
    public IReadOnlyList<ZipRange> ZipRanges { get; }

    /// <summary>True when the caller passes the allow-list. A request with no caller never matches a non-empty list.</summary>
    /// <param name="callerId">The authenticated caller, or <c>null</c> when unknown.</param>
    public bool MatchesCaller(string? callerId)
        => Callers.Count == 0 || (callerId is not null && Callers.Contains(callerId, StringComparer.Ordinal));

    /// <summary>True when the ZIP matches a prefix or a range; with neither configured, every ZIP matches.</summary>
    /// <param name="zip">The customer ZIP.</param>
    public bool MatchesZip(ZipCode zip)
        => (ZipPrefixes.Count == 0 && ZipRanges.Count == 0)
            || ZipPrefixes.Any(prefix => zip.Value.StartsWith(prefix, StringComparison.Ordinal))
            || ZipRanges.Any(range => range.Contains(zip));

    /// <summary>Both the caller and the ZIP must match (target first, then split, SC-26).</summary>
    /// <param name="callerId">The authenticated caller, or <c>null</c>.</param>
    /// <param name="zip">The customer ZIP.</param>
    public bool Matches(string? callerId, ZipCode zip) => MatchesCaller(callerId) && MatchesZip(zip);
}
