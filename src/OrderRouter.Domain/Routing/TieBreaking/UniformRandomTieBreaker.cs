using OrderRouter.Domain.Routing.PlanSearch;

namespace OrderRouter.Domain.Routing.TieBreaking;

/// <summary>
/// Key 5 (FR-13, SC-17): on a full tie, picks one plan uniformly at random so the same supplier does not
/// always win. Stateless apart from the injected random source (ADR-0001).
/// </summary>
public sealed class UniformRandomTieBreaker : ITieBreaker
{
    private readonly IRandomSource random;

    /// <summary>Creates the tie-breaker.</summary>
    /// <param name="random">The only source of randomness (NFR-08a).</param>
    public UniformRandomTieBreaker(IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        this.random = random;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="tied"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tied"/> is empty; the caller handles "no plan" first, and a plan is never invented.</exception>
    /// <exception cref="InvalidOperationException">The random source returned a value outside the valid range.</exception>
    public TieBreakResult Pick(IReadOnlyList<RoutingPlan> tied)
    {
        ArgumentNullException.ThrowIfNull(tied);

        if (tied.Count == 0)
        {
            throw new ArgumentException("At least one plan is required.", nameof(tied));
        }

        // NFR-01, NFR-08d: no tie means no randomness, so the same input always gives the same plan.
        if (tied.Count == 1)
        {
            return new TieBreakResult(tied[0], tied);
        }

        var index = random.NextInt(tied.Count);

        // Fail safe: a broken source must never silently pick a wrong or default plan.
        if (index < 0 || index >= tied.Count)
        {
            throw new InvalidOperationException(
                $"The random source returned {index}, which is outside the range 0 to {tied.Count - 1}.");
        }

        return new TieBreakResult(tied[index], tied);
    }
}
