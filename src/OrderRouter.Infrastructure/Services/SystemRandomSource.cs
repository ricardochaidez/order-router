using OrderRouter.Domain.Interfaces;

namespace OrderRouter.Infrastructure.Services;

/// <summary>
/// Production random source. It wraps <see cref="Random.Shared"/>, which is thread-safe and not seeded, so
/// ties are truly random and not repeatable (SC-17, SC-18). It lives here, not in the Domain, so the domain
/// stays free of hidden randomness (NFR-08a, ADR-0001).
/// </summary>
public sealed class SystemRandomSource : IRandomSource
{
    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxExclusive"/> is less than 1.</exception>
    public int NextInt(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxExclusive, 1);

        return Random.Shared.Next(maxExclusive);
    }
}
