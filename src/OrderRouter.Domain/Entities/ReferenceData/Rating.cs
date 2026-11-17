using OrderRouter.Domain.Models;

namespace OrderRouter.Domain.Entities.ReferenceData;

/// <summary>
/// A supplier rating from 1 to 10 inclusive, rounded to 2 decimal places with halves rounded up.
/// </summary>
/// <remarks>
/// <c>decimal</c> avoids binary float drift and compares numerically (<c>10</c> equals <c>10.0</c>, DQ-06).
/// <see cref="MidpointRounding.AwayFromZero"/> is the agreed rule; .NET's default (to even) would turn
/// <c>9.245</c> into <c>9.24</c>. The domain rounds here, once, so the database never rounds again.
/// The 1–10 check runs on the <b>raw</b> value, before rounding, so <c>0.996</c> and <c>10.004</c> are
/// rejected instead of silently repaired (NFR-10).
/// </remarks>
public readonly record struct Rating
{
    private const decimal min = 1m;
    private const decimal max = 10m;

    /// <summary>Creates a rating.</summary>
    /// <param name="value">Raw value, 1 to 10 inclusive.</param>
    /// <exception cref="DomainValidationException">The value is outside 1–10.</exception>
    public Rating(decimal value)
    {
        if (value is < min or > max)
        {
            throw new DomainValidationException($"Rating must be between 1 and 10 inclusive (received {value}).");
        }

        Value = Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>The rating rounded to 2 decimal places.</summary>
    public decimal Value { get; }

    /// <summary>Returns the rounded value.</summary>
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
