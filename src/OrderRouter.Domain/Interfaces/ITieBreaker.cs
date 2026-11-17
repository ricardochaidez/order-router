using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Models;

namespace OrderRouter.Domain.Interfaces;

/// <summary>Ranking key 5 (Technical Requirements §7.3, FR-13): chooses one plan from those still tied. Shared by every experiment arm (ADR-0001).</summary>
public interface ITieBreaker
{
    /// <summary>Picks one plan from the tied plans.</summary>
    /// <param name="tied">Plans still tied after keys 1-4; at least one.</param>
    /// <returns>The chosen plan with the tied candidates, for the decision log (AB-04).</returns>
    TieBreakResult Pick(IReadOnlyList<RoutingPlan> tied);
}
