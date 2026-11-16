using OrderRouter.Domain.Interfaces;

namespace OrderRouter.IntegrationTests.Acceptance;

/// <summary>
/// Test double for the only source of randomness (NFR-08a): always returns the same index, so a tie-break is
/// deterministic. Thread-safe because the shared host may route requests in parallel.
/// </summary>
internal sealed class FixedRandomSource(int value) : IRandomSource
{
    private int calls;

    /// <summary>How many times a random number was requested.</summary>
    public int CallCount => Volatile.Read(ref calls);

    /// <inheritdoc />
    public int NextInt(int maxExclusive)
    {
        Interlocked.Increment(ref calls);

        return Math.Min(value, maxExclusive - 1);
    }
}
