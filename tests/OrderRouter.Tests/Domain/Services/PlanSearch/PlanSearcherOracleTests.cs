using OrderRouter.Domain.Entities.Eligibility;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Helpers;
using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Services.PlanSearch;

namespace OrderRouter.Tests.Domain.Services.PlanSearch;

/// <summary>
/// Proves the pruning and bound never drop a plan: the searcher is compared with a brute-force
/// enumeration of every category-to-supplier assignment (AC9, working rule "bound is optimisation only").
/// </summary>
public class PlanSearcherOracleTests
{
    public static TheoryData<int> Seeds => [.. Enumerable.Range(1, 300)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Search_RandomCases_MatchesBruteForce(int seed)
    {
        var random = new Random(seed);
        var categoryCount = random.Next(1, 6);
        var candidateCount = random.Next(1, 8);
        var demand = Enumerable.Range(0, categoryCount).Select(i => new CategoryDemand(new Category($"c{i}"), random.Next(1, 4))).ToList();
        var candidates = new List<SupplierCandidate>();

        for (var i = 0; i < candidateCount; i++)
        {
            var categories = demand.Where(_ => random.Next(2) == 0).Select(d => d.Category).ToList();

            if (categories.Count == 0)
            {
                categories.Add(demand[random.Next(categoryCount)].Category);
            }

            candidates.Add(new SupplierCandidate(
                $"S{i}",
                categories,
                random.Next(2) == 0 ? FulfillmentMode.Local : FulfillmentMode.Mail,
                random.Next(4, 10) + (random.Next(3) * 0.25m)));
        }

        var band = random.Next(3) * 0.5m;
        var extra = random.Next(2);
        var request = new PlanSearchRequest(demand, candidates, band, extra);

        var actual = new PlanSearcher().Search(request).Plans
            .Select(p => (p.ShipmentCount, p.Quality, Key(p)))
            .OrderBy(p => p.ShipmentCount).ThenBy(p => p.Item3, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(BruteForce(request), actual);
    }

    private static string Key(RoutingPlan plan)
        => string.Join(',', plan.Assignments.OrderBy(a => a.Key.Value, StringComparer.Ordinal).Select(a => $"{a.Key}={a.Value}"));

    private static List<(int Size, decimal Quality, string Key)> BruteForce(PlanSearchRequest request)
    {
        var prepared = CandidatePreparer.Prepare(request);
        var categories = prepared.Categories;
        var candidates = prepared.Candidates;
        var total = prepared.LineCounts.Sum();
        var all = new List<(int Size, decimal Quality, string Key)>();
        var assignment = new int[categories.Count];

        void Walk(int category)
        {
            if (category == categories.Count)
            {
                var used = assignment.Distinct().ToList();

                // Same assignment rule as the searcher: each category goes to a best (rating, then local) of the used suppliers able to take it.
                for (var c = 0; c < categories.Count; c++)
                {
                    var bit = 1u << c;
                    var chosen = candidates[assignment[c]];

                    if (used.Any(u => (candidates[u].Mask & bit) != 0 && Rank(candidates[u]).CompareTo(Rank(chosen)) > 0))
                    {
                        return;
                    }
                }

                var quality = Enumerable.Range(0, categories.Count).Sum(c => candidates[assignment[c]].Rating * prepared.LineCounts[c]) / total;
                var key = string.Join(',', Enumerable.Range(0, categories.Count).Select(c => $"{categories[c]}={candidates[assignment[c]].SupplierId}"));
                all.Add((used.Count, quality, key));

                return;
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                if ((candidates[i].Mask & (1u << category)) != 0)
                {
                    assignment[category] = i;
                    Walk(category + 1);
                }
            }
        }

        Walk(0);

        if (all.Count == 0)
        {
            return [];
        }

        var minimum = all.Min(p => p.Size);
        var kept = new List<(int Size, decimal Quality, string Key)>();

        foreach (var size in all.Select(p => p.Size).Distinct().Where(s => s <= minimum + request.MaxExtraShipments))
        {
            var ofSize = all.Where(p => p.Size == size).ToList();
            var best = ofSize.Max(p => p.Quality);
            kept.AddRange(ofSize.Where(p => p.Quality >= best - request.SimilarityBand));
        }

        return [.. kept.OrderBy(p => p.Size).ThenBy(p => p.Key, StringComparer.Ordinal)];
    }

    private static (decimal Rating, int Local) Rank(PreparedCandidate candidate)
        => (candidate.Rating, candidate.Mode == FulfillmentMode.Local ? 1 : 0);
}
