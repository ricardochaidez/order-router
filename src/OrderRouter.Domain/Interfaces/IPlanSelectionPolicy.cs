using OrderRouter.Domain.Models.PlanSearch;

namespace OrderRouter.Domain.Interfaces;

/// <summary>EXP-1 seam: which of the candidate plans is best (ADR-0001).</summary>
public interface IPlanSelectionPolicy
{
    /// <summary>Extra shipments beyond the minimum that the plan search should also return (0 or 1).</summary>
    int MaxExtraShipments { get; }

    /// <summary>Returns every plan still tied after the ranking; the tie-break (OR-014) picks one.</summary>
    /// <param name="plans">Plans from the plan search.</param>
    IReadOnlyList<RoutingPlan> SelectBest(IReadOnlyList<RoutingPlan> plans);
}
