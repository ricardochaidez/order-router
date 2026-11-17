
namespace OrderRouter.Domain.Models.PlanSearch;

/// <summary>
/// The two guard rails of the exact search (ADR-0003, NFR-03, NFR-05). Immutable and validated on creation,
/// so an invalid limit can never reach the search.
/// </summary>
public sealed record PlanSearchLimits
{
    /// <summary>Default for <see cref="MaxExactCategories"/>.</summary>
    public const int DefaultMaxExactCategories = 8;

    /// <summary>Default for <see cref="NodeBudget"/>.</summary>
    public const int DefaultNodeBudget = 200_000;

    /// <summary>The limits used when nothing is configured.</summary>
    public static PlanSearchLimits Default { get; } = new(DefaultMaxExactCategories, DefaultNodeBudget);

    /// <summary>Creates limits, reporting every invalid value in one exception.</summary>
    /// <param name="maxExactCategories">More distinct categories than this goes straight to greedy; 1 to <see cref="PlanSearchRequest.MaxCategories"/>.</param>
    /// <param name="nodeBudget">Most combinations the exact search may check; 1 or more.</param>
    /// <exception cref="DomainValidationException">One or more values are invalid.</exception>
    public PlanSearchLimits(int maxExactCategories, int nodeBudget)
    {
        var errors = Validate(maxExactCategories, nodeBudget);

        if (errors.Count > 0)
        {
            throw new DomainValidationException($"Invalid plan search limits: {string.Join("; ", errors)}.");
        }

        MaxExactCategories = maxExactCategories;
        NodeBudget = nodeBudget;
    }

    /// <summary>Most distinct categories the exact search accepts.</summary>
    public int MaxExactCategories { get; }

    /// <summary>Most combinations the exact search may check before falling back.</summary>
    public int NodeBudget { get; }

    /// <summary>Lists every problem with the values; empty when valid. Shared with configuration validation (single source of rules).</summary>
    /// <param name="maxExactCategories">Candidate value.</param>
    /// <param name="nodeBudget">Candidate value.</param>
    /// <returns>One message per invalid value.</returns>
    public static IReadOnlyList<string> Validate(int maxExactCategories, int nodeBudget)
    {
        var errors = new List<string>();

        if (maxExactCategories is < 1 or > PlanSearchRequest.MaxCategories)
        {
            errors.Add($"max exact categories must be between 1 and {PlanSearchRequest.MaxCategories} (received {maxExactCategories})");
        }

        if (nodeBudget < 1)
        {
            errors.Add($"node budget must be at least 1 (received {nodeBudget})");
        }

        return errors;
    }
}
