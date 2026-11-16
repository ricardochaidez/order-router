using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.Routing.PlanSearch;

namespace OrderRouter.UnitTests.Domain.Routing;

/// <summary>Test adapter for the plan-search port: runs the real exact search, counts calls and keeps the last request.</summary>
internal sealed class CountingPlanSearch(PlanSearchResult? forced = null) : IPlanSearchService
{
    private readonly PlanSearcher searcher = new();

    public int CallCount { get; private set; }

    public PlanSearchRequest? LastRequest { get; private set; }

    public PlanSearchResult Search(PlanSearchRequest request)
    {
        CallCount++;
        LastRequest = request;

        return forced ?? searcher.Search(request);
    }
}
