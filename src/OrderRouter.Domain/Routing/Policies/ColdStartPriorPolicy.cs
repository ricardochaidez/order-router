using OrderRouter.Domain.Common;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.Domain.Routing.Policies;

/// <summary>
/// Treatment arm of EXP-2 (SC-02): an unrated supplier counts as the prior, so it can win some orders. A rated
/// supplier keeps its own rating. Immutable: safe to share.
/// </summary>
/// <remarks>
/// The prior arrives per request through <c>ExperimentAssignment.Parameters</c>; the policy never reads
/// configuration or captures a value at startup (ADR-0001).
/// </remarks>
public sealed class ColdStartPriorPolicy : IEffectiveRatingPolicy
{
    private readonly decimal prior;

    /// <summary>Creates the policy.</summary>
    /// <param name="prior">Effective rating of an unrated supplier; 1 to 10 inclusive.</param>
    /// <exception cref="DomainValidationException">The prior is outside 1 to 10.</exception>
    public ColdStartPriorPolicy(decimal prior)
    {
        if (prior is < 1m or > 10m)
        {
            throw new DomainValidationException($"Unrated prior must be between 1 and 10 inclusive (received {prior}).");
        }

        this.prior = prior;
    }

    /// <inheritdoc />
    public decimal EffectiveRating(Supplier supplier)
    {
        ArgumentNullException.ThrowIfNull(supplier);

        return supplier.Rating?.Value ?? prior;
    }
}
