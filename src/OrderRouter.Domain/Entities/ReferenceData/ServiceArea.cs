using OrderRouter.Domain.Models;

namespace OrderRouter.Domain.Entities.ReferenceData;

/// <summary>
/// The ZIP ranges a supplier serves (Technical Requirements §3, "Service area"). Segments are kept
/// as given and never merged; overlaps are allowed because the source data has them.
/// </summary>
public sealed class ServiceArea
{
    private readonly ZipRange[] segments;

    /// <summary>Creates a service area from at least one segment.</summary>
    /// <param name="segments">The segments; copied so later changes by the caller have no effect.</param>
    /// <exception cref="DomainValidationException">The collection is null or empty (NFR-10: fail safe).</exception>
    public ServiceArea(IEnumerable<ZipRange> segments)
    {
        var copy = segments?.ToArray() ?? [];

        this.segments = copy.Length > 0
            ? copy
            : throw new DomainValidationException("Service area must have at least one ZIP range segment.");
    }

    /// <summary>The segments, in the order given.</summary>
    public IReadOnlyList<ZipRange> Segments => segments;

    /// <summary>True when any segment contains <paramref name="zip"/> (AC4).</summary>
    /// <param name="zip">The ZIP to test.</param>
    public bool Contains(ZipCode zip) => segments.Any(segment => segment.Contains(zip));

    /// <summary>
    /// True only when there is exactly one segment and it equals <paramref name="range"/>.
    /// Segments are deliberately not merged (SC-09): <c>{00100-50000, 50001-99999}</c> is not the nationwide range.
    /// </summary>
    /// <param name="range">The range to compare against.</param>
    public bool IsExactly(ZipRange range) => segments.Length == 1 && segments[0] == range;
}
