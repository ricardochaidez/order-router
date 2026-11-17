using System.Diagnostics.CodeAnalysis;
using OrderRouter.Domain.Models;

namespace OrderRouter.Domain.Entities.ReferenceData;

/// <summary>
/// An inclusive range of ZIP codes (AC4). A single ZIP is a range where start equals end.
/// </summary>
/// <example>
/// <code>
/// ZipRange.Parse("02114-02212").Contains(ZipCode.Parse("02212")); // true
/// </code>
/// </example>
public readonly record struct ZipRange
{
    /// <summary>Creates a range.</summary>
    /// <param name="start">First ZIP, inclusive.</param>
    /// <param name="end">Last ZIP, inclusive.</param>
    /// <exception cref="DomainValidationException"><paramref name="start"/> is greater than <paramref name="end"/>.</exception>
    public ZipRange(ZipCode start, ZipCode end)
    {
        if (start.CompareTo(end) > 0)
        {
            throw new DomainValidationException($"ZIP range start {start} must not be greater than end {end}.");
        }

        Start = start;
        End = end;
    }

    /// <summary>First ZIP, inclusive.</summary>
    public ZipCode Start { get; }

    /// <summary>Last ZIP, inclusive.</summary>
    public ZipCode End { get; }

    /// <summary>Creates a range covering exactly one ZIP.</summary>
    /// <param name="zip">The ZIP.</param>
    public static ZipRange Single(ZipCode zip) => new(zip, zip);

    /// <summary>True when <paramref name="zip"/> is between start and end, inclusive on both ends.</summary>
    /// <param name="zip">The ZIP to test.</param>
    public bool Contains(ZipCode zip) => zip.CompareTo(Start) >= 0 && zip.CompareTo(End) <= 0;

    /// <summary>Parses <c>"NNNNN"</c> or <c>"NNNNN-NNNNN"</c>; no spaces and no padding.</summary>
    /// <param name="value">Candidate text.</param>
    /// <exception cref="DomainValidationException">The text is not a valid range.</exception>
    public static ZipRange Parse(string? value)
        => TryParse(value, out var range)
            ? range
            : throw new DomainValidationException(
                $"ZIP range must be \"NNNNN\" or \"NNNNN-NNNNN\" with start <= end (received {(value is null ? "null" : $"\"{value}\"")}).");

    /// <summary>Tries to parse a range without throwing.</summary>
    /// <param name="value">Candidate text.</param>
    /// <param name="range">The parsed range, or <c>default</c> on failure.</param>
    /// <returns><c>true</c> when valid.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out ZipRange range)
    {
        range = default;

        switch (value)
        {
            case { Length: 5 } when ZipCode.TryParse(value, out var single):
                range = Single(single);
                return true;
            case { Length: 11 } when value[5] == '-'
                && ZipCode.TryParse(value[..5], out var start)
                && ZipCode.TryParse(value[6..], out var end)
                && start.CompareTo(end) <= 0:
                range = new ZipRange(start, end);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Returns <c>"NNNNN-NNNNN"</c>, the format <see cref="Parse"/> accepts.</summary>
    public override string ToString() => $"{Start}-{End}";
}
