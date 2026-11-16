using OrderRouter.Domain.Routing.PlanSearch;

namespace OrderRouter.Application.Routing.PlanSearch;

/// <summary>Runs the plan search with the configured guard rails. OR-015 (orchestration) calls this.</summary>
public interface IPlanSearchService
{
    /// <summary>Searches for routing plans; falls back to greedy when a guard rail trips.</summary>
    /// <param name="request">Demand, eligible candidates and policy values.</param>
    /// <returns>The result, including <see cref="PlanSearchResult.Mode"/> for <c>search_mode</c>.</returns>
    PlanSearchResult Search(PlanSearchRequest request);
}
