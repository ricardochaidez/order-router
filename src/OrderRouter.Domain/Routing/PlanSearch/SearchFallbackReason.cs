namespace OrderRouter.Domain.Routing.PlanSearch;

/// <summary>Why the exact search was replaced by the greedy fallback (ADR-0003 guard rails).</summary>
public enum SearchFallbackReason
{
    /// <summary>The order has more distinct categories than <see cref="PlanSearchLimits.MaxExactCategories"/>.</summary>
    TooManyCategories,

    /// <summary>The exact search checked more combinations than <see cref="PlanSearchLimits.NodeBudget"/>.</summary>
    NodeBudgetExceeded,
}
