using OrderRouter.Domain.Routing.PlanSearch;
namespace OrderRouter.Domain.Interfaces;

/// <summary>
/// Port: runs the plan search with the configured guard rails. The domain routing service (OR-015) calls it; the
/// Application layer implements it, because the limits come from configuration (ADR-0003).
/// </summary>
public interface IPlanSearchService
{
    /// <summary>Searches for routing plans; falls back to greedy when a guard rail trips.</summary>
    /// <param name="request">Demand, eligible candidates and policy values.</param>
    /// <returns>The result, including <see cref="PlanSearchResult.Mode"/> for <c>search_mode</c>.</returns>
    PlanSearchResult Search(PlanSearchRequest request);
}
