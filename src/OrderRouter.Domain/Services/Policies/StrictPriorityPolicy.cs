using OrderRouter.Domain.Helpers;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Models;

namespace OrderRouter.Domain.Services.Policies;

/// <summary>
/// Control arm of EXP-1 (Technical Requirements §7.3): fewest shipments, then the quality band,
/// then most local lines, then highest quality. Immutable: safe to share.
/// </summary>
public sealed class StrictPriorityPolicy : IPlanSelectionPolicy
{
    private readonly decimal similarityBand;

    /// <summary>Creates the policy.</summary>
    /// <param name="similarityBand">How far below the best quality a plan may be and still count as similar; 0 or more.</param>
    /// <exception cref="DomainValidationException">The band is negative.</exception>
    public StrictPriorityPolicy(decimal similarityBand)
    {
        if (similarityBand < 0m)
        {
            throw new DomainValidationException($"Similarity band must not be negative (received {similarityBand}).");
        }

        this.similarityBand = similarityBand;
    }

    /// <inheritdoc />
    public int MaxExtraShipments => 0;

    /// <inheritdoc />
    public IReadOnlyList<RoutingPlan> SelectBest(IReadOnlyList<RoutingPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);

        if (plans.Count == 0)
        {
            return [];
        }

        var fewest = plans.Min(p => p.ShipmentCount);

        return PlanRanking.BestWithinSize([.. plans.Where(p => p.ShipmentCount == fewest)], similarityBand);
    }
}
