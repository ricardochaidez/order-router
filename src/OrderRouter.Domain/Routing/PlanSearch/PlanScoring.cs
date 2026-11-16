using OrderRouter.Domain.Eligibility;

namespace OrderRouter.Domain.Routing.PlanSearch;

/// <summary>
/// Rules shared by the exact and greedy searches, so both rank suppliers and score plans identically (ADR-0003).
/// </summary>
internal static class PlanScoring
{
    /// <summary>Orders two suppliers for one category: higher rating wins, then local over mail. 0 means a true tie.</summary>
    public static int Compare(PreparedCandidate left, PreparedCandidate right)
    {
        var byRating = left.Rating.CompareTo(right.Rating);

        return byRating != 0
            ? byRating
            : (left.Mode == FulfillmentMode.Local).CompareTo(right.Mode == FulfillmentMode.Local);
    }

    /// <summary>Line-weighted mean rating (FR-12) and the number of lines delivered locally for one assignment.</summary>
    /// <param name="prepared">The prepared search (line counts per category).</param>
    /// <param name="perCategory">The supplier assigned to each category, same index as the categories.</param>
    public static (decimal Quality, int LocalLines) Score(PreparedSearch prepared, IReadOnlyList<PreparedCandidate> perCategory)
    {
        var weighted = 0m;
        var local = 0;
        var totalLines = 0;

        for (var c = 0; c < perCategory.Count; c++)
        {
            var lines = prepared.LineCounts[c];
            weighted += perCategory[c].Rating * lines;
            totalLines += lines;

            if (perCategory[c].Mode == FulfillmentMode.Local)
            {
                local += lines;
            }
        }

        return (weighted / totalLines, local);
    }
}
