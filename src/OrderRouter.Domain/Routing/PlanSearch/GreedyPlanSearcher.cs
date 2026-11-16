using System.Collections.ObjectModel;
using System.Numerics;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.Domain.Routing.PlanSearch;

/// <summary>
/// Fallback set cover used when the exact search would be too slow (ADR-0003, NFR-03). Repeatedly takes the
/// supplier covering the most uncovered categories. Returns one deterministic plan that is valid but not
/// guaranteed to use the fewest shipments. <c>SimilarityBand</c> and <c>MaxExtraShipments</c> are ignored.
/// Stateless: safe to share and to call concurrently.
/// </summary>
public sealed class GreedyPlanSearcher
{
    /// <summary>Builds one covering plan.</summary>
    /// <param name="request">Demand, eligible candidates and policy values (only demand and candidates are used).</param>
    /// <returns>A <see cref="SearchMode.Greedy"/> result; no plans when the candidates cannot cover every category (fail safe).</returns>
    public PlanSearchResult Search(PlanSearchRequest request) => Search(CandidatePreparer.Prepare(request));

    internal PlanSearchResult Search(PreparedSearch prepared)
    {
        var categoryCount = prepared.Categories.Count;
        var full = categoryCount == 32 ? uint.MaxValue : (1u << categoryCount) - 1;
        var uncovered = full;
        var chosen = new List<PreparedCandidate>();

        while (uncovered != 0)
        {
            PreparedCandidate? best = null;
            var bestGain = 0;

            // Candidates are ordered by supplier id, so keeping the first of equals makes the lowest id win the last tie.
            foreach (var candidate in prepared.Candidates)
            {
                var gain = BitOperations.PopCount(candidate.Mask & uncovered);

                if (gain == 0)
                {
                    continue;
                }

                if (best is null || gain > bestGain || (gain == bestGain && PlanScoring.Compare(candidate, best) > 0))
                {
                    best = candidate;
                    bestGain = gain;
                }
            }

            if (best is null)
            {
                return new PlanSearchResult([], 0, SearchMode.Greedy);
            }

            chosen.Add(best);
            uncovered &= ~best.Mask;
        }

        var perCategory = new PreparedCandidate[categoryCount];

        for (var c = 0; c < categoryCount; c++)
        {
            var bit = 1u << c;
            PreparedCandidate? winner = null;

            foreach (var candidate in chosen.OrderBy(x => x.SupplierId, StringComparer.Ordinal))
            {
                if ((candidate.Mask & bit) != 0 && (winner is null || PlanScoring.Compare(candidate, winner) > 0))
                {
                    winner = candidate;
                }
            }

            perCategory[c] = winner!;
        }

        var (quality, local) = PlanScoring.Score(prepared, perCategory);
        var assignments = new Dictionary<Category, string>();

        for (var c = 0; c < categoryCount; c++)
        {
            assignments[prepared.Categories[c]] = perCategory[c].SupplierId;
        }

        // A supplier chosen early can lose every category to a better one later; the shipment count is distinct suppliers.
        var shipments = assignments.Values.Distinct(StringComparer.Ordinal).Count();
        var plan = new RoutingPlan(new ReadOnlyDictionary<Category, string>(assignments), shipments, quality, local);

        return new PlanSearchResult([plan], shipments, SearchMode.Greedy);
    }
}
