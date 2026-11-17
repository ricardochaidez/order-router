using System.Collections.ObjectModel;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Helpers;
using OrderRouter.Domain.Models.PlanSearch;

namespace OrderRouter.Domain.Services.PlanSearch;

/// <summary>
/// Exact search for the fewest-shipment plans (ADR-0003, FR-10, FR-12). It tries covers of size 1, 2, …
/// and stops at the first size that works (one more size when <c>MaxExtraShipments</c> is 1). It returns
/// every plan still in the running, not the first one. Stateless: safe to share and to call concurrently.
/// </summary>
/// <remarks>
/// Guard rails (ADR-0003, NFR-03): more categories than <see cref="PlanSearchLimits.MaxExactCategories"/>, or more
/// checked combinations than <see cref="PlanSearchLimits.NodeBudget"/>, replace the result with the greedy one.
/// Exact and greedy plans are never mixed.
/// </remarks>
public sealed class PlanSearcher
{
    private readonly GreedyPlanSearcher greedy = new();

    /// <summary>Searches with <see cref="PlanSearchLimits.Default"/>.</summary>
    /// <param name="request">Demand, eligible candidates and policy values.</param>
    /// <returns>See <see cref="Search(PlanSearchRequest, PlanSearchLimits)"/>.</returns>
    public PlanSearchResult Search(PlanSearchRequest request) => Search(request, PlanSearchLimits.Default);

    /// <summary>Finds all plans that use the fewest suppliers, within the quality band of their own size, or falls back to greedy.</summary>
    /// <param name="request">Demand, eligible candidates and policy values.</param>
    /// <param name="limits">The guard rails.</param>
    /// <returns>The plans, or an empty list when no cover exists (fail safe: never invent a plan).</returns>
    public PlanSearchResult Search(PlanSearchRequest request, PlanSearchLimits limits)
    {
        var prepared = CandidatePreparer.Prepare(request);
        var categoryCount = prepared.Categories.Count;

        // Guard rail 1: too many categories for the exact search; do not start it.
        if (categoryCount > limits.MaxExactCategories)
        {
            return greedy.Search(prepared) with { FallbackReason = SearchFallbackReason.TooManyCategories };
        }

        var full = categoryCount == PlanSearchRequest.MaxCategories ? ulong.MaxValue : (1UL << categoryCount) - 1;

        if (prepared.Candidates.Aggregate(0UL, (all, c) => all | c.Mask) != full)
        {
            return new PlanSearchResult([], 0);
        }

        var plans = new List<Plan>();
        var budget = new NodeBudget(limits.NodeBudget);
        var minimum = 0;
        var maxSize = Math.Min(categoryCount, prepared.Candidates.Count);

        for (var size = 1; size <= maxSize; size++)
        {
            if (minimum > 0 && size > minimum + request.MaxExtraShipments)
            {
                break;
            }

            var found = new SizeSearch(prepared, full, size, request.SimilarityBand, budget).Run();

            // Guard rail 2: out of budget. Drop every partial exact result and start greedy from scratch.
            if (budget.Exceeded)
            {
                return greedy.Search(prepared) with { FallbackReason = SearchFallbackReason.NodeBudgetExceeded };
            }

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

    /// <summary>Combinations checked so far, shared by every size of one request (cumulative, ADR-0003).</summary>
    private sealed class NodeBudget(int limit)
    {
        private int used;

        public bool Exceeded { get; private set; }

        /// <summary>Counts one node; sets <see cref="Exceeded"/> once the count passes the limit.</summary>
        public void Spend()
        {
            if (++used > limit)
            {
                Exceeded = true;
            }
        }
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
        private readonly ulong full;
        private readonly int size;
        private readonly decimal band;
        private readonly int categoryCount;
        private readonly int totalLines;
        private readonly ulong[] suffixMask;
        private readonly decimal[][] suffixBest;
        private readonly int[] chosen;

        // Scratch buffers reused for every cover. Evaluate and Record run once per complete cover, which is the hot loop
        // of the search, so allocating these per call dominated the cost. Safe to share: a SizeSearch is single-threaded.
        private readonly List<int>[] options;
        private readonly int[] assignment;
        private readonly PreparedCandidate[] assigned;
        private readonly NodeBudget budget;
        private readonly List<Plan> plans = [];
        private decimal bestQuality = -1m;

        public SizeSearch(PreparedSearch prepared, ulong full, int size, decimal band, NodeBudget budget)
        {
            this.budget = budget;
            this.prepared = prepared;
            candidates = prepared.Candidates;
            this.full = full;
            this.size = size;
            this.band = band;
            categoryCount = prepared.Categories.Count;
            totalLines = prepared.LineCounts.Sum();
            chosen = new int[size];
            options = new List<int>[categoryCount];
            assignment = new int[categoryCount];
            assigned = new PreparedCandidate[categoryCount];

            for (var c = 0; c < categoryCount; c++)
            {
                options[c] = [];
            }

            // suffixMask[i]: categories the candidates from i onward can still cover.
            // suffixBest[i][c]: best rating among candidates from i onward for category c (for the bound).
            suffixMask = new ulong[candidates.Count + 1];
            suffixBest = new decimal[candidates.Count + 1][];
            suffixBest[candidates.Count] = new decimal[categoryCount];

            for (var i = candidates.Count - 1; i >= 0; i--)
            {
                suffixMask[i] = suffixMask[i + 1] | candidates[i].Mask;
                suffixBest[i] = (decimal[])suffixBest[i + 1].Clone();

                for (var c = 0; c < categoryCount; c++)
                {
                    if ((candidates[i].Mask & (1UL << c)) != 0 && candidates[i].Rating > suffixBest[i][c])
                    {
                        suffixBest[i][c] = candidates[i].Rating;
                    }
                }
            }
        }

        public List<Plan> Run()
        {
            Extend(0, 0, 0UL);

            return plans;
        }

        private void Extend(int start, int depth, ulong covered)
        {
            budget.Spend();

            if (budget.Exceeded)
            {
                return;
            }

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

                if (budget.Exceeded)
                {
                    return;
                }
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

                    if ((candidate.Mask & (1UL << c)) != 0 && candidate.Rating > best)
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
            for (var c = 0; c < categoryCount; c++)
            {
                var bit = 1UL << c;
                var winners = options[c];
                winners.Clear();

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

                    var comparison = PlanScoring.Compare(candidate, candidates[winners[0]]);

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
            }

            Expand(0);
        }

        private void Expand(int category)
        {
            if (category == categoryCount)
            {
                Record();

                return;
            }

            foreach (var index in options[category])
            {
                assignment[category] = index;
                Expand(category + 1);
            }
        }

        private void Record()
        {
            // Every chosen supplier must receive a category; otherwise this is a smaller cover found earlier.
            // A plain loop, not LINQ: this runs for every cover and must not allocate.
            for (var i = 0; i < chosen.Length; i++)
            {
                if (Array.IndexOf(assignment, chosen[i]) < 0)
                {
                    return;
                }
            }

            var suppliers = new string[categoryCount];

            for (var c = 0; c < categoryCount; c++)
            {
                assigned[c] = candidates[assignment[c]];
                suppliers[c] = assigned[c].SupplierId;
            }

            var (quality, local) = PlanScoring.Score(prepared, assigned);
            plans.Add(new Plan(size, quality, local, suppliers));
            bestQuality = Math.Max(bestQuality, quality);
        }
    }
}
