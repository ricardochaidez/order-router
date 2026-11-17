using OrderRouter.Domain.Models.PlanSearch;

namespace OrderRouter.Domain.Sections;

/// <summary>
/// The <c>PlanSearch</c> configuration section (NFR-05, ADR-0003). Overridable per environment, for example
/// <c>PlanSearch__NodeBudget</c>. Validated at startup by <see cref="PlanSearchOptionsValidator"/>.
/// </summary>
public sealed class PlanSearchOptions
{
    /// <summary>Name of the configuration section.</summary>
    public static readonly string Name = "PlanSearch";

    /// <summary>More distinct categories than this goes straight to the greedy fallback.</summary>
    public int MaxExactCategories { get; set; } = PlanSearchLimits.DefaultMaxExactCategories;

    /// <summary>Most combinations the exact search may check before the greedy fallback runs.</summary>
    public int NodeBudget { get; set; } = PlanSearchLimits.DefaultNodeBudget;

    /// <summary>Converts to the validated domain value object.</summary>
    /// <exception cref="OrderRouter.Domain.Models.DomainValidationException">A value is out of range.</exception>
    public PlanSearchLimits ToLimits() => new(MaxExactCategories, NodeBudget);
}
