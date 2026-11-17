using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Models;

namespace OrderRouter.Domain.Services;

/// <summary>
/// Core tie-break (key 5, FR-13) and the EXP-4 control arm: on a full tie, picks the plan whose sorted supplier IDs come first in ordinal order, so
/// the same input always gives the same plan and needs no random source. A plan that differs only in how the
/// categories are split between the same suppliers keeps the search order (first one wins).
/// </summary>
public sealed class DeterministicTieBreaker : ITieBreaker
{
    private DeterministicTieBreaker()
    {
    }

    /// <summary>The shared instance; the tie-breaker has no state.</summary>
    public static DeterministicTieBreaker Instance { get; } = new();

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="tied"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tied"/> is empty; the caller handles "no plan" first, and a plan is never invented.</exception>
    public TieBreakResult Pick(IReadOnlyList<RoutingPlan> tied)
    {
        ArgumentNullException.ThrowIfNull(tied);

        if (tied.Count == 0)
        {
            throw new ArgumentException("At least one plan is required.", nameof(tied));
        }

        var chosen = tied[0];
        var chosenKey = Key(chosen);

        for (var i = 1; i < tied.Count; i++)
        {
            var key = Key(tied[i]);

            // Strictly less, so an equal key keeps the earlier plan.
            if (Compare(key, chosenKey) < 0)
            {
                chosen = tied[i];
                chosenKey = key;
            }
        }

        return new TieBreakResult(chosen, tied);
    }

    private static string[] Key(RoutingPlan plan)
        => [.. plan.Assignments.Values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static int Compare(string[] left, string[] right)
    {
        var length = Math.Min(left.Length, right.Length);

        for (var i = 0; i < length; i++)
        {
            var result = string.CompareOrdinal(left[i], right[i]);

            if (result != 0)
            {
                return result;
            }
        }

        return left.Length.CompareTo(right.Length);
    }
}
