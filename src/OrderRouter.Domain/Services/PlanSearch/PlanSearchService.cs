using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Sections;

namespace OrderRouter.Domain.Services.PlanSearch;

/// <summary>
/// Applies the configured limits to the domain search and logs one warning when greedy runs (ADR-0003).
/// The domain stays pure; configuration and logging live here.
/// </summary>
public sealed partial class PlanSearchService(IOptions<PlanSearchOptions> options, ILogger<PlanSearchService> logger) : IPlanSearchService
{
    private readonly PlanSearcher searcher = new();
    private readonly PlanSearchLimits limits = options.Value.ToLimits();

    /// <inheritdoc />
    public PlanSearchResult Search(PlanSearchRequest request)
    {
        var result = searcher.Search(request, limits);

        if (result.Mode is SearchMode.Greedy)
        {
            // Counts and limits only: no ZIP, order id or caller (NFR-11).
            LogGreedyFallback(result.FallbackReason, request.Demand.Count, limits.MaxExactCategories, limits.NodeBudget);
        }

        return result;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Plan search fell back to greedy ({Reason}): {CategoryCount} categories, MaxExactCategories {MaxExactCategories}, NodeBudget {NodeBudget}.")]
    private partial void LogGreedyFallback(SearchFallbackReason? reason, int categoryCount, int maxExactCategories, int nodeBudget);
}
