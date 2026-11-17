using OrderRouter.Domain.Helpers;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Models;

namespace OrderRouter.Domain.Services.Policies;

/// <summary>
/// Treatment arm of EXP-1 (SC-01, Technical Requirements §8.1): allow one extra shipment, but only for a much better plan.
/// Immutable: safe to share.
/// </summary>
/// <remarks>
/// The baseline is the plan <see cref="StrictPriorityPolicy"/> would pick. The best plan with one more shipment wins only when
/// its quality is at least the uplift threshold above the baseline's; otherwise this behaves exactly like strict priority.
/// Plans with two or more extra shipments are never selected (BR-12).
/// </remarks>
/// <example>
/// Threshold 2.0: a one-shipment plan at 6.0 against a two-shipment plan at 9.0 selects the two-shipment plan;
/// at 8.0 against 9.0 the uplift is only 1.0, so the one-shipment plan stays.
/// </example>
public sealed class BoundedTradeOffPolicy : IPlanSelectionPolicy
{
    private readonly decimal similarityBand;
    private readonly decimal qualityUpliftThreshold;

    /// <summary>Creates the policy.</summary>
    /// <param name="similarityBand">How far below the best quality a plan may be and still count as similar; 0 or more.</param>
    /// <param name="qualityUpliftThreshold">Minimum quality gain over the strict-priority plan that justifies one extra shipment; above 0.</param>
    /// <exception cref="DomainValidationException">The band is negative or the threshold is not above 0 (all problems reported together).</exception>
    public BoundedTradeOffPolicy(decimal similarityBand, decimal qualityUpliftThreshold)
    {
        var errors = new List<string>();

        if (similarityBand < 0m)
        {
            errors.Add($"Similarity band must not be negative (received {similarityBand}).");
        }

        if (qualityUpliftThreshold <= 0m)
        {
            errors.Add($"Quality uplift threshold must be greater than 0 (received {qualityUpliftThreshold}).");
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException($"Invalid bounded trade-off parameters: {string.Join("; ", errors)}.");
        }

        this.similarityBand = similarityBand;
        this.qualityUpliftThreshold = qualityUpliftThreshold;
    }

    /// <inheritdoc />
    public int MaxExtraShipments => 1;

    /// <inheritdoc />
    public IReadOnlyList<RoutingPlan> SelectBest(IReadOnlyList<RoutingPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);

        if (plans.Count == 0)
        {
            return [];
        }

        var fewest = plans.Min(p => p.ShipmentCount);
        var baseline = PlanRanking.BestWithinSize([.. plans.Where(p => p.ShipmentCount == fewest)], similarityBand);
        var oneMore = plans.Where(p => p.ShipmentCount == fewest + 1).ToList();

        // Fail safe: no challenger means the control plan.
        if (oneMore.Count == 0)
        {
            return baseline;
        }

        var challenger = PlanRanking.BestWithinSize(oneMore, similarityBand);

        // SC-01: the uplift is measured against the plan strict priority would pick; "at least" is inclusive.
        return challenger[0].Quality - baseline[0].Quality >= qualityUpliftThreshold ? challenger : baseline;
    }
}
