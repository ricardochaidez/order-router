using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Models;
using OrderRouter.Domain.Services.PlanSearch;
using OrderRouter.Domain.Services.Policies;
using OrderRouter.Domain.Services;
using OrderRouter.Infrastructure.Services;
using OrderRouter.Tests.TestData;
using static OrderRouter.Tests.TestData.PlanSearchTestData;

namespace OrderRouter.Tests.Domain.Services;

/// <summary>Search, then ranking keys 2-4, then key 5, on sentinel data. OR-015 wires the same steps into the routing service.</summary>
public class TieBreakPipelineTests
{
    private static readonly string[] tiedSuppliers = ["SUP-T003", "SUP-T004"];

    private readonly PlanSearcher searcher = new();
    private readonly StrictPriorityPolicy policy = new(0.5m);

    private TieBreakResult Route(ITieBreaker tieBreaker, params SupplierCandidate[] candidates)
    {
        var request = new PlanSearchRequest([Demand("cane")], candidates);
        var best = policy.SelectBest(searcher.Search(request).Plans);

        return tieBreaker.Pick(best);
    }

    [Fact]
    public void Route_OneBestPlan_NeverUsesRandomnessAndIsRepeatable()
    {
        var source = new FixedRandomSource(1);
        var tieBreaker = new UniformRandomTieBreaker(source);
        SupplierCandidate[] candidates = [Local("SUP-T003", 8m, "cane"), Local("SUP-X", 7m, "cane")];

        var suppliers = Enumerable.Range(0, 20).Select(_ => Ids(Route(tieBreaker, candidates).Chosen)[0]).Distinct().ToList();

        Assert.Equal(["SUP-T003"], suppliers);
        Assert.Equal(0, source.CallCount);
    }

    [Fact]
    public void Route_FullTieWithRealSource_ChoosesEachTiedSupplierAndNoOther()
    {
        var tieBreaker = new UniformRandomTieBreaker(new SystemRandomSource());
        SupplierCandidate[] candidates = [Local("SUP-T003", 8m, "cane"), Local("SUP-T004", 8m, "cane")];

        var chosen = Enumerable.Range(0, 200).Select(_ => Ids(Route(tieBreaker, candidates).Chosen)[0]).ToHashSet();

        Assert.Equal(tiedSuppliers, chosen.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Route_ThreeWayTieWithLowerRatedSupplier_NeverChoosesLowerRated()
    {
        var tieBreaker = new UniformRandomTieBreaker(new SystemRandomSource());
        SupplierCandidate[] candidates =
        [
            Local("SUP-T003", 8m, "cane"),
            Local("SUP-T004", 8m, "cane"),
            Local("SUP-T005", 8m, "cane"),
            Local("SUP-LOW", 7m, "cane"),
        ];

        var chosen = Enumerable.Range(0, 200).Select(_ => Ids(Route(tieBreaker, candidates).Chosen)[0]).ToHashSet();

        Assert.Equal(["SUP-T003", "SUP-T004", "SUP-T005"], chosen.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Route_FullTie_ReturnsTiedCandidatesAndThePick()
    {
        var result = Route(
            new UniformRandomTieBreaker(new FixedRandomSource(1)),
            Local("SUP-T003", 8m, "cane"),
            Local("SUP-T004", 8m, "cane"));

        Assert.True(result.HasTie);
        Assert.Equal(tiedSuppliers, result.TiedCandidates.Select(p => Ids(p)[0]));
        Assert.Equal(["SUP-T004"], Ids(result.Chosen));
        Assert.Contains(result.Chosen, result.TiedCandidates);
    }
}
