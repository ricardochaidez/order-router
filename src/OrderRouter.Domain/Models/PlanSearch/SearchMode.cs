namespace OrderRouter.Domain.Models.PlanSearch;

/// <summary>Which algorithm produced a plan search result (ADR-0003). Recorded later as <c>search_mode</c>.</summary>
public enum SearchMode
{
    /// <summary>Exhaustive search: the plans use the fewest possible shipments.</summary>
    Exact,

    /// <summary>Greedy set cover: a valid cover, not guaranteed to be the smallest.</summary>
    Greedy,
}
