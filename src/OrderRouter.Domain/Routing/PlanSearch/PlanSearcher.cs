using System.Collections.ObjectModel;
using OrderRouter.Domain.Eligibility;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.Domain.Routing.PlanSearch;

/// <summary>
/// Exact search for the fewest-shipment plans (ADR-0003, FR-10, FR-12). It tries covers of size 1, 2, …
/// and stops at the first size that works (one more size when <c>MaxExtraShipments</c> is 1). It returns
/// every plan still in the running, not the first one. Stateless: safe to share and to call concurrently.
/// </summary>
/// <remarks>
/// Guard rails (work budget, the 8-category limit, greedy fallback) belong to OR-012 and wrap this class.
/// </remarks>
public sealed class PlanSearcher
{
    /// <summary>Finds all plans that use the fewest suppliers, within the quality band of their own size.</summary>
    /// <param name="request">Demand, eligible candidates and policy values.</param>
    /// <returns>The plans, or an empty list when no cover exists (fail safe: never invent a plan).</returns>
    public PlanSearchResult Search(PlanSearchRequest request)
    {
        var prepared = CandidatePreparer.Prepare(request);
        var categoryCount = prepared.Categories.Count;
        var full = categoryCount == 32 ? uint.MaxValue : (1u << categoryCount) - 1;

        if (prepared.Candidates.Aggregate(0u, (all, c) => all | c.Mask) != full)
        {
            return new PlanSearchResult([], 0);
        }

        var plans = new List<Plan>();
        var minimum = 0;
        var maxSize = Math.Min(categoryCount, prepared.Candidates.Count);

        for (var size = 1; size <= maxSize; size++)
        {
            if (minimum > 0 && size > minimum + request.MaxExtraShipments)
            {
                break;
            }

            var found = new SizeSearch(prepared, full, size, request.SimilarityBand).Run();

            if (found.Count == 0)
            {
                continue;
            }

            minimum = minimum == 0 ? size : minimum;

            // FR-12: the band compares plans of the same shipment count only, never across counts (AC9).
            var best = found.Max(p => p.Quality);
            plans.AddRange(found.Where(p => p.Quality >= best - request.SimilarityBand));
        }

        var ordered = plans
            .OrderBy(p => p.Size)
            .ThenBy(p => p.SortKey, StringComparer.Ordinal)
            .Select(p => p.ToRoutingPlan(prepared))
            .ToList();

        return new PlanSearchResult(ordered.AsReadOnly(), minimum);
    }

    private sealed record Plan(int Size, decimal Quality, int LocalLines, string[] Suppliers)
    {
        // Ordinal supplier ids in category order: a stable order for tests and the later tie-break.
        public string SortKey => string.Join('\u0001', Suppliers);

        public RoutingPlan ToRoutingPlan(PreparedSearch prepared)
        {
            var assignments = new Dictionary<Category, string>();

            for (var index = 0; index < Suppliers.Length; index++)
            {
                assignments[prepared.Categories[index]] = Suppliers[index];
            }

            return new RoutingPlan(new ReadOnlyDictionary<Category, string>(assignments), Size, Quality, LocalLines);
        }
    }

    /// <summary>The search for covers of exactly one size.</summary>
    private sealed class SizeSearch
    {
        private readonly PreparedSearch prepared;
        private readonly IReadOnlyList<PreparedCandidate> candidates;
        private readonly uint full;
        private readonly int size;
        private readonly decimal band;
        private readonly int categoryCount;
        private readonly int totalLines;
        private readonly uint[] suffixMask;
        private readonly decimal[][] suffixBest;
        private readonly int[] chosen;
        private readonly List<Plan> plans = [];
        private decimal bestQuality = -1m;

        public SizeSearch(PreparedSearch prepared, uint full, int size, decimal band)
        {
            this.prepared = prepared;
            candidates = prepared.Candidates;
            this.full = full;
            this.size = size;
            this.band = band;
            categoryCount = prepared.Categories.Count;
            totalLines = prepared.LineCounts.Sum();
            chosen = new int[size];

            // suffixMask[i]: categories the candidates from i onward can still cover.
            // suffixBest[i][c]: best rating among candidates from i onward for category c (for the bound).
            suffixMask = new uint[candidates.Count + 1];
            suffixBest = new decimal[candidates.Count + 1][];
            suffixBest[candidates.Count] = new decimal[categoryCount];

            for (var i = candidates.Count - 1; i >= 0; i--)
            {
                suffixMask[i] = suffixMask[i + 1] | candidates[i].Mask;
                suffixBest[i] = (decimal[])suffixBest[i + 1].Clone();

                for (var c = 0; c < categoryCount; c++)
                {
                    if ((candidates[i].Mask & (1u << c)) != 0 && candidates[i].Rating > suffixBest[i][c])
                    {
                        suffixBest[i][c] = candidates[i].Rating;
                    }
                }
            }
        }

        public List<Plan> Run()
        {
            Extend(0, 0, 0u);

            return plans;
        }

        private void Extend(int start, int depth, uint covered)
        {
            if (depth == size)
            {
                if (covered == full)
                {
                    Evaluate();
                }

                return;
            }

            // Not enough candidates left, or what is left cannot complete the cover.
            if (candidates.Count - start < size - depth || (covered | suffixMask[start]) != full)
            {
                return;
            }

            // Optimistic bound (optimisation only): if even the best possible ratings leave the plan below
            // the band of the best found at this size, no completion can survive the band filter.
            // Applied within one shipment count only (AC8, AC9); it never changes the result.
            if (bestQuality >= 0m && UpperBound(start, depth) < bestQuality - band)
            {
                return;
            }

            for (var i = start; i < candidates.Count; i++)
            {
                chosen[depth] = i;
                Extend(i + 1, depth + 1, covered | candidates[i].Mask);
            }
        }

        private decimal UpperBound(int start, int depth)
        {
            var sum = 0m;

            for (var c = 0; c < categoryCount; c++)
            {
                var best = suffixBest[start][c];

                for (var d = 0; d < depth; d++)
                {
                    var candidate = candidates[chosen[d]];

                    if ((candidate.Mask & (1u << c)) != 0 && candidate.Rating > best)
                    {
                        best = candidate.Rating;
                    }
                }

                sum += best * prepared.LineCounts[c];
            }

            return sum / totalLines;
        }

        private void Evaluate()
        {
            // Per category: the chosen supplier with the higher rating, then local; exact ties give separate plans (ADR-0003).
            var options = new List<int>[categoryCount];

            for (var c = 0; c < categoryCount; c++)
            {
                var bit = 1u << c;
                var winners = new List<int>();

                foreach (var index in chosen)
                {
                    var candidate = candidates[index];

                    if ((candidate.Mask & bit) == 0)
                    {
                        continue;
                    }

                    if (winners.Count == 0)
                    {
                        winners.Add(index);
                        continue;
                    }

                    var comparison = Compare(candidate, candidates[winners[0]]);

                    if (comparison > 0)
                    {
                        winners.Clear();
                        winners.Add(index);
                    }
                    else if (comparison == 0)
                    {
                        winners.Add(index);
                    }
                }

                options[c] = winners;
            }

            var assignment = new int[categoryCount];
            Expand(options, assignment, 0);
        }

        private static int Compare(PreparedCandidate left, PreparedCandidate right)
        {
            var byRating = left.Rating.CompareTo(right.Rating);

            return byRating != 0
                ? byRating
                : (left.Mode == FulfillmentMode.Local).CompareTo(right.Mode == FulfillmentMode.Local);
        }

        private void Expand(List<int>[] options, int[] assignment, int category)
        {
            if (category == categoryCount)
            {
                Record(assignment);

                return;
            }

            foreach (var index in options[category])
            {
                assignment[category] = index;
                Expand(options, assignment, category + 1);
            }
        }

        private void Record(int[] assignment)
        {
            // Every chosen supplier must receive a category; otherwise this is a smaller cover found earlier.
            if (chosen.Any(index => !assignment.Contains(index)))
            {
                return;
            }

            var weighted = 0m;
            var local = 0;

            for (var c = 0; c < categoryCount; c++)
            {
                var candidate = candidates[assignment[c]];
                weighted += candidate.Rating * prepared.LineCounts[c];

                if (candidate.Mode == FulfillmentMode.Local)
                {
                    local += prepared.LineCounts[c];
                }
            }

            var quality = weighted / totalLines;
            var suppliers = assignment.Select(index => candidates[index].SupplierId).ToArray();
            plans.Add(new Plan(size, quality, local, suppliers));
            bestQuality = Math.Max(bestQuality, quality);
        }
    }
}
